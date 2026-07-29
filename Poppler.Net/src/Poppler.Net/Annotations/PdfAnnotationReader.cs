using System.Collections.ObjectModel;
using Poppler.Core;
using Poppler.DocumentModel;
using Poppler.Forms;
using Poppler.OptionalContent;

namespace Poppler.Annotations;

internal sealed record PdfAnnotationData(
    int Index,
    PdfAnnotation Annotation,
    PdfStream? NormalAppearance,
    PdfFormWidgetData? FormWidget,
    PdfObject? OptionalContent);

internal static class PdfAnnotationReader
{
    public static IReadOnlyList<PdfAnnotationData> Read(
        PdfDocumentCore document,
        PdfPageNode page,
        PdfDestinationResolver destinations,
        PdfFormModel forms,
        PdfOptionalContentModel optionalContentModel)
    {
        PdfArray? source = page.Dictionary.GetValueOrNull("Annots").AsArray(document);
        if (source is null)
            return Array.Empty<PdfAnnotationData>();
        if (source.Count > document.Options.MaximumAnnotationsPerPage)
        {
            throw new PdfLimitException(
                "Page annotation count exceeds the configured limit.");
        }

        var result = new List<PdfAnnotationData>(source.Count);
        PdfOptionalContentEvaluator optionalContent =
            optionalContentModel.CreateEvaluator();
        PdfDictionary? resources = page.Resources.AsDictionary(document);
        for (int index = 0; index < source.Count; index++)
        {
            PdfObject sourceObject = source[index];
            PdfDictionary? dictionary = sourceObject.AsDictionary(document);
            if (dictionary is null)
            {
                document.AddDiagnostic(
                    PdfDiagnosticSeverity.Warning,
                    "annotation.invalid",
                    $"Annotation {index + 1} is not a dictionary and was skipped.");
                continue;
            }

            string subtype = dictionary.GetValueOrNull("Subtype").AsName(document) ?? "";
            PdfAnnotationType type = AnnotationType(subtype);
            PdfRectangle rectangle = NormalizeRectangle(
                dictionary.GetValueOrNull("Rect").AsRectangle(document));
            PdfAnnotationFlags flags = (PdfAnnotationFlags)
                Math.Max(0, dictionary.GetValueOrNull("F").AsInteger(document) ?? 0);
            PdfObject? optionalContentMembership =
                dictionary.GetValueOrNull("OC");
            bool defaultVisible =
                (flags &
                 (PdfAnnotationFlags.Invisible |
                  PdfAnnotationFlags.Hidden |
                  PdfAnnotationFlags.NoView)) == 0 &&
                optionalContent.IsVisible(
                    optionalContentMembership,
                    resources);
            PdfColor? color = ReadColor(dictionary.GetValueOrNull("C"), document);
            PdfColor? interiorColor =
                ReadColor(dictionary.GetValueOrNull("IC"), document);
            double rawOpacity =
                dictionary.GetValueOrNull("CA").AsNumber(document) ?? 1;
            double opacity = double.IsFinite(rawOpacity)
                ? Math.Clamp(rawOpacity, 0, 1)
                : 1;
            IReadOnlyList<PdfPoint> quadPoints =
                ReadPoints(dictionary.GetValueOrNull("QuadPoints"), document);
            IReadOnlyList<PdfPoint> vertices =
                ReadPoints(dictionary.GetValueOrNull("Vertices"), document);
            IReadOnlyList<PdfPoint> linePoints =
                ReadPoints(dictionary.GetValueOrNull("L"), document);
            IReadOnlyList<IReadOnlyList<PdfPoint>> inkPaths =
                ReadInkPaths(dictionary.GetValueOrNull("InkList"), document);
            int totalPoints =
                quadPoints.Count +
                vertices.Count +
                linePoints.Count +
                inkPaths.Sum(path => path.Count);
            if (totalPoints > document.Options.MaximumAnnotationPoints)
            {
                throw new PdfLimitException(
                    "Annotation point count exceeds the configured limit.");
            }

            PdfFormWidgetData? formWidget =
                forms.FindWidget(sourceObject, dictionary);
            PdfStream? appearance = formWidget is null
                ? ReadNormalAppearance(dictionary, document)
                : formWidget.NormalAppearance;
            PdfAnnotationAction action = ReadAction(dictionary, destinations, document);
            var annotation = new PdfAnnotation(
                type,
                subtype,
                rectangle,
                ReadText(dictionary, "Contents", document),
                ReadText(dictionary, "NM", document),
                ReadText(dictionary, "T", document),
                ReadText(dictionary, "Subj", document),
                dictionary.GetValueOrNull("Name").AsName(document) ?? "",
                PdfDateParser.Parse(ReadText(dictionary, "M", document)),
                flags,
                color,
                interiorColor,
                opacity,
                ReadBorder(dictionary, document),
                quadPoints,
                vertices,
                linePoints,
                inkPaths,
                action,
                appearance is not null,
                defaultVisible);
            result.Add(new PdfAnnotationData(
                index,
                annotation,
                appearance,
                formWidget,
                optionalContentMembership));
        }

        return new ReadOnlyCollection<PdfAnnotationData>(result);
    }

    private static PdfAnnotationAction ReadAction(
        PdfDictionary annotation,
        PdfDestinationResolver destinations,
        PdfDocumentCore document)
    {
        if (annotation.GetValueOrNull("A").AsDictionary(document) is { } action)
        {
            string kind = action.GetValueOrNull("S").AsName(document) ?? "";
            switch (kind)
            {
                case "URI":
                    return new PdfAnnotationAction(
                        PdfAnnotationActionType.Uri,
                        ReadText(action, "URI", document),
                        destination: null,
                        namedTarget: null);
                case "GoTo":
                {
                    PdfObject? target = action.GetValueOrNull("D");
                    string? name = DestinationName(target, document);
                    return new PdfAnnotationAction(
                        PdfAnnotationActionType.GoTo,
                        uri: null,
                        destinations.Resolve(target),
                        name);
                }
                case "Named":
                    return new PdfAnnotationAction(
                        PdfAnnotationActionType.Named,
                        uri: null,
                        destination: null,
                        action.GetValueOrNull("N").AsName(document));
                default:
                    return new PdfAnnotationAction(
                        PdfAnnotationActionType.Unsupported,
                        uri: null,
                        destination: null,
                        kind);
            }
        }

        if (annotation.GetValueOrNull("Dest") is { } direct)
        {
            return new PdfAnnotationAction(
                PdfAnnotationActionType.GoTo,
                uri: null,
                destinations.Resolve(direct),
                DestinationName(direct, document));
        }

        return new PdfAnnotationAction(
            PdfAnnotationActionType.None,
            uri: null,
            destination: null,
            namedTarget: null);
    }

    private static string? DestinationName(PdfObject? value, PdfDocumentCore document) =>
        value?.Resolve(document) switch
        {
            PdfName name => name.Value,
            PdfString text => text.Text,
            _ => null
        };

    private static PdfStream? ReadNormalAppearance(
        PdfDictionary annotation,
        PdfDocumentCore document)
    {
        PdfDictionary? appearances =
            annotation.GetValueOrNull("AP").AsDictionary(document);
        PdfObject? normal = appearances?.GetValueOrNull("N");
        if (normal.AsStream(document) is { } stream)
            return stream;
        PdfDictionary? states = normal.AsDictionary(document);
        if (states is null || states.Count == 0)
            return null;
        string? activeState = annotation.GetValueOrNull("AS").AsName(document);
        if (activeState is not null &&
            states.TryGetValue(activeState, out PdfObject? selected))
        {
            return selected.AsStream(document);
        }

        return states
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value.AsStream(document))
            .FirstOrDefault(streamValue => streamValue is not null);
    }

    private static PdfAnnotationBorderStyle ReadBorder(
        PdfDictionary annotation,
        PdfDocumentCore document)
    {
        double horizontalRadius = 0;
        double verticalRadius = 0;
        double width = 1;
        double[] dash = Array.Empty<double>();
        if (annotation.GetValueOrNull("Border").AsArray(document) is { } border)
        {
            horizontalRadius = Number(border, 0, document) ?? 0;
            verticalRadius = Number(border, 1, document) ?? 0;
            width = Number(border, 2, document) ?? 1;
            if (border.Count > 3)
                dash = ReadNumbers(border[3], document);
        }

        PdfAnnotationBorderStyleKind style = dash.Length > 0
            ? PdfAnnotationBorderStyleKind.Dashed
            : PdfAnnotationBorderStyleKind.Solid;
        if (annotation.GetValueOrNull("BS").AsDictionary(document) is { } borderStyle)
        {
            width = borderStyle.GetValueOrNull("W").AsNumber(document) ?? width;
            style = borderStyle.GetValueOrNull("S").AsName(document) switch
            {
                "D" => PdfAnnotationBorderStyleKind.Dashed,
                "B" => PdfAnnotationBorderStyleKind.Beveled,
                "I" => PdfAnnotationBorderStyleKind.Inset,
                "U" => PdfAnnotationBorderStyleKind.Underline,
                _ => PdfAnnotationBorderStyleKind.Solid
            };
            if (borderStyle.GetValueOrNull("D") is { } styleDash)
                dash = ReadNumbers(styleDash, document);
        }

        return new PdfAnnotationBorderStyle(
            NonNegativeFinite(width),
            NonNegativeFinite(horizontalRadius),
            NonNegativeFinite(verticalRadius),
            style,
            dash.Where(value => value >= 0 && double.IsFinite(value)));
    }

    private static IReadOnlyList<PdfPoint> ReadPoints(
        PdfObject? value,
        PdfDocumentCore document)
    {
        PdfArray? array = value.AsArray(document);
        if (array is null)
            return Array.Empty<PdfPoint>();
        var points = new List<PdfPoint>(array.Count / 2);
        for (int index = 0; index + 1 < array.Count; index += 2)
        {
            double? x = array[index].AsNumber(document);
            double? y = array[index + 1].AsNumber(document);
            if (x is { } pointX &&
                y is { } pointY &&
                double.IsFinite(pointX) &&
                double.IsFinite(pointY))
            {
                points.Add(new PdfPoint(pointX, pointY));
            }
        }

        return points;
    }

    private static IReadOnlyList<IReadOnlyList<PdfPoint>> ReadInkPaths(
        PdfObject? value,
        PdfDocumentCore document)
    {
        PdfArray? paths = value.AsArray(document);
        if (paths is null)
            return Array.Empty<IReadOnlyList<PdfPoint>>();
        return paths
            .Select(path => ReadPoints(path, document))
            .Where(path => path.Count > 0)
            .ToArray();
    }

    private static PdfColor? ReadColor(
        PdfObject? value,
        PdfDocumentCore document)
    {
        double[] components = ReadNumbers(value, document);
        return components.Length switch
        {
            1 => PdfColor.Gray(components[0]),
            3 => PdfColor.Rgb(components[0], components[1], components[2]),
            4 => PdfColor.Cmyk(
                components[0],
                components[1],
                components[2],
                components[3]),
            _ => null
        };
    }

    private static double[] ReadNumbers(
        PdfObject? value,
        PdfDocumentCore document)
    {
        PdfArray? array = value.AsArray(document);
        if (array is null)
            return Array.Empty<double>();
        return array
            .Select(item => item.AsNumber(document))
            .Where(number => number is { } valueNumber && double.IsFinite(valueNumber))
            .Select(number => number!.Value)
            .ToArray();
    }

    private static string ReadText(
        PdfDictionary dictionary,
        string key,
        PdfDocumentCore document) =>
        dictionary.GetValueOrNull(key)?.Resolve(document) switch
        {
            PdfString value => value.Text,
            PdfName name => name.Value,
            _ => ""
        };

    private static PdfRectangle NormalizeRectangle(PdfRectangle? value)
    {
        if (value is not { } rectangle ||
            !double.IsFinite(rectangle.Left) ||
            !double.IsFinite(rectangle.Bottom) ||
            !double.IsFinite(rectangle.Right) ||
            !double.IsFinite(rectangle.Top))
        {
            return default;
        }

        return new PdfRectangle(
            Math.Min(rectangle.Left, rectangle.Right),
            Math.Min(rectangle.Bottom, rectangle.Top),
            Math.Max(rectangle.Left, rectangle.Right),
            Math.Max(rectangle.Bottom, rectangle.Top));
    }

    private static PdfAnnotationType AnnotationType(string subtype) => subtype switch
    {
        "Link" => PdfAnnotationType.Link,
        "Text" => PdfAnnotationType.Text,
        "FreeText" => PdfAnnotationType.FreeText,
        "Highlight" => PdfAnnotationType.Highlight,
        "Underline" => PdfAnnotationType.Underline,
        "Squiggly" => PdfAnnotationType.Squiggly,
        "StrikeOut" => PdfAnnotationType.StrikeOut,
        "Square" => PdfAnnotationType.Square,
        "Circle" => PdfAnnotationType.Circle,
        "Line" => PdfAnnotationType.Line,
        "Polygon" => PdfAnnotationType.Polygon,
        "PolyLine" => PdfAnnotationType.PolyLine,
        "Ink" => PdfAnnotationType.Ink,
        "Stamp" => PdfAnnotationType.Stamp,
        "Widget" => PdfAnnotationType.Widget,
        _ => PdfAnnotationType.Unknown
    };

    private static double? Number(
        PdfArray array,
        int index,
        PdfDocumentCore document) =>
        index < array.Count ? array[index].AsNumber(document) : null;

    private static double NonNegativeFinite(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;
}
