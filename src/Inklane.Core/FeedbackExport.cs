using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Inklane.Core;

/// <summary>A portable report of marks on one exact source. Only explicitly listed fields leave the editor.</summary>
public static class FeedbackExport
{
    private const string Format = "inklane.feedback";
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static void Write(Stream output, DocumentData document, string sourceName, string sourceHash)
    {
        DocumentStore.Validate(document);
        var name = Path.GetFileName(sourceName);
        var expectedHash = document.Kind == DocumentKind.Pdf ? document.SourcePdfSha256 : document.SourceImageSha256;
        var mediaType = (document.Kind, Path.GetExtension(name).ToLowerInvariant()) switch
        {
            (DocumentKind.Pdf or DocumentKind.Notebook, ".pdf") => "application/pdf",
            (DocumentKind.Image, ".png") => "image/png",
            (DocumentKind.Image, ".jpg" or ".jpeg") => "image/jpeg",
            _ => throw new NotSupportedException("Feedback export requires a source image or PDF.")
        };
        if (sourceHash.Length != 64 || !sourceHash.All(Uri.IsHexDigit) ||
            document.Kind != DocumentKind.Notebook && !string.Equals(sourceHash,expectedHash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The source fingerprint does not match the reviewed document.");
        var unit = document.Kind == DocumentKind.Image ? "image-pixel" : "dip-96";
        JsonSerializer.Serialize(output, new
        {
            format = Format, version = 1,
            source = new { name, mediaType, sha256 = sourceHash.ToLowerInvariant() },
            coordinateSystem = "top-left",
            pages = document.Pages.Select((page, i) => new
            {
                number = i + 1, page.Width, page.Height, unit, annotations = Annotations(page)
            })
        }, Options);
    }

    private static IEnumerable<object> Annotations(PageData page)
    {
        foreach (var image in page.Images)
            yield return new { image.Id, type = "picture", image.X, image.Y, image.Width, image.Height,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(image.PngData)) };
        foreach (var ink in page.Ink)
            yield return new { ink.Id, type = "ink", ink.Color, ink.Width,
                points = ink.Points.Select(p => new { p.X, p.Y, p.Pressure }) };
        foreach (var item in page.Items)
        {
            if (item.IsNote)
                yield return new { item.Id, type = "note", item.X, item.Y, item.Width, item.Height, item.Text, item.Title, item.Color, item.FontSize };
            else
                yield return new { item.Id, type = "text", item.X, item.Y, item.Width, item.Height, item.Text, item.Color, item.FontSize };
        }
    }

    // Save As can give editable annotation files arbitrary JSON filenames.
    // Only replace a prior feedback report for the same source, even when the suffix matches.
    public static bool IsMatchingReport(Stream existing, string sourceHash)
    {
        if (existing.Length > 2L * DocumentStore.MaxJsonBytes) return false;
        try
        {
            using var data = JsonDocument.Parse(existing);
            var root = data.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String && format.ValueEquals(Format) &&
                root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var v) && v == 1 &&
                root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object &&
                source.TryGetProperty("sha256", out var hash) && hash.ValueKind == JsonValueKind.String &&
                string.Equals(hash.GetString(), sourceHash, StringComparison.OrdinalIgnoreCase) &&
                root.TryGetProperty("pages",out var pages) && pages.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException) { return false; }
    }
}
