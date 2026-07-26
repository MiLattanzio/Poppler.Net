using Poppler.Core;

namespace Poppler.Text;

internal sealed class PdfCidMetrics
{
    private readonly List<HorizontalRange> _horizontal = new();
    private readonly List<VerticalRange> _vertical = new();

    public PdfCidMetrics(PdfDictionary dictionary, PdfDocumentCore document)
    {
        DefaultWidth = dictionary.GetValueOrNull("DW").AsNumber(document) ?? 1000;
        PdfArray? defaultVertical = dictionary.GetValueOrNull("DW2").AsArray(document);
        if (defaultVertical is { Count: >= 2 })
        {
            DefaultOriginY = defaultVertical[0].AsNumber(document) ?? 880;
            DefaultVerticalAdvance = defaultVertical[1].AsNumber(document) ?? -1000;
        }

        ReadHorizontal(dictionary.GetValueOrNull("W").AsArray(document), document);
        ReadVertical(dictionary.GetValueOrNull("W2").AsArray(document), document);
    }

    public double DefaultWidth { get; }
    public double DefaultVerticalAdvance { get; } = -1000;
    public double DefaultOriginY { get; } = 880;

    public double GetWidth(uint cid)
    {
        foreach (HorizontalRange range in _horizontal)
        {
            if (cid >= range.First && cid <= range.Last)
                return range.Widths is null
                    ? range.Width
                    : range.Widths[checked((int)(cid - range.First))];
        }

        return DefaultWidth;
    }

    public (double Advance, double OriginX, double OriginY) GetVertical(uint cid)
    {
        foreach (VerticalRange range in _vertical)
        {
            if (cid < range.First || cid > range.Last)
                continue;
            if (range.Metrics is null)
                return (range.Advance, range.OriginX, range.OriginY);
            int index = checked((int)(cid - range.First));
            return range.Metrics[index];
        }

        return (DefaultVerticalAdvance, GetWidth(cid) / 2, DefaultOriginY);
    }

    private void ReadHorizontal(PdfArray? values, PdfDocumentCore document)
    {
        if (values is null)
            return;
        int index = 0;
        while (index + 1 < values.Count)
        {
            int? firstValue = values[index++].AsInteger(document);
            if (!firstValue.HasValue || firstValue.Value < 0)
            {
                index++;
                continue;
            }

            uint first = (uint)firstValue.Value;
            PdfObject next = values[index++].Resolve(document);
            if (next is PdfArray widths)
            {
                double[] parsed = widths
                    .Select(item => item.AsNumber(document) ?? DefaultWidth)
                    .ToArray();
                if (parsed.Length > 0)
                {
                    _horizontal.Add(new HorizontalRange(
                        first,
                        checked(first + (uint)parsed.Length - 1),
                        0,
                        parsed));
                }
            }
            else if (next is PdfNumber { IsInteger: true } lastNumber &&
                     lastNumber.Value >= first &&
                     index < values.Count &&
                     values[index++].AsNumber(document) is { } width)
            {
                _horizontal.Add(new HorizontalRange(
                    first,
                    checked((uint)lastNumber.Value),
                    width,
                    null));
            }
        }
    }

    private void ReadVertical(PdfArray? values, PdfDocumentCore document)
    {
        if (values is null)
            return;
        int index = 0;
        while (index + 1 < values.Count)
        {
            int? firstValue = values[index++].AsInteger(document);
            if (!firstValue.HasValue || firstValue.Value < 0)
            {
                index++;
                continue;
            }

            uint first = (uint)firstValue.Value;
            PdfObject next = values[index++].Resolve(document);
            if (next is PdfArray metrics)
            {
                int count = metrics.Count / 3;
                var parsed = new (double Advance, double OriginX, double OriginY)[count];
                for (int item = 0; item < count; item++)
                {
                    parsed[item] = (
                        metrics[item * 3].AsNumber(document) ?? DefaultVerticalAdvance,
                        metrics[item * 3 + 1].AsNumber(document) ?? GetWidth(first + (uint)item) / 2,
                        metrics[item * 3 + 2].AsNumber(document) ?? DefaultOriginY);
                }

                if (parsed.Length > 0)
                {
                    _vertical.Add(new VerticalRange(
                        first,
                        checked(first + (uint)parsed.Length - 1),
                        0,
                        0,
                        0,
                        parsed));
                }
            }
            else if (next is PdfNumber { IsInteger: true } lastNumber &&
                     lastNumber.Value >= first &&
                     index + 2 < values.Count)
            {
                double? advance = values[index++].AsNumber(document);
                double? originX = values[index++].AsNumber(document);
                double? originY = values[index++].AsNumber(document);
                if (advance.HasValue && originX.HasValue && originY.HasValue)
                {
                    _vertical.Add(new VerticalRange(
                        first,
                        checked((uint)lastNumber.Value),
                        advance.Value,
                        originX.Value,
                        originY.Value,
                        null));
                }
            }
        }
    }

    private sealed record HorizontalRange(
        uint First,
        uint Last,
        double Width,
        double[]? Widths);

    private sealed record VerticalRange(
        uint First,
        uint Last,
        double Advance,
        double OriginX,
        double OriginY,
        (double Advance, double OriginX, double OriginY)[]? Metrics);
}
