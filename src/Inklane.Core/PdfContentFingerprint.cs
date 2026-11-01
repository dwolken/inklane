using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace Inklane.Core;

/// <summary>
/// A bounded canonical walk of PDFsharp's page object graph. Object numbers are intentionally
/// excluded: PDF writers may renumber references without changing their meaning.
/// </summary>
internal sealed class PdfContentFingerprint : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly Dictionary<PdfObject, int> ancestors = new(ReferenceEqualityComparer.Instance);
    private int nodes;
    private long bytes;

    public static string Compute(PdfDocument document)
    {
        using var visitor = new PdfContentFingerprint();
        visitor.Token("format", "Inklane page graph 2");
        visitor.Token("catalog", "");
        foreach (var key in document.Internals.Catalog.Elements.Keys.Order(StringComparer.Ordinal))
        {
            // Pages are visited separately in their reading order. Exclude the fingerprint itself
            // and informational XMP timestamps, which PDFsharp may regenerate while saving.
            if (key is "/Pages" or "/Metadata" or "/InklaneContentHash" or "/InklaneContentHashVersion") continue;
            visitor.Token("key", key);
            visitor.Visit(document.Internals.Catalog.Elements[key], 0);
        }
        foreach (var page in document.Pages)
        {
            visitor.Token("page", "");
            visitor.Visit(page, 0);
            // Resources, page boxes and rotation may be inherited from the /Pages tree.
            // Hash ancestor entries without following /Kids back into every page in the document.
            var parent = page.Elements.GetDictionary("/Parent");
            var parents = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            while (parent != null)
            {
                if (!parents.Add(parent) || parents.Count > 128)
                    throw new InvalidDataException("The PDF page ancestry is cyclic or too deep.");
                visitor.Token("page-parent", "");
                foreach (var key in new[] { "/Resources", "/MediaBox", "/CropBox", "/Rotate" })
                {
                    visitor.Token("key", key);
                    visitor.Visit(parent.Elements[key], 0);
                }
                parent = parent.Elements.GetDictionary("/Parent");
            }
        }
        return Convert.ToHexString(visitor.hash.GetHashAndReset());
    }

    private void Visit(PdfItem? item, int depth)
    {
        if (++nodes > 2_000_000 || depth > 128)
            throw new InvalidDataException("The PDF object graph exceeds the supported complexity limit.");
        if (item is PdfReference reference) { Visit(reference.Value, depth); return; }
        if (item is PdfObject obj)
        {
            if (ancestors.TryGetValue(obj, out var ancestorDepth))
            {
                Token("cycle", ancestorDepth.ToString(CultureInfo.InvariantCulture));
                return;
            }
            ancestors.Add(obj, depth);
        }
        try
        {
            switch (item)
            {
                case null or PdfNull or PdfNullObject: Token("null", ""); break;
                case PdfDictionary dictionary: Dictionary(dictionary, depth); break;
                case PdfRectangle rectangle:
                    Token("array", "4");
                    Real(rectangle.X1); Real(rectangle.Y1); Real(rectangle.X2); Real(rectangle.Y2);
                    Token("end-array", "");
                    break;
                case PdfArray array:
                    Token("array", array.Elements.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (var child in array.Elements) Visit(child, depth + 1);
                    Token("end-array", "");
                    break;
                case PdfBoolean value: Boolean(value.Value); break;
                case PdfBooleanObject value: Boolean(value.Value); break;
                case PdfInteger value: Number(value.Value); break;
                case PdfIntegerObject value: Number(value.Value); break;
                case PdfLongInteger value: Number(value.Value); break;
                case PdfLongIntegerObject value: Number(value.Value); break;
                case PdfReal value: Real(value.Value); break;
                case PdfRealObject value: Real(value.Value); break;
                case PdfName value: Token("name", value.Value); break;
                case PdfNameObject value: Token("name", value.Value); break;
                case PdfString value: Token("string", value.Value); break;
                case PdfStringObject value: Token("string", value.Value); break;
                default:
                    // Parsed PDF objects should use the scalar/container types above. Do not
                    // silently ignore a new representation whose content has not been verified.
                    throw new NotSupportedException($"The PDF contains an unsupported object type: {item.GetType().Name}.");
            }
        }
        finally { if (item is PdfObject objectItem) ancestors.Remove(objectItem); }
    }

    private void Dictionary(PdfDictionary dictionary, int depth)
    {
        Token("dictionary", "");
        var type = dictionary.Elements.GetName("/Type");
        foreach (var key in dictionary.Elements.Keys.Order(StringComparer.Ordinal))
        {
            if (key == "/Length" && dictionary.Stream != null) continue; // Derived from framed bytes below.
            if (key == "/Parent" && type is "/Page" or "/Pages") continue;
            Token("key", key);
            Visit(dictionary.Elements[key], depth + 1);
        }
        if (dictionary.Stream is { } stream)
        {
            // Include encoded bytes and the complete /Filter + /DecodeParms dictionaries.
            // This handles fonts and JPEGs without requiring a second image/PDF decoder.
            TokenBytes("stream", stream.Value);
        }
        Token("end-dictionary", "");
    }

    private void Boolean(bool value) => Token("boolean", value ? "true" : "false");
    private void Number(long value) => Token("number", value.ToString(CultureInfo.InvariantCulture));
    private void Real(double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("The PDF contains a nonfinite number.");
        // A millionth of a PDF point is below meaningful document geometry, while avoiding
        // false differences when another serializer rounds a value such as 766.8897638.
        Token("number", value.ToString("0.######", CultureInfo.InvariantCulture));
    }
    private void Token(string kind, string value) => TokenBytes(kind, Encoding.UTF8.GetBytes(value));
    private void TokenBytes(string kind, byte[] value)
    {
        bytes += value.Length;
        if (bytes > 512L * 1024 * 1024)
            throw new InvalidDataException("The PDF referenced content exceeds the supported verification limit.");
        hash.AppendData(Encoding.ASCII.GetBytes(kind + ":"));
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
    public void Dispose() => hash.Dispose();
}
