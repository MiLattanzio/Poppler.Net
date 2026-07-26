using System.Text;
using Poppler.Core;

namespace Poppler.DocumentModel;

internal sealed class PageLabelTree
{
    private readonly SortedDictionary<int, LabelDefinition> _definitions = new();

    public PageLabelTree(PdfObject? root, PdfDocumentCore document)
    {
        if (root is not null)
            ReadNode(root, document, 0);
    }

    public string GetLabel(int pageIndex)
    {
        KeyValuePair<int, LabelDefinition>? selected = null;
        foreach (KeyValuePair<int, LabelDefinition> definition in _definitions)
        {
            if (definition.Key > pageIndex)
                break;
            selected = definition;
        }

        if (selected is null)
            return (pageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

        int number = selected.Value.Value.Start + pageIndex - selected.Value.Key;
        return selected.Value.Value.Prefix + Format(number, selected.Value.Value.Style);
    }

    private void ReadNode(PdfObject nodeObject, PdfDocumentCore document, int depth)
    {
        if (depth > document.Options.MaximumTreeDepth)
            throw new PdfLimitException("Page-label number tree is too deep.");
        PdfDictionary? node = nodeObject.AsDictionary(document);
        if (node is null)
            return;

        PdfArray? numbers = node.GetValueOrNull("Nums").AsArray(document);
        if (numbers is not null)
        {
            for (int index = 0; index + 1 < numbers.Count; index += 2)
            {
                int? pageIndex = numbers[index].AsInteger(document);
                PdfDictionary? definition = numbers[index + 1].AsDictionary(document);
                if (pageIndex is null || pageIndex < 0 || definition is null)
                    continue;
                string prefix =
                    (definition.GetValueOrNull("P")?.Resolve(document) as PdfString)?.Text ?? "";
                string? style = definition.GetValueOrNull("S").AsName(document);
                int start = definition.GetValueOrNull("St").AsInteger(document) ?? 1;
                _definitions[pageIndex.Value] = new LabelDefinition(prefix, style, Math.Max(1, start));
            }
        }

        PdfArray? kids = node.GetValueOrNull("Kids").AsArray(document);
        if (kids is not null)
        {
            foreach (PdfObject kid in kids)
                ReadNode(kid, document, depth + 1);
        }
    }

    private static string Format(int value, string? style) => style switch
    {
        "D" => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "R" => ToRoman(value).ToUpperInvariant(),
        "r" => ToRoman(value),
        "A" => ToLetters(value).ToUpperInvariant(),
        "a" => ToLetters(value),
        _ => ""
    };

    private static string ToRoman(int value)
    {
        if (value <= 0)
            return "";
        (int Value, string Digits)[] symbols =
        {
            (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"),
            (100, "c"), (90, "xc"), (50, "l"), (40, "xl"),
            (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")
        };
        var builder = new StringBuilder();
        foreach ((int amount, string digits) in symbols)
        {
            while (value >= amount)
            {
                builder.Append(digits);
                value -= amount;
            }
        }

        return builder.ToString();
    }

    private static string ToLetters(int value)
    {
        if (value <= 0)
            return "";
        int repeated = (value - 1) / 26 + 1;
        char letter = (char)('a' + (value - 1) % 26);
        return new string(letter, repeated);
    }

    private sealed record LabelDefinition(string Prefix, string? Style, int Start);
}
