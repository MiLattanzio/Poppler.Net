using Poppler.Core;
using Poppler.DocumentModel;
using Poppler.Graphics;
using Poppler.Rendering;
using Poppler.Text;

namespace Poppler;

public sealed class Page
{
    private readonly Document _owner;
    private readonly PdfDocumentCore _document;
    private readonly PdfPageNode _node;
    private readonly Lazy<IReadOnlyList<TextBox>> _physicalText;
    private readonly Lazy<IReadOnlyList<TextBox>> _rawText;
    private readonly Lazy<IReadOnlyList<TextBox>> _readingOrderText;
    private readonly Lazy<IReadOnlyDictionary<string, PdfFontDecoder>> _fontDecoders;
    private readonly Lazy<IReadOnlyList<FontInfo>> _fonts;
    private readonly Lazy<IReadOnlyList<PdfGraphicsElement>> _graphics;
    private readonly Lazy<IReadOnlyList<PdfImage>> _images;

    internal Page(
        Document owner,
        PdfDocumentCore document,
        PdfPageNode node,
        int index,
        string label)
    {
        _owner = owner;
        _document = document;
        _node = node;
        Index = index;
        Label = label;
        _physicalText = new Lazy<IReadOnlyList<TextBox>>(
            () => Extract(TextLayout.Physical));
        _rawText = new Lazy<IReadOnlyList<TextBox>>(
            () => Extract(TextLayout.RawOrder));
        _readingOrderText = new Lazy<IReadOnlyList<TextBox>>(
            () => Extract(TextLayout.NonRawNonPhysical));
        _fontDecoders = new Lazy<IReadOnlyDictionary<string, PdfFontDecoder>>(
            () => PdfFontCollection.Read(_document, _node));
        _fonts = new Lazy<IReadOnlyList<FontInfo>>(
            () => _fontDecoders.Value
                .Values
                .Select(font => font.Info)
                .OrderBy(font => font.ResourceName, StringComparer.Ordinal)
                .ToArray());
        _graphics = new Lazy<IReadOnlyList<PdfGraphicsElement>>(
            ExtractGraphics);
        _images = new Lazy<IReadOnlyList<PdfImage>>(
            () => Graphics
                .OfType<PdfImageElement>()
                .Select(element => element.Image)
                .OfType<PdfImage>()
                .ToArray());
    }

    public int Index { get; }
    public int Number => Index + 1;
    public string Label { get; }
    public int Rotation => _node.Rotation;
    public double Duration => _node.Dictionary.GetValueOrNull("Dur").AsNumber(_document) ?? -1;
    public IReadOnlyList<FontInfo> Fonts => _fonts.Value;
    public IReadOnlyList<PdfGraphicsElement> Graphics => _graphics.Value;
    public IReadOnlyList<PdfImage> Images => _images.Value;

    public PageOrientation Orientation => Rotation switch
    {
        90 => PageOrientation.Landscape,
        180 => PageOrientation.UpsideDown,
        270 => PageOrientation.Seascape,
        _ => PageOrientation.Portrait
    };

    public PdfRectangle PageRect(PageBox box = PageBox.CropBox) => box switch
    {
        PageBox.MediaBox => _node.MediaBox,
        PageBox.CropBox => _node.CropBox,
        PageBox.BleedBox => _node.BleedBox ?? _node.CropBox,
        PageBox.TrimBox => _node.TrimBox ?? _node.CropBox,
        PageBox.ArtBox => _node.ArtBox ?? _node.CropBox,
        _ => _node.CropBox
    };

    public string Text(
        PdfRectangle? rectangle = null,
        TextLayout layout = TextLayout.Physical)
    {
        IReadOnlyList<TextBox> boxes = TextList(layout);
        if (rectangle is not null)
        {
            PdfRectangle area = rectangle.Value;
            boxes = boxes.Where(box => Intersects(box.BoundingBox, area)).ToArray();
        }

        return PdfTextExtractor.Join(boxes, layout);
    }

    public IReadOnlyList<TextBox> TextList(TextLayout layout = TextLayout.Physical) =>
        layout switch
        {
            TextLayout.RawOrder => _rawText.Value,
            TextLayout.NonRawNonPhysical => _readingOrderText.Value,
            _ => _physicalText.Value
        };

    public IReadOnlyList<PdfRectangle> Search(
        string text,
        CaseSensitivity sensitivity = CaseSensitivity.CaseInsensitive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        StringComparison comparison = sensitivity == CaseSensitivity.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return TextList()
            .Where(box => box.Text.Contains(text, comparison))
            .Select(box => box.BoundingBox)
            .ToArray();
    }

    public string RenderToSvg(SvgRenderOptions? options = null) =>
        SvgPageRenderer.Render(this, options ?? new SvgRenderOptions());

    public void SaveSvg(string fileName, SvgRenderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        File.WriteAllText(fileName, RenderToSvg(options));
    }

    public PdfBitmap Render(RasterRenderOptions? options = null)
    {
        if (_owner.Locked)
            throw new PdfEncryptedException();
        return PdfRasterRenderer.Render(this, options ?? new RasterRenderOptions());
    }

    public byte[] RenderToPng(RasterRenderOptions? options = null) =>
        Render(options).ToPngBytes();

    public void SavePng(string fileName, RasterRenderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Render(options).SavePng(fileName);
    }

    internal PdfRectangle CropBox => _node.CropBox;
    internal PdfReadOptions ReadOptions => _document.Options;
    internal PdfFontDecoder? FindFont(string name) =>
        _fontDecoders.Value.Values.FirstOrDefault(
            font => string.Equals(font.Name, name, StringComparison.Ordinal));

    private IReadOnlyList<TextBox> Extract(TextLayout layout)
    {
        if (_owner.Locked)
            throw new PdfEncryptedException();
        return new PdfTextExtractor(_document, _node).Extract(layout);
    }

    private IReadOnlyList<PdfGraphicsElement> ExtractGraphics()
    {
        if (_owner.Locked)
            throw new PdfEncryptedException();
        return new PdfGraphicsInterpreter(_document, _node).Interpret();
    }

    private static bool Intersects(PdfRectangle left, PdfRectangle right) =>
        left.Left <= right.Right &&
        left.Right >= right.Left &&
        left.Bottom <= right.Top &&
        left.Top >= right.Bottom;
}
