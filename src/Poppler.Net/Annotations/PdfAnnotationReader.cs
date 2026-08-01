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
        var actionReader = new PdfActionReader(document, destinations);
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
            bool isOpen =
                ReadBoolean(dictionary.GetValueOrNull("Open"), document) ?? false;
            PdfObject? optionalContentMembership =
                dictionary.GetValueOrNull("OC");
            bool defaultVisible =
                (flags &
                 (PdfAnnotationFlags.Invisible |
                  PdfAnnotationFlags.Hidden |
                  PdfAnnotationFlags.NoView)) == 0 &&
                (type != PdfAnnotationType.Popup || isOpen) &&
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
            IReadOnlyList<PdfPoint> calloutLine =
                ReadPoints(dictionary.GetValueOrNull("CL"), document);
            IReadOnlyList<IReadOnlyList<PdfPoint>> inkPaths =
                ReadInkPaths(dictionary.GetValueOrNull("InkList"), document);
            int totalPoints =
                quadPoints.Count +
                vertices.Count +
                linePoints.Count +
                calloutLine.Count +
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
            PdfAnnotationAction action = actionReader.ReadAnnotationAction(dictionary);
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
                AnnotationId(sourceObject, dictionary, document),
                ReferenceId(
                    dictionary.GetValueOrNull("IRT") ??
                    dictionary.GetValueOrNull("Parent"),
                    document),
                ReferenceId(dictionary.GetValueOrNull("Popup"), document),
                dictionary.GetValueOrNull("RT").AsName(document) ?? "",
                ReadText(dictionary, "State", document),
                ReadText(dictionary, "StateModel", document),
                dictionary.GetValueOrNull("IT").AsName(document) ?? "",
                isOpen,
                ReadText(dictionary, "RC", document),
                ReadText(dictionary, "DS", document),
                flags,
                color,
                interiorColor,
                opacity,
                ReadBorder(dictionary, document),
                quadPoints,
                vertices,
                linePoints,
                calloutLine,
                inkPaths,
                ReadLineEndingStyles(dictionary, document),
                ReadNumbers(dictionary.GetValueOrNull("RD"), document),
                ReadAttachment(dictionary, document),
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

    private static string? DestinationName(PdfObject? value, PdfDocumentCore document) =>
        value?.Resolve(document) switch
        {
            PdfName name => name.Value,
            PdfString text => text.Text,
            _ => null
        };

    private static string AnnotationId(
        PdfObject source,
        PdfDictionary dictionary,
        PdfDocumentCore document) =>
        source is PdfReference reference
            ? $"{reference.ObjectNumber}:{reference.Generation}"
            : ReadText(dictionary, "NM", document);

    private static string ReferenceId(PdfObject? value, PdfDocumentCore document)
    {
        if (value is PdfReference reference)
            return $"{reference.ObjectNumber}:{reference.Generation}";
        PdfDictionary? dictionary = value.AsDictionary(document);
        return dictionary is null ? "" : ReadText(dictionary, "NM", document);
    }

    private static bool? ReadBoolean(PdfObject? value, PdfDocumentCore document) =>
        value?.Resolve(document) is PdfBoolean boolean ? boolean.Value : null;

    private static IReadOnlyList<string> ReadLineEndingStyles(
        PdfDictionary annotation,
        PdfDocumentCore document)
    {
        PdfObject? value = annotation.GetValueOrNull("LE");
        if (value.AsName(document) is { } single)
            return new[] { single };
        PdfArray? array = value.AsArray(document);
        return array is null
            ? Array.Empty<string>()
            : array
                .Select(item => item.AsName(document))
                .Where(name => name is not null)
                .Select(name => name!)
                .ToArray();
    }

    private static EmbeddedFile? ReadAttachment(
        PdfDictionary annotation,
        PdfDocumentCore document)
    {
        PdfDictionary? specification =
            annotation.GetValueOrNull("FS").AsDictionary(document);
        return specification is null
            ? null
            : EmbeddedFileReader.Create("", specification, document);
    }

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

    internal sealed class PdfActionReader
    {
        private readonly PdfDocumentCore _document;
        private readonly PdfDestinationResolver _destinations;
        private readonly string _diagnosticScope;
        private readonly HashSet<PdfReference> _active = new();
        private int _count;

        public PdfActionReader(
            PdfDocumentCore document,
            PdfDestinationResolver destinations,
            string diagnosticScope = "annotation")
        {
            _document = document;
            _destinations = destinations;
            _diagnosticScope = diagnosticScope;
        }

        public PdfAnnotationAction ReadAnnotationAction(PdfDictionary annotation)
        {
            if (annotation.GetValueOrNull("A") is { } action)
                return Read(action, 0);
            if (annotation.GetValueOrNull("Dest") is { } direct)
            {
                return new PdfAnnotationAction(
                    PdfAnnotationActionType.GoTo,
                    uri: null,
                    _destinations.Resolve(direct),
                    DestinationName(direct, _document));
            }

            return Empty();
        }

        private PdfAnnotationAction Read(PdfObject source, int depth)
        {
            if (depth >= _document.Options.MaximumActionDepth)
                throw new PdfLimitException("PDF action chain is too deep.");
            if (++_count > _document.Options.MaximumActions)
                throw new PdfLimitException("PDF action count exceeds the configured limit.");

            PdfReference? reference = source as PdfReference;
            if (reference is not null && !_active.Add(reference))
            {
                _document.AddDiagnostic(
                    PdfDiagnosticSeverity.Warning,
                    $"{_diagnosticScope}.action.circular",
                    "A circular PDF action chain was truncated.");
                return Empty();
            }

            try
            {
                PdfDictionary? action = source.AsDictionary(_document);
                if (action is null)
                    return Unsupported("");

                IReadOnlyList<PdfAnnotationAction> next =
                    ReadNext(action.GetValueOrNull("Next"), depth + 1);
                string kind = action.GetValueOrNull("S").AsName(_document) ?? "";
                string? uri = null;
                PdfDestination? destination = null;
                string? namedTarget = null;
                string? fileName = null;
                bool? newWindow = ReadBoolean(
                    action.GetValueOrNull("NewWindow"),
                    _document);
                string? script = null;
                int flags = action.GetValueOrNull("Flags").AsInteger(_document) ?? 0;
                bool? isHidden = null;
                IReadOnlyList<string> fields = Array.Empty<string>();
                IReadOnlyList<string> stateChanges = Array.Empty<string>();
                PdfAnnotationActionType type;

                switch (kind)
                {
                    case "URI":
                        type = PdfAnnotationActionType.Uri;
                        uri = ReadText(action, "URI", _document);
                        break;
                    case "GoTo":
                    {
                        type = PdfAnnotationActionType.GoTo;
                        PdfObject? target = action.GetValueOrNull("D");
                        destination = _destinations.Resolve(target);
                        namedTarget = DestinationName(target, _document);
                        break;
                    }
                    case "Named":
                        type = PdfAnnotationActionType.Named;
                        namedTarget = action.GetValueOrNull("N").AsName(_document);
                        break;
                    case "GoToR":
                        type = PdfAnnotationActionType.GoToRemote;
                        fileName = ReadFileSpecification(
                            action.GetValueOrNull("F"));
                        namedTarget = ObjectText(action.GetValueOrNull("D"));
                        break;
                    case "Launch":
                        type = PdfAnnotationActionType.Launch;
                        fileName = ReadFileSpecification(
                            action.GetValueOrNull("F"));
                        break;
                    case "JavaScript":
                        type = PdfAnnotationActionType.JavaScript;
                        script = ReadScript(action.GetValueOrNull("JS"));
                        break;
                    case "SubmitForm":
                        type = PdfAnnotationActionType.SubmitForm;
                        fileName = ReadFileSpecification(
                            action.GetValueOrNull("F"));
                        fields = ReadStrings(action.GetValueOrNull("Fields"));
                        break;
                    case "ResetForm":
                        type = PdfAnnotationActionType.ResetForm;
                        fields = ReadStrings(action.GetValueOrNull("Fields"));
                        break;
                    case "ImportData":
                        type = PdfAnnotationActionType.ImportData;
                        fileName = ReadFileSpecification(
                            action.GetValueOrNull("F"));
                        break;
                    case "Hide":
                        type = PdfAnnotationActionType.Hide;
                        fields = ReadStrings(action.GetValueOrNull("T"));
                        isHidden = ReadBoolean(
                            action.GetValueOrNull("H"),
                            _document) ?? true;
                        break;
                    case "SetOCGState":
                        type = PdfAnnotationActionType.SetOptionalContentState;
                        stateChanges = ReadStrings(action.GetValueOrNull("State"));
                        break;
                    case "Rendition":
                        type = PdfAnnotationActionType.Rendition;
                        namedTarget = ReadText(action, "N", _document);
                        flags = action.GetValueOrNull("OP").AsInteger(_document) ?? flags;
                        break;
                    case "Trans":
                        type = PdfAnnotationActionType.Transition;
                        namedTarget = "Trans";
                        break;
                    case "GoTo3DView":
                        type = PdfAnnotationActionType.GoToThreeDView;
                        namedTarget = ObjectText(action.GetValueOrNull("V"));
                        break;
                    default:
                        type = PdfAnnotationActionType.Unsupported;
                        namedTarget = kind;
                        break;
                }

                return new PdfAnnotationAction(
                    type,
                    uri,
                    destination,
                    namedTarget,
                    fileName,
                    newWindow,
                    script,
                    flags,
                    isHidden,
                    fields,
                    stateChanges,
                    next);
            }
            finally
            {
                if (reference is not null)
                    _active.Remove(reference);
            }
        }

        private IReadOnlyList<PdfAnnotationAction> ReadNext(
            PdfObject? value,
            int depth)
        {
            if (value is null)
                return Array.Empty<PdfAnnotationAction>();
            if (value.AsArray(_document) is { } array)
                return array.Select(item => Read(item, depth)).ToArray();
            return new[] { Read(value, depth) };
        }

        private IReadOnlyList<string> ReadStrings(PdfObject? value)
        {
            if (value is null)
                return Array.Empty<string>();
            if (value.AsArray(_document) is { } array)
                return array.Select(ObjectText).Where(text => text.Length > 0).ToArray();
            string single = ObjectText(value);
            return single.Length == 0 ? Array.Empty<string>() : new[] { single };
        }

        private string ReadFileSpecification(PdfObject? value)
        {
            if (value?.Resolve(_document) is PdfString text)
                return text.Text;
            PdfDictionary? specification = value.AsDictionary(_document);
            return specification is null
                ? ""
                : ReadText(specification, "UF", _document) is { Length: > 0 } unicode
                    ? unicode
                    : ReadText(specification, "F", _document);
        }

        private string ReadScript(PdfObject? value)
        {
            PdfObject? resolved = value?.Resolve(_document);
            byte[] bytes = resolved switch
            {
                PdfString text => text.Bytes.ToArray(),
                PdfStream stream => _document.Decode(stream),
                _ => Array.Empty<byte>()
            };
            if (bytes.Length > _document.Options.MaximumActionScriptBytes)
                throw new PdfLimitException("PDF action script exceeds the configured limit.");
            return PdfTextEncoding.DecodePdfString(bytes);
        }

        private string ObjectText(PdfObject? value)
        {
            if (value is PdfReference reference)
                return $"{reference.ObjectNumber}:{reference.Generation}";
            PdfObject? resolved = value?.Resolve(_document);
            return resolved switch
            {
                PdfString text => text.Text,
                PdfName name => name.Value,
                PdfNumber number => number.ToString(),
                PdfBoolean boolean => boolean.ToString(),
                PdfArray array => string.Join(
                    " ",
                    array.Select(ObjectText).Where(text => text.Length > 0)),
                PdfDictionary dictionary =>
                    ReadText(dictionary, "T", _document) is { Length: > 0 } target
                        ? target
                        : ReadText(dictionary, "NM", _document),
                _ => ""
            };
        }

        private static PdfAnnotationAction Empty() =>
            new(
                PdfAnnotationActionType.None,
                uri: null,
                destination: null,
                namedTarget: null);

        private static PdfAnnotationAction Unsupported(string kind) =>
            new(
                PdfAnnotationActionType.Unsupported,
                uri: null,
                destination: null,
                kind);
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
        "Caret" => PdfAnnotationType.Caret,
        "Popup" => PdfAnnotationType.Popup,
        "FileAttachment" => PdfAnnotationType.FileAttachment,
        "Sound" => PdfAnnotationType.Sound,
        "Movie" => PdfAnnotationType.Movie,
        "Screen" => PdfAnnotationType.Screen,
        "PrinterMark" => PdfAnnotationType.PrinterMark,
        "TrapNet" => PdfAnnotationType.TrapNet,
        "Watermark" => PdfAnnotationType.Watermark,
        "3D" => PdfAnnotationType.ThreeD,
        "Redact" => PdfAnnotationType.Redact,
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
