using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Inklane.Core;

namespace Inklane;

/// <summary>A source-preserving Office handoff: editable originals plus a visual review appendix and full text.</summary>
internal static class OfficeReviewExporter
{
    private static readonly XNamespace W="http://schemas.openxmlformats.org/wordprocessingml/2006/main",
        P="http://schemas.openxmlformats.org/presentationml/2006/main", A="http://schemas.openxmlformats.org/drawingml/2006/main",
        R="http://schemas.openxmlformats.org/officeDocument/2006/relationships", Rel="http://schemas.openxmlformats.org/package/2006/relationships",
        Types="http://schemas.openxmlformats.org/package/2006/content-types", WP="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing",
        Pic="http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly string Prefix="inklaneReview";
    public static void Write(Stream output,OfficeReviewLink source,DocumentData document,Func<int,byte[]> preview)
    {
        DocumentStore.Validate(document);
        using(var input=File.OpenRead(source.OfficePath))
        {
            if(input.Length>256L*1024*1024 || !string.Equals(Convert.ToHexString(SHA256.HashData(input)),source.OfficeHash,StringComparison.OrdinalIgnoreCase))
                throw new IOException(WorkflowText.T("SourceChanged"));
            input.Position=0;input.CopyTo(output);
        }
        using var zip=new ZipArchive(output,ZipArchiveMode.Update,true);
        if(zip.Entries.Any(e=>e.FullName.Contains(Prefix,StringComparison.Ordinal)))
            throw new InvalidDataException("This document already contains an Inklane review appendix. Export from the original source.");
        var types=Read(zip,"[Content_Types].xml");
        if(!types.Root!.Elements(Types+"Default").Any(e=>(string?)e.Attribute("Extension")=="png"))
            types.Root.Add(new XElement(Types+"Default",new XAttribute("Extension","png"),new XAttribute("ContentType","image/png")));
        if(Path.GetExtension(source.OfficePath).Equals(".docx",StringComparison.OrdinalIgnoreCase)) Word(zip,document,preview);
        else Presentation(zip,document,preview,types);
        Put(zip,"[Content_Types].xml",types);
    }
    private static XDocument Read(ZipArchive zip,string path)
    {
        var entry=zip.GetEntry(path)??throw new InvalidDataException("The Office package is incomplete.");
        if(entry.Length>32*1024*1024)throw new InvalidDataException("The Office part is too large.");
        using var stream=entry.Open();using var xml=XmlReader.Create(stream,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024});
        return XDocument.Load(xml,LoadOptions.PreserveWhitespace);
    }
    private static XDocument Relationships(ZipArchive zip,string path)=>zip.GetEntry(path)==null?new(new XElement(Rel+"Relationships")):Read(zip,path);
    private static string AddRel(XDocument rels,string type,string target)
    {
        int n=1;while(rels.Root!.Elements().Any(e=>(string?)e.Attribute("Id")=="rIdInklane"+n))n++;
        var id="rIdInklane"+n;rels.Root.Add(new XElement(Rel+"Relationship",new XAttribute("Id",id),new XAttribute("Type",R.NamespaceName+"/"+type),new XAttribute("Target",target)));return id;
    }
    private static void Put(ZipArchive zip,string path,XDocument xml)
    { zip.GetEntry(path)?.Delete();using var stream=zip.CreateEntry(path).Open();xml.Save(stream,SaveOptions.DisableFormatting); }
    private static void Bytes(ZipArchive zip,string path,byte[] bytes)
    {using var stream=zip.CreateEntry(path,CompressionLevel.Fastest).Open();stream.Write(bytes);}
    private static string Heading(int page)=>L.Language=="de"?$"Inklane-Feedback · Seite {page+1}":$"Inklane feedback · page {page+1}";
    private static XElement Paragraph(string text,bool heading=false)=>new(W+"p",
        heading?new XElement(W+"pPr",new XElement(W+"keepNext"),new XElement(W+"spacing",new XAttribute(W+"before",240),new XAttribute(W+"after",120))):null,
        new XElement(W+"r",heading?new XElement(W+"rPr",new XElement(W+"b"),new XElement(W+"sz",new XAttribute(W+"val",28))):null,
        new XElement(W+"t",new XAttribute(XNamespace.Xml+"space","preserve"),text)));
    private static IEnumerable<string> Explanations(PageData page)
    {
        int note=0;
        foreach(var text in page.Items)
        {
            string label=text.IsNote?$"#{++note} {(text.Title.Length>0?text.Title:"Remark")}":"Text";
            yield return string.Create(CultureInfo.InvariantCulture,$"{label} · x {text.X:0.#}, y {text.Y:0.#} (96 dpi)");
            foreach(var line in text.Text.Replace("\r","").Split('\n'))yield return line;
        }
    }
    private static void Word(ZipArchive zip,DocumentData document,Func<int,byte[]> preview)
    {
        var xml=Read(zip,"word/document.xml");var body=xml.Root?.Element(W+"body")??throw new InvalidDataException("The document body is missing.");
        var rels=Relationships(zip,"word/_rels/document.xml.rels");
        // Transfer the original final section settings onto a section break, then
        // give the review its own paper. Existing body elements keep their order.
        var section=body.Element(W+"sectPr");
        if(section!=null){section.Remove();body.Add(new XElement(W+"p",new XElement(W+"pPr",new XElement(section))));}
        else body.Add(new XElement(W+"p",new XElement(W+"r",new XElement(W+"br",new XAttribute(W+"type","page")))));
        for(int i=0;i<document.Pages.Count;i++)
        {
            body.Add(Paragraph(Heading(i),true));
            var name=Prefix+(i+1)+".png";Bytes(zip,"word/media/"+name,preview(i));
            var rid=AddRel(rels,"image","media/"+name);
            long cx=5_700_000,cy=(long)(cx*document.Pages[i].Height/document.Pages[i].Width);
            if(cy>7_700_000){cx=(long)(cx*7_700_000d/cy);cy=7_700_000;}
            body.Add(new XElement(W+"p",new XElement(W+"r",new XElement(W+"drawing",
                new XElement(WP+"inline",new XElement(WP+"extent",new XAttribute("cx",cx),new XAttribute("cy",cy)),
                    new XElement(WP+"docPr",new XAttribute("id",1_000_000+i),new XAttribute("name",Heading(i))),
                    new XElement(A+"graphic",new XElement(A+"graphicData",new XAttribute("uri",Pic.NamespaceName),
                        Picture(Pic,rid,cx,cy,i+1))))))));
            foreach(var line in Explanations(document.Pages[i]))body.Add(Paragraph(line));
            if(i<document.Pages.Count-1)body.Add(new XElement(W+"p",new XElement(W+"r",new XElement(W+"br",new XAttribute(W+"type","page")))));
        }
        body.Add(new XElement(W+"sectPr",new XElement(W+"pgSz",new XAttribute(W+"w",11906),new XAttribute(W+"h",16838)),
            new XElement(W+"pgMar",new XAttribute(W+"top",900),new XAttribute(W+"right",900),new XAttribute(W+"bottom",900),new XAttribute(W+"left",900))));
        Put(zip,"word/document.xml",xml);Put(zip,"word/_rels/document.xml.rels",rels);
    }
    private static XElement Picture(XNamespace ns,string rid,long cx,long cy,int id)=>new(ns+"pic",
        new XElement(ns+"nvPicPr",new XElement(ns+"cNvPr",new XAttribute("id",id),new XAttribute("name","Inklane review")),new XElement(ns+"cNvPicPr"),ns==P?new XElement(P+"nvPr"):null),
        new XElement(ns+"blipFill",new XElement(A+"blip",new XAttribute(R+"embed",rid)),new XElement(A+"stretch",new XElement(A+"fillRect"))),
        new XElement(ns+"spPr",new XElement(A+"xfrm",new XElement(A+"off",new XAttribute("x",0),new XAttribute("y",0)),new XElement(A+"ext",new XAttribute("cx",cx),new XAttribute("cy",cy))),
            new XElement(A+"prstGeom",new XAttribute("prst","rect"),new XElement(A+"avLst"))));
    private static void Presentation(ZipArchive zip,DocumentData document,Func<int,byte[]> preview,XDocument types)
    {
        var presentation=Read(zip,"ppt/presentation.xml");var rels=Relationships(zip,"ppt/_rels/presentation.xml.rels");
        var slides=presentation.Root!.Element(P+"sldIdLst")??throw new InvalidDataException("The presentation has no slides.");
        if(slides.Elements().Count()!=document.Pages.Count)throw new InvalidDataException("The rendered slides no longer match the presentation.");
        var size=presentation.Root.Element(P+"sldSz")!;long cx=(long)size.Attribute("cx")!,cy=(long)size.Attribute("cy")!;
        uint id=slides.Elements().Select(e=>(uint)e.Attribute("id")!).DefaultIfEmpty(255u).Max()+1;
        var firstId=(string)slides.Elements().First().Attribute(R+"id")!;
        var firstTarget=(string)rels.Root!.Elements().Single(e=>(string?)e.Attribute("Id")==firstId).Attribute("Target")!;
        var firstPath=Resolve("ppt/presentation.xml",firstTarget);
        var firstRels=Read(zip,Path.GetDirectoryName(firstPath)!.Replace('\\','/')+"/_rels/"+Path.GetFileName(firstPath)+".rels");
        var layoutTarget=(string)firstRels.Root!.Elements().First(e=>(string?)e.Attribute("Type")==R.NamespaceName+"/slideLayout").Attribute("Target")!;
        var layout=Resolve(firstPath,layoutTarget);
        var authorsName="ppt/"+Prefix+"Authors.xml";
        var existing=rels.Root.Elements().FirstOrDefault(e=>(string?)e.Attribute("Type")==R.NamespaceName+"/commentAuthors");
        XDocument authors;
        if(existing!=null){authorsName=Resolve("ppt/presentation.xml",(string)existing.Attribute("Target")!);authors=Read(zip,authorsName);}
        else{authors=new(new XElement(P+"cmAuthorLst"));AddRel(rels,"commentAuthors",Path.GetFileName(authorsName));Override(types,authorsName,"commentAuthors");}
        int author=authors.Root!.Elements().Select(e=>(int?)e.Attribute("id")??0).DefaultIfEmpty(-1).Max()+1;
        int commentIndex=0;
        for(int i=0;i<document.Pages.Count;i++)
        {
            string name=Prefix+(i+1),slidePath="ppt/slides/"+name+".xml";
            Bytes(zip,"ppt/media/"+name+".png",preview(i));
            var links=new XDocument(new XElement(Rel+"Relationships"));var imageId=AddRel(links,"image","../media/"+name+".png");
            AddRel(links,"slideLayout",new Uri("https://package.invalid/ppt/slides/").MakeRelativeUri(new Uri("https://package.invalid/"+layout)).ToString());
            var tree=new XElement(P+"spTree",new XElement(P+"nvGrpSpPr",new XElement(P+"cNvPr",new XAttribute("id",1),new XAttribute("name","")),new XElement(P+"cNvGrpSpPr"),new XElement(P+"nvPr")),new XElement(P+"grpSpPr"),Picture(P,imageId,cx,cy,2));
            Put(zip,slidePath,new(new XElement(P+"sld",new XElement(P+"cSld",new XAttribute("name",Heading(i)),tree),new XElement(P+"clrMapOvr",new XElement(A+"masterClrMapping")))));
            var comments=new XDocument(new XElement(P+"cmLst"));
            // Full explanations live in native comments, never in a clipped text box.
            comments.Root!.Add(new XElement(P+"cm",new XAttribute("authorId",author),new XAttribute("dt",DateTime.UtcNow.ToString("O")),new XAttribute("idx",++commentIndex),
                new XElement(P+"pos",new XAttribute("x",0),new XAttribute("y",0)),new XElement(P+"text",Heading(i)+"\n"+string.Join("\n",Explanations(document.Pages[i])))));
            Put(zip,"ppt/comments/"+name+".xml",comments);AddRel(links,"comments","../comments/"+name+".xml");
            Put(zip,"ppt/slides/_rels/"+name+".xml.rels",links);
            var rid=AddRel(rels,"slide","slides/"+name+".xml");slides.Add(new XElement(P+"sldId",new XAttribute("id",id++),new XAttribute(R+"id",rid)));
            Override(types,slidePath,"slide");Override(types,"ppt/comments/"+name+".xml","comments");
        }
        authors.Root.Add(new XElement(P+"cmAuthor",new XAttribute("id",author),new XAttribute("name","Inklane"),new XAttribute("initials","I"),new XAttribute("lastIdx",commentIndex),new XAttribute("clrIdx",author)));
        Put(zip,authorsName,authors);Put(zip,"ppt/presentation.xml",presentation);Put(zip,"ppt/_rels/presentation.xml.rels",rels);
    }
    private static void Override(XDocument types,string path,string kind)=>types.Root!.Add(new XElement(Types+"Override",new XAttribute("PartName","/"+path),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.presentationml."+kind+"+xml")));
    private static string Resolve(string part,string target)
    {
        var result=new Uri(new Uri("https://package.invalid/"+part),target);
        if(result.Host!="package.invalid" || result.Scheme!="https")throw new InvalidDataException("An Office relationship points outside the package.");
        return result.AbsolutePath.TrimStart('/');
    }
}
