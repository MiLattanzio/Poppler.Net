using System.Text;

namespace Poppler.Text;

internal static class PdfTextLayoutEngine
{
    public static IReadOnlyList<TextBox> Order(
        IReadOnlyList<TextBox> boxes,
        TextLayout layout)
    {
        if (layout == TextLayout.RawOrder || boxes.Count < 2)
            return boxes;
        return layout == TextLayout.NonRawNonPhysical
            ? OrderByColumns(boxes)
            : OrderPhysical(boxes);
    }

    public static string Join(IReadOnlyList<TextBox> boxes, TextLayout layout)
    {
        if (boxes.Count == 0)
            return "";
        if (layout == TextLayout.RawOrder)
        {
            return string.Concat(
                    boxes.Select(box => box.Text + (box.HasSpaceAfter ? " " : "")))
                .Trim();
        }

        var builder = new StringBuilder();
        TextBox? previous = null;
        foreach (TextBox box in boxes)
        {
            if (previous is not null)
            {
                if (StartsNewLine(previous, box))
                {
                    TrimTrailingSpaces(builder);
                    builder.AppendLine();
                }
                else if (NeedsSpace(previous, box) &&
                         builder.Length > 0 &&
                         builder[^1] is not '\n' and not ' ')
                {
                    builder.Append(' ');
                }
            }

            builder.Append(box.Text);
            if (box.HasSpaceAfter)
                builder.Append(' ');
            previous = box;
        }

        return builder.ToString().Trim();
    }

    public static bool ContainsStrongRightToLeft(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            int value = rune.Value;
            if (value is >= 0x0590 and <= 0x08FF or
                >= 0xFB1D and <= 0xFDFF or
                >= 0xFE70 and <= 0xFEFF or
                >= 0x10800 and <= 0x10FFF)
            {
                return true;
            }

            if (Rune.IsLetter(rune))
                return false;
        }

        return false;
    }

    private static IReadOnlyList<TextBox> OrderPhysical(IReadOnlyList<TextBox> boxes)
    {
        var horizontal = boxes
            .Where(box => box.WritingMode == FontWritingMode.Horizontal)
            .OrderByDescending(box => box.BoundingBox.Bottom)
            .ToList();
        var lines = new List<List<TextBox>>();
        foreach (TextBox box in horizontal)
        {
            List<TextBox>? line = lines.FirstOrDefault(candidate =>
                Math.Abs(candidate[0].BoundingBox.Bottom - box.BoundingBox.Bottom) <=
                Math.Max(1.5, Math.Max(candidate[0].FontSize, box.FontSize) * 0.35));
            if (line is null)
            {
                line = new List<TextBox>();
                lines.Add(line);
            }

            line.Add(box);
        }

        var result = new List<TextBox>(boxes.Count);
        foreach (List<TextBox> line in lines.OrderByDescending(line => line[0].BoundingBox.Bottom))
        {
            bool rightToLeft =
                line.Count(box => box.IsRightToLeft) > line.Count / 2;
            result.AddRange(rightToLeft
                ? line.OrderByDescending(box => box.BoundingBox.Right)
                : line.OrderBy(box => box.BoundingBox.Left));
        }

        result.AddRange(
            boxes
                .Where(box => box.WritingMode == FontWritingMode.Vertical)
                .OrderByDescending(box => box.BoundingBox.Right)
                .ThenByDescending(box => box.BoundingBox.Top));
        return result;
    }

    private static IReadOnlyList<TextBox> OrderByColumns(IReadOnlyList<TextBox> boxes)
    {
        if (boxes.Any(box => box.WritingMode == FontWritingMode.Vertical) || boxes.Count < 4)
            return OrderPhysical(boxes);

        TextBox[] byCenter = boxes
            .OrderBy(box => (box.BoundingBox.Left + box.BoundingBox.Right) / 2)
            .ToArray();
        double typicalFontSize = byCenter.Select(box => box.FontSize).Order().ElementAt(byCenter.Length / 2);
        (int Index, double Gap) best = (-1, 0);
        for (int index = 1; index < byCenter.Length; index++)
        {
            double previousCenter =
                (byCenter[index - 1].BoundingBox.Left + byCenter[index - 1].BoundingBox.Right) / 2;
            double currentCenter =
                (byCenter[index].BoundingBox.Left + byCenter[index].BoundingBox.Right) / 2;
            double gap = currentCenter - previousCenter;
            if (gap <= Math.Max(typicalFontSize * 4, best.Gap))
                continue;

            TextBox[] left = byCenter[..index];
            TextBox[] right = byCenter[index..];
            if (HasMultipleRows(left) && HasMultipleRows(right))
                best = (index, gap);
        }

        if (best.Index < 0)
            return OrderPhysical(boxes);
        var result = new List<TextBox>(boxes.Count);
        result.AddRange(OrderPhysical(byCenter[..best.Index]));
        result.AddRange(OrderPhysical(byCenter[best.Index..]));
        return result;
    }

    private static bool HasMultipleRows(IReadOnlyList<TextBox> boxes)
    {
        if (boxes.Count < 2)
            return false;
        double first = boxes[0].BoundingBox.Bottom;
        return boxes.Skip(1).Any(box =>
            Math.Abs(box.BoundingBox.Bottom - first) >
            Math.Max(2, Math.Max(box.FontSize, boxes[0].FontSize) * 0.5));
    }

    private static bool StartsNewLine(TextBox previous, TextBox current)
    {
        if (previous.WritingMode != current.WritingMode)
            return true;
        if (current.WritingMode == FontWritingMode.Vertical)
        {
            return Math.Abs(current.BoundingBox.Right - previous.BoundingBox.Right) >
                   Math.Max(2, Math.Max(previous.FontSize, current.FontSize) * 0.6);
        }

        return Math.Abs(current.BoundingBox.Bottom - previous.BoundingBox.Bottom) >
               Math.Max(2, Math.Max(previous.FontSize, current.FontSize) * 0.6);
    }

    private static bool NeedsSpace(TextBox previous, TextBox current)
    {
        if (previous.HasSpaceAfter)
            return true;
        if (current.WritingMode == FontWritingMode.Vertical)
        {
            double gap = previous.BoundingBox.Bottom - current.BoundingBox.Top;
            return gap > Math.Max(1, Math.Min(previous.FontSize, current.FontSize) * 0.15);
        }

        double horizontalGap = current.IsRightToLeft
            ? previous.BoundingBox.Left - current.BoundingBox.Right
            : current.BoundingBox.Left - previous.BoundingBox.Right;
        return horizontalGap >
               Math.Max(1, Math.Min(previous.FontSize, current.FontSize) * 0.15);
    }

    private static void TrimTrailingSpaces(StringBuilder builder)
    {
        while (builder.Length > 0 && builder[^1] == ' ')
            builder.Length--;
    }
}
