using System.Collections.ObjectModel;

namespace Poppler;

public enum PdfFormFieldType
{
    Unknown,
    Button,
    Text,
    Choice,
    Signature
}

public enum PdfButtonType
{
    None,
    CheckBox,
    RadioButton,
    PushButton
}

public enum PdfTextAlignment
{
    Left,
    Center,
    Right
}

[Flags]
public enum PdfFormFieldFlags
{
    None = 0,
    ReadOnly = 1 << 0,
    Required = 1 << 1,
    NoExport = 1 << 2,
    Multiline = 1 << 12,
    Password = 1 << 13,
    NoToggleToOff = 1 << 14,
    Radio = 1 << 15,
    PushButton = 1 << 16,
    Combo = 1 << 17,
    Edit = 1 << 18,
    Sort = 1 << 19,
    FileSelect = 1 << 20,
    MultiSelect = 1 << 21,
    DoNotSpellCheck = 1 << 22,
    DoNotScroll = 1 << 23,
    Comb = 1 << 24,
    RichText = 1 << 25,
    RadiosInUnison = 1 << 25,
    CommitOnSelectionChange = 1 << 26
}

public sealed class PdfFormFieldOption
{
    internal PdfFormFieldOption(
        string exportValue,
        string displayValue,
        bool isSelected)
    {
        ExportValue = exportValue;
        DisplayValue = displayValue;
        IsSelected = isSelected;
    }

    public string ExportValue { get; }
    public string DisplayValue { get; }
    public bool IsSelected { get; }
}

/// <summary>Immutable page placement and appearance metadata for a form widget.</summary>
public sealed class PdfFormWidget
{
    internal PdfFormWidget(
        string fieldName,
        PdfFormFieldType fieldType,
        int? pageIndex,
        PdfRectangle rectangle,
        string appearanceState,
        string onState,
        string caption,
        int rotation,
        PdfColor? borderColor,
        PdfColor? backgroundColor,
        bool hasAppearance)
    {
        FieldName = fieldName;
        FieldType = fieldType;
        PageIndex = pageIndex;
        Rectangle = rectangle;
        AppearanceState = appearanceState;
        OnState = onState;
        Caption = caption;
        Rotation = rotation;
        BorderColor = borderColor;
        BackgroundColor = backgroundColor;
        HasAppearance = hasAppearance;
    }

    public string FieldName { get; }
    public PdfFormFieldType FieldType { get; }
    public int? PageIndex { get; }
    public int? PageNumber => PageIndex is { } index ? index + 1 : null;
    public PdfRectangle Rectangle { get; }
    public string AppearanceState { get; }
    public string OnState { get; }
    public string Caption { get; }
    public int Rotation { get; }
    public PdfColor? BorderColor { get; }
    public PdfColor? BackgroundColor { get; }
    public bool HasAppearance { get; }
}

/// <summary>Immutable logical AcroForm field with its current read-only value.</summary>
public sealed class PdfFormField
{
    internal PdfFormField(
        PdfFormFieldType type,
        PdfButtonType buttonType,
        string partialName,
        string fullyQualifiedName,
        string alternateName,
        string mappingName,
        PdfFormFieldFlags flags,
        IEnumerable<string> values,
        IEnumerable<string> defaultValues,
        bool hasValue,
        string defaultAppearance,
        PdfTextAlignment alignment,
        int maximumLength,
        int topIndex,
        IEnumerable<PdfFormFieldOption> options,
        IEnumerable<PdfFormWidget> widgets)
    {
        Type = type;
        ButtonType = buttonType;
        PartialName = partialName;
        FullyQualifiedName = fullyQualifiedName;
        AlternateName = alternateName;
        MappingName = mappingName;
        Flags = flags;
        Values = new ReadOnlyCollection<string>(values.ToArray());
        DefaultValues = new ReadOnlyCollection<string>(defaultValues.ToArray());
        HasValue = hasValue;
        DefaultAppearance = defaultAppearance;
        Alignment = alignment;
        MaximumLength = maximumLength;
        TopIndex = topIndex;
        Options = new ReadOnlyCollection<PdfFormFieldOption>(options.ToArray());
        Widgets = new ReadOnlyCollection<PdfFormWidget>(widgets.ToArray());
    }

    public PdfFormFieldType Type { get; }
    public PdfButtonType ButtonType { get; }
    public string PartialName { get; }
    public string FullyQualifiedName { get; }
    public string AlternateName { get; }
    public string MappingName { get; }
    public PdfFormFieldFlags Flags { get; }
    public IReadOnlyList<string> Values { get; }
    public string Value => Values.Count > 0 ? Values[0] : "";
    public IReadOnlyList<string> DefaultValues { get; }
    public string DefaultValue => DefaultValues.Count > 0 ? DefaultValues[0] : "";
    public bool HasValue { get; }
    public bool IsSigned => Type == PdfFormFieldType.Signature && HasValue;
    public string DefaultAppearance { get; }
    public PdfTextAlignment Alignment { get; }
    public int MaximumLength { get; }
    public int TopIndex { get; }
    public IReadOnlyList<PdfFormFieldOption> Options { get; }
    public IReadOnlyList<PdfFormWidget> Widgets { get; }
}
