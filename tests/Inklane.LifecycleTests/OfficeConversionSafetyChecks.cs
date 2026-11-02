using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Inklane.LifecycleTests;

internal static partial class Program
{
    private static void OfficeConversionSafety()
    {
        using var state = new WindowCase("conversion-policy");
        const string packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string rootRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        const string word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        string Package(string label,string extraRel="",string content="",string extraFile="")
        {
            string path = state.Path(label+".docx");
            using var archive = ZipFile.Open(path,ZipArchiveMode.Create);
            void Part(string name,string text)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(),new UTF8Encoding(false));
                writer.Write(text);
            }
            Part("[Content_Types].xml","<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>");
            Part("_rels/.rels",$"<Relationships xmlns='{packageRel}'><Relationship Id='r' Type='{rootRel}officeDocument' Target='word/document.xml'/>{extraRel}</Relationships>");
            Part("word/document.xml",$"<w:document xmlns:w='{word}'><w:body><w:p>{content}</w:p></w:body></w:document>");
            if (extraFile.Length > 0) Part(extraFile,"test");
            return path;
        }
        var assembly = typeof(MainWindow).Assembly;
        var check = assembly.GetType("Inklane.OfficeConversionPolicy")!.GetMethod("Check",BindingFlags.NonPublic | BindingFlags.Static)!;
        check.Invoke(null,[Package("self-contained"),CancellationToken.None]);
        var convert = assembly.GetType("Inklane.OfficePdfConverter")!.GetMethod("ConvertAsync")!;
        foreach (var path in new[]
        {
            Package("external-image",$"<Relationship Id='risk' Type='{rootRel}image' Target='https://example.invalid/remote.png' TargetMode='External'/>"),
            Package("embedded-macro",extraFile:"word/vbaProject.bin"),
            Package("dynamic-field",content:"<w:instrText>INCLUDETEXT external-file</w:instrText>"),
            Package("link-without-mode",$"<Relationship Id='risk' Type='{rootRel}image' Target='file:///C:/unsafe.png'/>"),
            Package("active-slide",content:"<w:object/>")
        })
        {
            string pdf = Path.ChangeExtension(path,".pdf");
            var task = (Task<OfficeReviewLink>)convert.Invoke(null,[state.Path("renderer-must-never-start.exe"),path,pdf,CancellationToken.None])!;
            try { task.GetAwaiter().GetResult(); throw new Exception("Unexpected automatic conversion."); }
            catch (InvalidDataException) { }
            Assert(!File.Exists(pdf),"A refused conversion left a file at the destination.");
        }
    }
}
