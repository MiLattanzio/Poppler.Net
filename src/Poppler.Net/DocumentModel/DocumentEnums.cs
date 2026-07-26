namespace Poppler;

public enum PageMode
{
    UseNone,
    UseOutlines,
    UseThumbs,
    FullScreen,
    UseOptionalContent,
    UseAttachments
}

public enum PageLayout
{
    NoLayout,
    SinglePage,
    OneColumn,
    TwoColumnLeft,
    TwoColumnRight,
    TwoPageLeft,
    TwoPageRight
}

public enum FormType
{
    None,
    AcroForm,
    Xfa
}

public enum PageBox
{
    MediaBox,
    CropBox,
    BleedBox,
    TrimBox,
    ArtBox
}

public enum PageOrientation
{
    Portrait,
    Landscape,
    UpsideDown,
    Seascape
}

public enum TextLayout
{
    Physical,
    RawOrder,
    NonRawNonPhysical
}

public enum CaseSensitivity
{
    CaseSensitive,
    CaseInsensitive
}

[Flags]
public enum Permission
{
    None = 0,
    Print = 1 << 0,
    Modify = 1 << 1,
    Copy = 1 << 2,
    AddNotes = 1 << 3,
    FillForms = 1 << 4,
    Accessibility = 1 << 5,
    Assemble = 1 << 6,
    HighResolutionPrint = 1 << 7,
    All = Print | Modify | Copy | AddNotes | FillForms |
          Accessibility | Assemble | HighResolutionPrint
}
