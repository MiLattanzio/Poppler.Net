using System.Collections.ObjectModel;
using System.Text;
using Poppler.Core;
using Poppler.DocumentModel;
using Poppler.Text;

namespace Poppler.Forms;

internal sealed record PdfFormWidgetData(
    PdfFormField Field,
    PdfFormWidget Widget,
    PdfDictionary Dictionary,
    PdfStream? NormalAppearance,
    PdfColor TextColor,
    double FontSize);

internal sealed class PdfFormModel
{
    private readonly IReadOnlyDictionary<PdfReference, PdfFormWidgetData> _widgetsByReference;
    private readonly IReadOnlyDictionary<PdfDictionary, PdfFormWidgetData> _widgetsByDictionary;

    public PdfFormModel(
        IEnumerable<PdfFormField> fields,
        IEnumerable<IEnumerable<PdfFormWidget>> widgetsByPage,
        IDictionary<PdfReference, PdfFormWidgetData> widgetsByReference,
        IDictionary<PdfDictionary, PdfFormWidgetData> widgetsByDictionary,
        bool needAppearances)
    {
        Fields = new ReadOnlyCollection<PdfFormField>(fields.ToArray());
        WidgetsByPage = new ReadOnlyCollection<IReadOnlyList<PdfFormWidget>>(
            widgetsByPage
                .Select(widgets =>
                    (IReadOnlyList<PdfFormWidget>)
                    new ReadOnlyCollection<PdfFormWidget>(widgets.ToArray()))
                .ToArray());
        _widgetsByReference =
            new ReadOnlyDictionary<PdfReference, PdfFormWidgetData>(
                new Dictionary<PdfReference, PdfFormWidgetData>(
                    widgetsByReference));
        _widgetsByDictionary =
            new ReadOnlyDictionary<PdfDictionary, PdfFormWidgetData>(
                new Dictionary<PdfDictionary, PdfFormWidgetData>(
                    widgetsByDictionary,
                    ReferenceEqualityComparer.Instance));
        NeedAppearances = needAppearances;
    }

    public IReadOnlyList<PdfFormField> Fields { get; }
    public IReadOnlyList<IReadOnlyList<PdfFormWidget>> WidgetsByPage { get; }
    public bool NeedAppearances { get; }

    public PdfFormWidgetData? FindWidget(
        PdfObject source,
        PdfDictionary dictionary)
    {
        if (source is PdfReference reference &&
            _widgetsByReference.TryGetValue(reference, out PdfFormWidgetData? byReference))
        {
            return byReference;
        }
        return _widgetsByDictionary.TryGetValue(
            dictionary,
            out PdfFormWidgetData? byDictionary)
                ? byDictionary
                : null;
    }

    public static PdfFormModel Empty(int pageCount) => new(
        Array.Empty<PdfFormField>(),
        Enumerable.Range(0, pageCount)
            .Select(_ => (IEnumerable<PdfFormWidget>)Array.Empty<PdfFormWidget>()),
        new Dictionary<PdfReference, PdfFormWidgetData>(),
        new Dictionary<PdfDictionary, PdfFormWidgetData>(
            ReferenceEqualityComparer.Instance),
        needAppearances: false);
}

internal static class PdfFormReader
{
    private static readonly string[] InheritableKeys =
    {
        "FT", "Ff", "V", "DV", "DA", "Q", "MaxLen", "Opt", "I", "TI"
    };

    public static PdfFormModel Read(
        PdfDocumentCore document,
        PdfDictionary catalog,
        IReadOnlyList<PdfPageNode> pages)
    {
        PdfDictionary? acroForm =
            catalog.GetValueOrNull("AcroForm").AsDictionary(document);
        if (acroForm is null)
            return PdfFormModel.Empty(pages.Count);

        PdfArray roots =
            acroForm.GetValueOrNull("Fields").AsArray(document) ??
            new PdfArray(Array.Empty<PdfObject>());
        if (roots.Count > document.Options.MaximumFormFields)
            throw new PdfLimitException("AcroForm field count exceeds the configured limit.");

        var reader = new Reader(document, acroForm, pages);
        return reader.Read(roots);
    }

    private sealed class Reader
    {
        private readonly PdfDocumentCore _document;
        private readonly IReadOnlyList<PdfPageNode> _pages;
        private readonly string _formDefaultAppearance;
        private readonly bool _needAppearances;
        private readonly List<PdfFormField> _fields = new();
        private readonly List<PdfFormWidget>[] _widgetsByPage;
        private readonly Dictionary<PdfReference, int> _annotationPages = new();
        private readonly Dictionary<PdfDictionary, int> _annotationDictionaryPages =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<PdfReference, int> _pageReferences = new();
        private readonly Dictionary<PdfReference, PdfFormWidgetData> _widgetsByReference =
            new();
        private readonly Dictionary<PdfDictionary, PdfFormWidgetData> _widgetsByDictionary =
            new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<PdfReference> _visitedReferences = new();
        private readonly HashSet<PdfDictionary> _visitedDictionaries =
            new(ReferenceEqualityComparer.Instance);
        private int _fieldCount;
        private int _widgetCount;
        private int _optionCount;

        public Reader(
            PdfDocumentCore document,
            PdfDictionary acroForm,
            IReadOnlyList<PdfPageNode> pages)
        {
            _document = document;
            _pages = pages;
            _formDefaultAppearance = ReadText(acroForm, "DA");
            _needAppearances =
                acroForm.GetValueOrNull("NeedAppearances")?.Resolve(document)
                    is PdfBoolean { Value: true };
            _widgetsByPage = Enumerable.Range(0, pages.Count)
                .Select(_ => new List<PdfFormWidget>())
                .ToArray();
            IndexPages();
        }

        public PdfFormModel Read(PdfArray roots)
        {
            foreach (PdfObject root in roots)
            {
                ParseField(
                    root,
                    new Dictionary<string, PdfObject>(StringComparer.Ordinal),
                    Array.Empty<string>(),
                    depth: 0);
            }

            return new PdfFormModel(
                _fields,
                _widgetsByPage,
                _widgetsByReference,
                _widgetsByDictionary,
                _needAppearances);
        }

        private void IndexPages()
        {
            for (int pageIndex = 0; pageIndex < _pages.Count; pageIndex++)
            {
                PdfPageNode page = _pages[pageIndex];
                if (page.SourceReference is { } pageReference)
                    _pageReferences[pageReference] = pageIndex;
                PdfArray? annotations =
                    page.Dictionary.GetValueOrNull("Annots").AsArray(_document);
                if (annotations is null)
                    continue;
                if (annotations.Count > _document.Options.MaximumAnnotationsPerPage)
                {
                    throw new PdfLimitException(
                        "Page annotation count exceeds the configured limit.");
                }

                foreach (PdfObject item in annotations)
                {
                    PdfDictionary? dictionary = item.AsDictionary(_document);
                    if (dictionary is null ||
                        dictionary.GetValueOrNull("Subtype").AsName(_document) != "Widget")
                    {
                        continue;
                    }
                    if (item is PdfReference reference)
                        _annotationPages.TryAdd(reference, pageIndex);
                    _annotationDictionaryPages.TryAdd(dictionary, pageIndex);
                }
            }
        }

        private void ParseField(
            PdfObject source,
            IReadOnlyDictionary<string, PdfObject> inherited,
            IReadOnlyList<string> parentNames,
            int depth)
        {
            if (depth > _document.Options.MaximumFormFieldDepth)
            {
                throw new PdfLimitException(
                    "AcroForm field hierarchy exceeds the configured limit.");
            }

            PdfDictionary? dictionary = source.AsDictionary(_document);
            if (dictionary is null)
            {
                Report(
                    "form.field.invalid",
                    "An AcroForm field that is not a dictionary was skipped.");
                return;
            }
            if (!Enter(source, dictionary))
            {
                Report(
                    "form.field.circular",
                    "A circular or repeated AcroForm field reference was skipped.");
                return;
            }
            _fieldCount++;
            if (_fieldCount > _document.Options.MaximumFormFields)
                throw new PdfLimitException("AcroForm field count exceeds the configured limit.");

            var effective = inherited.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            foreach (string key in InheritableKeys)
            {
                if (dictionary.TryGetValue(key, out PdfObject? value))
                    effective[key] = value;
            }

            string partialName = ReadText(dictionary, "T");
            string[] names = string.IsNullOrEmpty(partialName)
                ? parentNames.ToArray()
                : parentNames.Append(partialName).ToArray();
            string fullName = string.Join(".", names);
            PdfFormFieldType type = FieldType(Name(effective, "FT"));
            PdfFormFieldFlags flags = (PdfFormFieldFlags)Math.Max(
                0,
                Integer(effective, "Ff") ?? 0);
            PdfButtonType buttonType = ButtonType(type, flags);
            IReadOnlyList<string> values = ReadValues(Value(effective, "V"));
            IReadOnlyList<string> defaultValues = ReadValues(Value(effective, "DV"));
            bool hasValue = HasValue(Value(effective, "V"));
            string defaultAppearance =
                Text(effective, "DA") ?? _formDefaultAppearance;
            PdfTextAlignment alignment = (Integer(effective, "Q") ?? 0) switch
            {
                1 => PdfTextAlignment.Center,
                2 => PdfTextAlignment.Right,
                _ => PdfTextAlignment.Left
            };
            int maximumLength = Math.Max(0, Integer(effective, "MaxLen") ?? 0);
            int topIndex = Math.Max(0, Integer(effective, "TI") ?? 0);
            IReadOnlyList<PdfFormFieldOption> options =
                ReadOptions(effective, values);

            var pureWidgets = new List<PdfObject>();
            var childFields = new List<PdfObject>();
            PdfArray? kids = dictionary.GetValueOrNull("Kids").AsArray(_document);
            if (kids is not null)
            {
                foreach (PdfObject kid in kids)
                {
                    PdfDictionary? child = kid.AsDictionary(_document);
                    if (child is null)
                    {
                        Report(
                            "form.field.child.invalid",
                            "An invalid AcroForm child was skipped.");
                        continue;
                    }
                    bool widget =
                        child.GetValueOrNull("Subtype").AsName(_document) == "Widget";
                    bool hasFieldIdentity =
                        child.ContainsKey("T") || child.ContainsKey("FT");
                    if (widget && !hasFieldIdentity)
                        pureWidgets.Add(kid);
                    else
                        childFields.Add(kid);
                }
            }

            bool composedWidget =
                dictionary.GetValueOrNull("Subtype").AsName(_document) == "Widget";
            var widgetSources = new List<PdfObject>();
            if (composedWidget)
                widgetSources.Add(source);
            widgetSources.AddRange(pureWidgets);
            if (widgetSources.Count > 0 || childFields.Count == 0)
            {
                AddTerminalField(
                    dictionary,
                    type,
                    buttonType,
                    partialName,
                    fullName,
                    flags,
                    values,
                    defaultValues,
                    hasValue,
                    defaultAppearance,
                    alignment,
                    maximumLength,
                    topIndex,
                    options,
                    widgetSources);
            }

            if (widgetSources.Count > 0 && childFields.Count > 0)
            {
                Report(
                    "form.field.kids.mixed",
                    "A field with both widget and field children was read conservatively.");
            }
            foreach (PdfObject child in childFields)
                ParseField(child, effective, names, depth + 1);
        }

        private void AddTerminalField(
            PdfDictionary fieldDictionary,
            PdfFormFieldType type,
            PdfButtonType buttonType,
            string partialName,
            string fullName,
            PdfFormFieldFlags flags,
            IReadOnlyList<string> values,
            IReadOnlyList<string> defaultValues,
            bool hasValue,
            string defaultAppearance,
            PdfTextAlignment alignment,
            int maximumLength,
            int topIndex,
            IReadOnlyList<PdfFormFieldOption> options,
            IReadOnlyList<PdfObject> widgetSources)
        {
            (PdfColor textColor, double fontSize) =
                ReadDefaultAppearance(defaultAppearance);
            var temporaryWidgets = new List<TemporaryWidget>(widgetSources.Count);
            foreach (PdfObject widgetSource in widgetSources)
            {
                PdfDictionary? widgetDictionary =
                    widgetSource.AsDictionary(_document);
                if (widgetDictionary is null)
                    continue;
                _widgetCount++;
                if (_widgetCount > _document.Options.MaximumFormWidgets)
                {
                    throw new PdfLimitException(
                        "AcroForm widget count exceeds the configured limit.");
                }

                int? pageIndex = FindPage(widgetSource, widgetDictionary);
                PdfRectangle rectangle = NormalizeRectangle(
                    widgetDictionary.GetValueOrNull("Rect").AsRectangle(_document));
                string localState =
                    widgetDictionary.GetValueOrNull("AS").AsName(_document) ?? "";
                (PdfStream? appearance, string selectedState, string onState) =
                    SelectNormalAppearance(
                        widgetDictionary,
                        localState,
                        values.FirstOrDefault() ?? "",
                        buttonType);
                PdfDictionary? characteristics =
                    widgetDictionary.GetValueOrNull("MK").AsDictionary(_document);
                string caption = characteristics is null
                    ? ""
                    : ReadText(characteristics, "CA");
                int rotation = characteristics?
                    .GetValueOrNull("R")
                    .AsInteger(_document) ?? 0;
                rotation = ((rotation % 360) + 360) % 360;
                PdfColor? borderColor = characteristics is null
                    ? null
                    : ReadColor(characteristics.GetValueOrNull("BC"));
                PdfColor? backgroundColor = characteristics is null
                    ? null
                    : ReadColor(characteristics.GetValueOrNull("BG"));
                var widget = new PdfFormWidget(
                    fullName,
                    type,
                    pageIndex,
                    rectangle,
                    selectedState,
                    onState,
                    caption,
                    rotation,
                    borderColor,
                    backgroundColor,
                    appearance is not null);
                temporaryWidgets.Add(new TemporaryWidget(
                    widgetSource,
                    widgetDictionary,
                    widget,
                    appearance));
            }

            var field = new PdfFormField(
                type,
                buttonType,
                partialName,
                fullName,
                ReadText(fieldDictionary, "TU"),
                ReadText(fieldDictionary, "TM"),
                flags,
                values,
                defaultValues,
                hasValue,
                defaultAppearance,
                alignment,
                maximumLength,
                topIndex,
                options,
                temporaryWidgets.Select(item => item.Widget));
            _fields.Add(field);

            foreach (TemporaryWidget item in temporaryWidgets)
            {
                var data = new PdfFormWidgetData(
                    field,
                    item.Widget,
                    item.Dictionary,
                    item.Appearance,
                    textColor,
                    fontSize);
                if (item.Source is PdfReference reference)
                    _widgetsByReference.TryAdd(reference, data);
                _widgetsByDictionary.TryAdd(item.Dictionary, data);
                if (item.Widget.PageIndex is { } pageIndex)
                    _widgetsByPage[pageIndex].Add(item.Widget);
            }
        }

        private IReadOnlyList<PdfFormFieldOption> ReadOptions(
            IReadOnlyDictionary<string, PdfObject> effective,
            IReadOnlyList<string> values)
        {
            PdfArray? source = Value(effective, "Opt").AsArray(_document);
            if (source is null)
                return Array.Empty<PdfFormFieldOption>();
            _optionCount += source.Count;
            if (_optionCount > _document.Options.MaximumFormOptions)
                throw new PdfLimitException("AcroForm option count exceeds the configured limit.");

            HashSet<int>? selectedIndices = ReadSelectedIndices(Value(effective, "I"));
            var parsed = new List<(string Export, string Display)>(source.Count);
            for (int index = 0; index < source.Count; index++)
            {
                PdfObject resolved = source[index].Resolve(_document);
                if (resolved is PdfString text)
                {
                    parsed.Add((text.Text, text.Text));
                    continue;
                }
                if (resolved is PdfArray pair &&
                    pair.Count >= 2 &&
                    ReadScalar(pair[0]) is { } export &&
                    ReadScalar(pair[1]) is { } display)
                {
                    parsed.Add((export, display));
                    continue;
                }
                parsed.Add(("", ""));
                Report(
                    "form.choice.option.invalid",
                    "An invalid AcroForm choice option was retained as an empty value.");
            }

            return parsed
                .Select((option, index) => new PdfFormFieldOption(
                    option.Export,
                    option.Display,
                    selectedIndices is not null
                        ? selectedIndices.Contains(index)
                        : values.Contains(option.Export, StringComparer.Ordinal) ||
                          values.Contains(option.Display, StringComparer.Ordinal)))
                .ToArray();
        }

        private HashSet<int>? ReadSelectedIndices(PdfObject? source)
        {
            PdfArray? indices = source.AsArray(_document);
            if (indices is null)
                return null;
            var result = new HashSet<int>();
            foreach (PdfObject item in indices)
            {
                if (item.AsInteger(_document) is { } index && index >= 0)
                    result.Add(index);
            }
            return result;
        }

        private int? FindPage(PdfObject source, PdfDictionary dictionary)
        {
            if (source is PdfReference reference &&
                _annotationPages.TryGetValue(reference, out int byReference))
            {
                return byReference;
            }
            if (_annotationDictionaryPages.TryGetValue(
                    dictionary,
                    out int byDictionary))
            {
                return byDictionary;
            }
            if (dictionary.GetValueOrNull("P") is PdfReference pageReference &&
                _pageReferences.TryGetValue(pageReference, out int byPageReference))
            {
                return byPageReference;
            }
            return null;
        }

        private (
            PdfStream? Stream,
            string SelectedState,
            string OnState) SelectNormalAppearance(
            PdfDictionary widget,
            string localState,
            string fieldValue,
            PdfButtonType buttonType)
        {
            PdfDictionary? appearances =
                widget.GetValueOrNull("AP").AsDictionary(_document);
            PdfObject? normal = appearances?.GetValueOrNull("N");
            if (normal.AsStream(_document) is { } stream)
                return (stream, localState, "");
            PdfDictionary? states = normal.AsDictionary(_document);
            if (states is null || states.Count == 0)
            {
                string inferredOnState =
                    buttonType is PdfButtonType.CheckBox or PdfButtonType.RadioButton &&
                    !string.IsNullOrEmpty(fieldValue) &&
                    !string.Equals(fieldValue, "Off", StringComparison.Ordinal)
                        ? fieldValue
                        : "";
                return (null, localState, inferredOnState);
            }

            string onState = states.Keys
                .Where(key => !string.Equals(key, "Off", StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal)
                .FirstOrDefault() ?? "";
            string[] candidates =
                buttonType is PdfButtonType.CheckBox or PdfButtonType.RadioButton
                    ? new[] { fieldValue, localState }
                    : new[] { localState, fieldValue };
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate) &&
                    states.TryGetValue(candidate, out PdfObject? selected) &&
                    selected.AsStream(_document) is { } selectedStream)
                {
                    return (selectedStream, candidate, onState);
                }
            }
            if (buttonType is PdfButtonType.CheckBox or PdfButtonType.RadioButton &&
                states.TryGetValue("Off", out PdfObject? off) &&
                off.AsStream(_document) is { } offStream)
            {
                return (offStream, "Off", onState);
            }

            foreach ((string key, PdfObject value) in states
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (value.AsStream(_document) is { } fallback)
                    return (fallback, key, onState);
            }
            return (null, localState, onState);
        }

        private (PdfColor Color, double FontSize) ReadDefaultAppearance(
            string appearance)
        {
            if (string.IsNullOrWhiteSpace(appearance))
                return (PdfColor.Black, 0);
            if (Encoding.Latin1.GetByteCount(appearance) >
                _document.Options.MaximumFormDefaultAppearanceBytes)
            {
                throw new PdfLimitException(
                    "AcroForm default appearance exceeds the configured limit.");
            }

            PdfColor color = PdfColor.Black;
            double fontSize = 0;
            try
            {
                foreach (PdfContentOperation operation in PdfContentReader.Read(
                             Encoding.Latin1.GetBytes(appearance),
                             _document.Options))
                {
                    IReadOnlyList<PdfObject> values = operation.Operands;
                    switch (operation.Operator)
                    {
                        case "Tf" when values.Count >= 2 &&
                                           values[^1] is PdfNumber size:
                            fontSize = double.IsFinite(size.Value)
                                ? Math.Max(0, size.Value)
                                : 0;
                            break;
                        case "g" when Numbers(values, 1) is { } gray:
                            color = PdfColor.Gray(gray[0]);
                            break;
                        case "rg" when Numbers(values, 3) is { } rgb:
                            color = PdfColor.Rgb(rgb[0], rgb[1], rgb[2]);
                            break;
                        case "k" when Numbers(values, 4) is { } cmyk:
                            color = PdfColor.Cmyk(
                                cmyk[0],
                                cmyk[1],
                                cmyk[2],
                                cmyk[3]);
                            break;
                    }
                }
            }
            catch (PdfLimitException)
            {
                throw;
            }
            catch (PdfException)
            {
                Report(
                    "form.default-appearance.invalid",
                    "An invalid AcroForm default appearance used managed defaults.");
            }

            return (color, fontSize);
        }

        private bool Enter(PdfObject source, PdfDictionary dictionary)
        {
            if (source is PdfReference reference)
                return _visitedReferences.Add(reference);
            return _visitedDictionaries.Add(dictionary);
        }

        private void Report(string code, string message) =>
            _document.AddDiagnostic(PdfDiagnosticSeverity.Warning, code, message);

        private static PdfFormFieldType FieldType(string? name) => name switch
        {
            "Btn" => PdfFormFieldType.Button,
            "Tx" => PdfFormFieldType.Text,
            "Ch" => PdfFormFieldType.Choice,
            "Sig" => PdfFormFieldType.Signature,
            _ => PdfFormFieldType.Unknown
        };

        private static PdfButtonType ButtonType(
            PdfFormFieldType type,
            PdfFormFieldFlags flags)
        {
            if (type != PdfFormFieldType.Button)
                return PdfButtonType.None;
            if ((flags & PdfFormFieldFlags.PushButton) != 0)
                return PdfButtonType.PushButton;
            if ((flags & PdfFormFieldFlags.Radio) != 0)
                return PdfButtonType.RadioButton;
            return PdfButtonType.CheckBox;
        }

        private string? Name(
            IReadOnlyDictionary<string, PdfObject> values,
            string key) =>
            Value(values, key).AsName(_document);

        private int? Integer(
            IReadOnlyDictionary<string, PdfObject> values,
            string key) =>
            Value(values, key).AsInteger(_document);

        private string? Text(
            IReadOnlyDictionary<string, PdfObject> values,
            string key) =>
            ReadScalar(Value(values, key));

        private static PdfObject? Value(
            IReadOnlyDictionary<string, PdfObject> values,
            string key) =>
            values.TryGetValue(key, out PdfObject? value) ? value : null;

        private string ReadText(PdfDictionary dictionary, string key) =>
            ReadScalar(dictionary.GetValueOrNull(key)) ?? "";

        private string? ReadScalar(PdfObject? source) =>
            source?.Resolve(_document) switch
            {
                PdfString text => text.Text,
                PdfName name => name.Value,
                _ => null
            };

        private IReadOnlyList<string> ReadValues(PdfObject? source)
        {
            PdfObject? resolved = source?.Resolve(_document);
            if (resolved is PdfArray array)
            {
                return array
                    .Select(ReadScalar)
                    .Where(value => value is not null)
                    .Select(value => value!)
                    .ToArray();
            }
            return ReadScalar(resolved) is { } value
                ? new[] { value }
                : Array.Empty<string>();
        }

        private bool HasValue(PdfObject? source) =>
            source is not null && source.Resolve(_document) is not PdfNull;

        private PdfColor? ReadColor(PdfObject? source)
        {
            PdfArray? array = source.AsArray(_document);
            if (array is null)
                return null;
            double[] values = array
                .Select(value => value.AsNumber(_document))
                .Where(value => value is { } number && double.IsFinite(number))
                .Select(value => value!.Value)
                .ToArray();
            return values.Length switch
            {
                1 => PdfColor.Gray(values[0]),
                3 => PdfColor.Rgb(values[0], values[1], values[2]),
                4 => PdfColor.Cmyk(values[0], values[1], values[2], values[3]),
                _ => null
            };
        }

        private static double[]? Numbers(
            IReadOnlyList<PdfObject> values,
            int count)
        {
            if (values.Count < count)
                return null;
            PdfObject[] tail = values.Skip(values.Count - count).ToArray();
            if (tail.Any(value =>
                    value is not PdfNumber number ||
                    !double.IsFinite(number.Value)))
            {
                return null;
            }
            return tail.Cast<PdfNumber>().Select(number => number.Value).ToArray();
        }

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

        private sealed record TemporaryWidget(
            PdfObject Source,
            PdfDictionary Dictionary,
            PdfFormWidget Widget,
            PdfStream? Appearance);
    }
}
