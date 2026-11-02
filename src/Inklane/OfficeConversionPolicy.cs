using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Windows.Media.Imaging;

namespace Inklane;

// The automatic renderer accepts a bounded subset of self-contained OOXML.
// More complex packages can use a PDF exported in their authoring application.
internal static partial class OfficeConversionPolicy
{
    [GeneratedRegex(@"^(?:\[Content_Types\]\.xml|docProps/(?:app|core|custom)\.xml|(?:word|ppt)/theme/theme\d+\.xml|word/(?:document|styles|stylesWithEffects|settings|webSettings|fontTable|numbering|footnotes|endnotes|comments|header\d+|footer\d+)\.xml|ppt/(?:presentation|presProps|viewProps|tableStyles|commentAuthors)\.xml|ppt/(?:slides/slide|slideLayouts/slideLayout|slideMasters/slideMaster|notesSlides/notesSlide|notesMasters/notesMaster)\d+\.xml)$",RegexOptions.CultureInvariant)]
    private static partial Regex XmlPart();
    [GeneratedRegex(@"^(?:docProps/thumbnail|(?:word|ppt)/media/[\w.-]+)\.(?:png|jpe?g)$",RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImagePart();

    private static readonly HashSet<string> RelationshipTypes = new(StringComparer.Ordinal)
    {
        "officeDocument","styles","stylesWithEffects","settings","webSettings","fontTable","numbering","header","footer",
        "footnotes","endnotes","comments","theme","core-properties","extended-properties","custom-properties","thumbnail",
        "slide","slideLayout","slideMaster","notesSlide","notesMaster","presProps","viewProps","tableStyles","commentAuthors","image"
    };
    private static readonly HashSet<string> ActiveElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "object","oleObj","OLEObject","control","activeX","altChunk","externalData","attachedTemplate","mailMerge",
        "subDoc","instrText","delInstrText","fldSimple","fldChar","script","audioFile","videoFile","contentPart",
        "movie","imagedata","hyperlink","hlinkClick","hlinkHover","legacyDrawing","legacyDrawingHF"
    };
    private static bool SafeContentType(string value)
    {
        const string office = "application/vnd.openxmlformats-officedocument.";
        return value is "application/xml" or "image/png" or "image/jpeg" or
            "application/vnd.openxmlformats-package.relationships+xml" or "application/vnd.openxmlformats-package.core-properties+xml" ||
            value.StartsWith(office,StringComparison.Ordinal) && value[office.Length..] is
                "theme+xml" or "extended-properties+xml" or "custom-properties+xml" or
                "wordprocessingml.document.main+xml" or "wordprocessingml.styles+xml" or "wordprocessingml.settings+xml" or
                "wordprocessingml.webSettings+xml" or "wordprocessingml.fontTable+xml" or "wordprocessingml.numbering+xml" or
                "wordprocessingml.header+xml" or "wordprocessingml.footer+xml" or "wordprocessingml.footnotes+xml" or
                "wordprocessingml.endnotes+xml" or "wordprocessingml.comments+xml" or
                "presentationml.presentation.main+xml" or "presentationml.slide+xml" or "presentationml.slideLayout+xml" or
                "presentationml.slideMaster+xml" or "presentationml.notesSlide+xml" or "presentationml.notesMaster+xml" or
                "presentationml.presProps+xml" or "presentationml.viewProps+xml" or "presentationml.tableStyles+xml" or
                "presentationml.commentAuthors+xml";
    }
    internal static void Check(string path, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(path);
        var main = Path.GetExtension(path).Equals(".docx",StringComparison.OrdinalIgnoreCase) ? "word/document.xml" : "ppt/presentation.xml";
        if (zip.Entries.Count is 0 or > 2048 || zip.GetEntry(main) == null || zip.GetEntry("[Content_Types].xml") == null ||
            zip.GetEntry("_rels/.rels") == null) throw Unsupported();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0, pixels = 0;
        int mainReferences = 0;
        bool declaredMain = false;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string name = entry.FullName;
            if (!names.Add(name) || name.Length > 240 || name.Contains('\\') || name.Contains("..",StringComparison.Ordinal) ||
                entry.Length is < 0 or > 16 * 1024 * 1024 || (expanded += entry.Length) > 128L * 1024 * 1024)
                throw Unsupported();
            if (ImagePart().IsMatch(name))
            {
                using var raw = entry.Open();
                byte[] bytes = new byte[entry.Length];
                raw.ReadExactly(bytes);
                if (raw.ReadByte() >= 0) throw Unsupported();
                using var copy = new MemoryStream(bytes,writable:false);
                BitmapDecoder decoder = name.EndsWith(".png",StringComparison.OrdinalIgnoreCase)
                    ? new PngBitmapDecoder(copy,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None)
                    : new JpegBitmapDecoder(copy,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                if ((pixels += (long)frame.PixelHeight * frame.PixelWidth) > 64_000_000) throw Unsupported();
                continue;
            }
            var isRelationships = name == "_rels/.rels" || name.EndsWith(".xml.rels",StringComparison.Ordinal) &&
                name.Contains("/_rels/",StringComparison.Ordinal) && XmlPart().IsMatch(name.Replace("/_rels/","/",StringComparison.Ordinal)[..^5]);
            if (!isRelationships && !XmlPart().IsMatch(name)) throw Unsupported();
            using var input = entry.Open();
            using var xml = XmlReader.Create(input,new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000
            });
            while (xml.Read())
            {
                if (xml.NodeType == XmlNodeType.ProcessingInstruction) throw Unsupported();
                if (xml.NodeType != XmlNodeType.Element) continue;
                if (xml.Depth > 128 || ActiveElements.Contains(xml.LocalName) || xml.NamespaceURI == "urn:schemas-microsoft-com:vml")
                    throw Unsupported();
                if (isRelationships)
                {
                    if (xml.NamespaceURI != "http://schemas.openxmlformats.org/package/2006/relationships") throw Unsupported();
                    if (xml.LocalName == "Relationship")
                    {
                        var target = CheckRelationship(xml,name,zip);
                        if (name == "_rels/.rels" && xml.GetAttribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument")
                        {
                            if (target != main) throw Unsupported();
                            mainReferences++;
                        }
                    }
                    else if (xml.LocalName != "Relationships") throw Unsupported();
                }
                else if (name == "[Content_Types].xml" && xml.GetAttribute("ContentType") is { } mime)
                {
                    if (!SafeContentType(mime)) throw Unsupported();
                    if (xml.LocalName == "Override" && xml.GetAttribute("PartName") == "/" + main)
                    {
                        if (declaredMain || mime != (main.StartsWith("word/",StringComparison.Ordinal)
                            ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"
                            : "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml")) throw Unsupported();
                        declaredMain = true;
                    }
                }
                else
                {
                    bool wordCompatibility = xml.LocalName == "compatSetting" && xml.NamespaceURI == "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    while (xml.MoveToNextAttribute())
                    {
                        if (xml.Prefix == "xmlns" || xml.Name == "xmlns") continue;
                        string value = xml.Value.TrimStart();
                        if (wordCompatibility && xml.LocalName == "uri" && value == "http://schemas.microsoft.com/office/word") continue;
                        if (xml.LocalName is "href" or "src" or "link" or "action" or "base" or "schemaLocation" ||
                            value.Contains("://",StringComparison.Ordinal) || value.StartsWith(@"\\",StringComparison.Ordinal) ||
                            value.StartsWith("file:",StringComparison.OrdinalIgnoreCase) || value.StartsWith("data:",StringComparison.OrdinalIgnoreCase))
                            throw Unsupported();
                    }
                    xml.MoveToElement();
                }
            }
        }
        if (mainReferences != 1 || !declaredMain) throw Unsupported();
    }

    private static string CheckRelationship(XmlReader xml,string part,ZipArchive zip)
    {
        string target = xml.GetAttribute("Target") ?? "";
        string type = xml.GetAttribute("Type") ?? "";
        if (xml.GetAttribute("TargetMode") is not (null or "Internal") ||
            !(type.StartsWith("http://schemas.openxmlformats.org/officeDocument/2006/relationships/",StringComparison.Ordinal) ||
              type.StartsWith("http://schemas.openxmlformats.org/package/2006/relationships/",StringComparison.Ordinal)) ||
            !RelationshipTypes.Contains(type[(type.LastIndexOf('/')+1)..]) ||
            target.Length == 0 || target.StartsWith('/') || target.IndexOfAny([':', '\\','%','?','#']) >= 0)
            throw Unsupported();
        string folder = part == "_rels/.rels" ? "" : part[..(part.IndexOf("/_rels/",StringComparison.Ordinal)+1)];
        // OPC uses relative URI paths. Resolve against a fixed in-memory base, without performing I/O.
        var resolved = new Uri(new Uri("https://package.invalid/"+folder),target);
        if (resolved.Host != "package.invalid" || zip.GetEntry(resolved.AbsolutePath[1..]) == null) throw Unsupported();
        return resolved.AbsolutePath[1..];
    }

    private static InvalidDataException Unsupported() => new(L.T("OfficeConvertUnsupported"));
}
