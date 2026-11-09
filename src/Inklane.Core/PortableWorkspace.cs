using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Inklane.Core;

public sealed class PortableCapacityException(string message) : IOException(message);

/// <summary>A portable workspace contains a bounded editable layer followed by the exact optional source bytes.</summary>
public sealed class PortableWorkspace
{
    private static ReadOnlySpan<byte> Signature => "INKLANE1"u8;
    public const int MaxSourceBytes = 64 * 1024 * 1024;
    private const string LayerSuffix = ".inklane.json";
    public DocumentData Document { get; }
    public string SourceName { get; }
    public byte[] SourceBytes { get; }

    private PortableWorkspace(DocumentData document, string name, byte[] bytes)
    {
        Document = document;
        SourceName = name;
        SourceBytes = bytes;
    }

    public static PortableWorkspace Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(input);
    }

    /// <summary>Read one bounded workspace from the current stream position, without closing the caller's stream.</summary>
    public static PortableWorkspace Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead) throw new ArgumentException("The workspace stream must be readable.", nameof(input));
        Span<byte> header = stackalloc byte[12];
        input.ReadExactly(header);
        if (!header[..8].SequenceEqual(Signature)) throw new InvalidDataException("Invalid workspace header.");
        int length = BinaryPrimitives.ReadInt32BigEndian(header[8..]);
        if (length < 2 || length > DocumentStore.MaxJsonBytes)
            throw new InvalidDataException("The workspace exceeds the supported metadata size.");
        if (input.CanSeek && (input.Length - input.Position - length is < 0 or > MaxSourceBytes))
            throw new InvalidDataException("The workspace exceeds the supported size or is incomplete.");
        var bytes = new byte[length];
        input.ReadExactly(bytes);
        using var metadata = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = metadata.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("format",out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != "inklane.workspace" ||
            !root.TryGetProperty("version",out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 ||
            !root.TryGetProperty("sourceName",out var filename) || filename.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("document",out var model) || model.ValueKind != JsonValueKind.Object ||
            !model.TryGetProperty("kind",out var kind) || kind.ValueKind != JsonValueKind.String ||
            kind.GetString() is not ("Notebook" or "Image" or "Pdf"))
            throw new InvalidDataException("Unsupported workspace metadata.");
        var name = filename.GetString()!;
        var document = DocumentStore.Deserialize(model.GetRawText());
        if (document.Kind == DocumentKind.Notebook)
        {
            CheckNotebook(document);
            if (input.ReadByte() != -1) throw new InvalidDataException("Unexpected trailing notebook data.");
            return new(document,name,[]);
        }
        CheckSourceName(name,document.Kind);
        var declaredName = document.Kind == DocumentKind.Pdf ? document.SourcePdfName : document.SourceImagePath;
        if (!string.Equals(name,declaredName,StringComparison.Ordinal))
            throw new InvalidDataException("The source name or file does not match the annotation layer.");
        byte[] original;
        if (input.CanSeek)
        {
            long remaining=input.Length-input.Position;
            if (remaining is <=0 or >MaxSourceBytes) throw new InvalidDataException("The workspace source is missing or too large.");
            original=new byte[(int)remaining];input.ReadExactly(original);
            if(input.ReadByte()!=-1)throw new InvalidDataException("The workspace changed while reading.");
        }
        else
        {
            // A sequential transfer may not declare its total length. Read at most
            // one byte beyond the limit, and reject before storing that extra byte.
            using var source = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, MaxSourceBytes - source.Length + 1))) != 0)
            {
                if (source.Length + read > MaxSourceBytes) throw new InvalidDataException("The workspace source exceeds 64 MiB.");
                source.Write(buffer, 0, read);
            }
            if (source.Length == 0) throw new InvalidDataException("The workspace source is missing.");
            original = source.ToArray();
        }
        CheckFingerprint(document,Convert.ToHexString(SHA256.HashData(original)));
        return new(document,name,original);
    }

    public static void Write(Stream destination, DocumentData document, string name, string? sourcePath = null)
    {
        if (document.Kind == DocumentKind.Notebook)
        {
            CheckNotebook(document);
            WriteMetadata(destination,document,Path.GetFileName(name));
            return;
        }
        if (sourcePath == null) throw new InvalidDataException("Missing source file.");
        name = Path.GetFileName(sourcePath);
        CheckSourceName(name,document.Kind);
        using var source = new FileStream(sourcePath,FileMode.Open,FileAccess.Read,FileShare.Read);
        if (source.Length is <=0 or >MaxSourceBytes) throw new PortableCapacityException("The portable source must fit in 64 MiB.");
        CheckFingerprint(document,Convert.ToHexString(SHA256.HashData(source)));
        source.Position = 0;
        var safe = document.WithSourceName(name);
        if (safe.Kind == DocumentKind.Image && safe.Pages.Any(p => p.Width*p.Height>32_000_000 || Math.Max(p.Width,p.Height)>32768))
            throw new PortableCapacityException("The image exceeds the portable workspace dimensions.");
        WriteMetadata(destination,safe,name);
        source.CopyTo(destination);
    }

    private static void WriteMetadata(Stream destination, DocumentData document, string name)
    {
        var layer = DocumentStore.Serialize(document);
        // Shared web/native readers bound the decoded resources independently of the serialized byte limit.
        long pixels = 0, points = 0;
        foreach (var page in document.Pages)
        {
            if (page.Images.Count > 100 || page.Items.Count > 2000 || page.Shapes.Count > 2000 || page.Ink.Count > 10000)
                throw new PortableCapacityException("The page has too many objects for a portable workspace.");
            foreach (var picture in page.Images) pixels += EmbeddedPng.CheckHeader(picture.PngData);
            foreach (var stroke in page.Ink) points += stroke.Points.Count;
            if (pixels > 32_000_000 || points > 500_000)
                throw new PortableCapacityException("Portable workspaces allow 32 million embedded pixels and 500,000 ink samples.");
        }
        using var metadata = new MemoryStream();
        using (var json = new Utf8JsonWriter(metadata))
        {
            json.WriteStartObject();
            json.WriteString("format","inklane.workspace");
            json.WriteNumber("version",1);
            json.WriteString("sourceName",name);
            json.WritePropertyName("document");
            json.WriteRawValue(layer);
            json.WriteEndObject();
        }
        if (metadata.Length > DocumentStore.MaxJsonBytes) throw new PortableCapacityException("Workspace metadata exceeds 32 MiB.");
        Span<byte> header = stackalloc byte[12];
        Signature.CopyTo(header);
        BinaryPrimitives.WriteInt32BigEndian(header[8..], (int)metadata.Length);
        destination.Write(header); metadata.Position = 0; metadata.CopyTo(destination);
    }

    private static void CheckSourceName(string name, DocumentKind kind)
    {
        // This value becomes one Windows path component, with room for the sibling JSON suffix.
        if (string.IsNullOrWhiteSpace(name) || name.Length + LayerSuffix.Length > 255 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(name) != name ||
            name.EndsWith(' ') || name.EndsWith('.') || name.Any(char.IsControl))
            throw new InvalidDataException("Unsafe portable source filename.");
        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem.Length == 0 || stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                "0123456789¹²³".Contains(stem[3])))
            throw new InvalidDataException("Reserved portable source filename.");
        string ext = Path.GetExtension(name).ToLowerInvariant();
        if (kind == DocumentKind.Image ? ext is not (".png" or ".jpg" or ".jpeg") : kind != DocumentKind.Pdf || ext != ".pdf")
            throw new InvalidDataException("The filename and source type disagree.");
    }

    private static void CheckNotebook(DocumentData notebook)
    {
        if (notebook.SourceImagePath != null || notebook.SourceImageSha256 != null ||
            notebook.SourcePdfName != null || notebook.SourcePdfSha256 != null)
            throw new InvalidDataException("A portable notebook cannot reference external source files.");
    }

    private static void CheckFingerprint(DocumentData document, string actual)
    {
        var expected = document.Kind == DocumentKind.Pdf ? document.SourcePdfSha256 : document.SourceImageSha256;
        if (!string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source bytes do not match the annotation layer.");
    }

    /// <summary>Publish the complete pair as a newly named directory under the chosen parent.</summary>
    public (string Original,string Layer,string LayerHash) CreateWorkingCopy(string parent)
    {
        if (Document.Kind == DocumentKind.Notebook) throw new InvalidOperationException();
        parent = Path.GetFullPath(parent);
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException();
        var stage = Path.Combine(parent,".inklane-import-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(stage) || File.Exists(stage)) throw new IOException("The temporary folder already exists.");
        var staging = Directory.CreateDirectory(stage);
        if ((staging.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The temporary working folder is a redirected directory.");
        var original = Path.Combine(stage,SourceName);
        var layer = original + LayerSuffix;
        var created = new List<string>();
        try
        {
            void Create(string path, byte[] content)
            {
                using var stream = new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                created.Add(path);stream.Write(content);stream.Flush(true);
            }
            Create(original,SourceBytes);
            var json = Encoding.UTF8.GetBytes(DocumentStore.Serialize(Document));
            Create(layer,json);
            var hash = Convert.ToHexString(SHA256.HashData(json));
            var stem = Path.GetFileNameWithoutExtension(SourceName);
            for (int n=1;n<=1000;n++)
            {
                var suffix = n==1 ? "" : " (" + n.ToString(CultureInfo.InvariantCulture) + ")";
                var directory = Path.Combine(parent,stem+" - Inklane"+suffix);
                try { Directory.Move(stage,directory); }
                catch(IOException) when (Directory.Exists(directory) || File.Exists(directory)) { continue; }
                var saved = Path.Combine(directory,SourceName);
                return (saved,saved+LayerSuffix,hash);
            }
            throw new IOException("Choose another destination for this review.");
        }
        catch
        {
            // Remove only files created by this operation, never pre-existing directory contents.
            foreach (var path in created) File.Delete(path);
            Directory.Delete(stage,recursive:false);
            throw;
        }
    }
}
