using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace Inklane.Core;

public static class PortableNotebook
{
    private static ReadOnlySpan<byte> Signature => "INKLANE1"u8;

    public static DocumentData Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[12];
        input.ReadExactly(header);
        if (!header[..8].SequenceEqual(Signature)) throw new InvalidDataException("Invalid workspace header.");
        int length = BinaryPrimitives.ReadInt32BigEndian(header[8..]);
        if (length < 2 || length > DocumentStore.MaxJsonBytes || input.Length < 12L + length)
            throw new InvalidDataException("Invalid workspace metadata size.");
        var bytes = new byte[length];
        input.ReadExactly(bytes);
        using var metadata = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = metadata.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("format",out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != "inklane.workspace" ||
            !root.TryGetProperty("version",out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 ||
            !root.TryGetProperty("document",out var model) || model.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unsupported workspace metadata.");
        if (!model.TryGetProperty("kind",out var kind) || kind.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Missing document kind.");
        if (kind.GetString() != "Notebook") throw new NotSupportedException();
        if (input.Length != 12L + length) throw new InvalidDataException("Unexpected trailing notebook data.");
        var notebook = DocumentStore.Deserialize(model.GetRawText());
        CheckNotebook(notebook);
        return notebook;
    }

    public static void Write(Stream destination, DocumentData notebook, string suggestedName)
    {
        CheckNotebook(notebook);
        var layer = DocumentStore.Serialize(notebook);
        using var metadata = new MemoryStream();
        using (var json = new Utf8JsonWriter(metadata))
        {
            json.WriteStartObject();
            json.WriteString("format","inklane.workspace");
            json.WriteNumber("version",1);
            json.WriteString("sourceName",Path.GetFileName(suggestedName));
            json.WritePropertyName("document");
            json.WriteRawValue(layer);
            json.WriteEndObject();
        }
        if (metadata.Length > DocumentStore.MaxJsonBytes) throw new InvalidDataException("Workspace metadata exceeds 32 MiB.");
        Span<byte> header = stackalloc byte[12];
        Signature.CopyTo(header);
        BinaryPrimitives.WriteInt32BigEndian(header[8..], (int)metadata.Length);
        destination.Write(header);
        metadata.Position = 0;
        metadata.CopyTo(destination);
    }

    private static void CheckNotebook(DocumentData notebook)
    {
        if (notebook.Kind != DocumentKind.Notebook || notebook.SourceImagePath != null || notebook.SourceImageSha256 != null ||
            notebook.SourcePdfName != null || notebook.SourcePdfSha256 != null)
            throw new InvalidDataException("A portable notebook cannot reference external source files.");
    }
}
