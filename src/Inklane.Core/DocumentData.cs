namespace Inklane.Core;

public enum DocumentKind { Notebook, Image }
public enum PaperPattern { Blank, Ruled, Squared }

/// <summary>Editable document coordinates are device-independent pixels (96 per inch).</summary>
public sealed class DocumentData
{
    private int declaredVersion = 1;
    // Recovery and clipboard writers use different JSON options. The model itself
    // advertises the minimum reader it needs, without masking an unknown input version.
    public int SchemaVersion
    {
        get => declaredVersion == 1 && (!string.IsNullOrEmpty(Title) || Pages?.Any(page =>
            page?.Images is { Count: > 0 } || page?.Items?.Any(item => !string.IsNullOrEmpty(item?.Title)) == true) == true)
            ? 2 : declaredVersion;
        set => declaredVersion = value;
    }
    public DocumentKind Kind { get; set; }
    public string Title { get; set; } = "";
    public List<PageData> Pages { get; set; } = [];
    public string? SourceImagePath { get; set; }
    public string? SourceImageSha256 { get; set; }
}

public sealed class PageData
{
    public double Width { get; set; } = Units.MmToDip(210);
    public double Height { get; set; } = Units.MmToDip(297);
    public PaperPattern Pattern { get; set; }
    public double SpacingMm { get; set; } = 5;
    public double MarginMm { get; set; } = 15;
    public List<InkData> Ink { get; set; } = [];
    public List<TextItem> Items { get; set; } = [];
    public List<ImageItem> Images { get; set; } = [];
}

/// <summary>A portable PNG placed in page coordinates, below ink and text.</summary>
public sealed class ImageItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public byte[] PngData { get; set; } = [];
}

public sealed class InkData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Color { get; set; } = "#202020";
    public double Width { get; set; } = 2;
    public List<InkPoint> Points { get; set; } = [];
}

public sealed class InkPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public float Pressure { get; set; } = .5f;
}

public sealed class TextItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 80;
    public double FontSize { get; set; } = 18;
    public string Color { get; set; } = "#202020";
    public bool IsNote { get; set; }
    public bool IsExpanded { get; set; } = true;
}

public static class Units
{
    public static double MmToDip(double millimeters) => millimeters * 96 / 25.4;
    public static double DipToPoint(double dip) => dip * 72 / 96;
    public static double DipToMm(double dip) => dip * 25.4 / 96;
    public static double MmToPoint(double millimeters) => millimeters * 72 / 25.4;
}
