namespace Poppler.Rendering;

public sealed record SvgRenderOptions
{
    public double Scale { get; init; } = 1;
    public string Background { get; init; } = "#ffffff";
    public string Foreground { get; init; } = "#111111";
    public bool IncludeVectorGraphics { get; init; } = true;
    public bool IncludeText { get; init; } = true;
    public bool DrawTextBounds { get; init; }
    public bool DrawImageBounds { get; init; }
}
