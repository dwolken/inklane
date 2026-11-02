using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Inklane.Core;

namespace Inklane.LifecycleTests;

internal static partial class Program
{
    private static string ReviewFixture(string format) => Path.Combine(AppContext.BaseDirectory,"source-" + format + ".inklane");

    private static void PortableReviewJourney()
    {
        foreach (var format in new[] {"png","jpg","pdf"})
        {
            using var state = new WindowCase("portable-review-" + format);
            var w = state.Window;
            string incoming = ReviewFixture(format);
            var incomingHash = DocumentStore.GetFileHash(incoming);
            var original = PortableWorkspace.Read(incoming);
            var root = state.Path("Work");
            Directory.CreateDirectory(root);
            string stem = Path.GetFileNameWithoutExtension(original.SourceName) + " - Inklane";
            string occupied = Path.Combine(root,stem);
            Directory.CreateDirectory(occupied);
            var sentinel = Path.Combine(occupied,"prior.txt");
            File.WriteAllText(sentinel,"Keep this folder.");
            state.Interaction.WorkingFolders.Enqueue(null);
            w.OpenFile(incoming);
            Equal(DocumentKind.Notebook,Document(w).Kind);
            Equal(1,Directory.GetFileSystemEntries(root).Length);
            var vanished=state.Path("Folder-moved-by-another-app");
            state.Interaction.WorkingFolders.Enqueue(vanished);
            w.OpenFile(incoming);
            Equal(DocumentKind.Notebook,Document(w).Kind);
            Assert(!Directory.Exists(vanished) && state.Interaction.Messages.Count==1,"Invalid destination was accepted.");
            state.Interaction.Messages.Clear();
            state.Interaction.WorkingFolders.Enqueue(root);
            w.OpenFile(incoming);
            Equal(0,state.Interaction.UnexpectedCalls.Count);
            Equal(0,state.Interaction.Messages.Count);
            string source = Path.Combine(root,stem+" (2)",original.SourceName);
            string layer = source+".inklane.json";
            Equal(layer,Field<string>(w,"_filePath"));
            Assert(File.ReadAllBytes(source).SequenceEqual(original.SourceBytes),"The extracted source changed.");
            Equal("Keep this folder.",File.ReadAllText(sentinel));
            Equal(DocumentStore.Serialize(original.Document),DocumentStore.Serialize(DocumentStore.LoadJson(layer)));
            int notePage = original.Document.Pages.Count-1;
            var note = original.Document.Pages[notePage].Items.Single();
            var live = Document(w).Pages[notePage].Items.Single();
            Equal(note.Id,live.Id); Equal(note.Title,live.Title); Equal(note.Text,live.Text);
            Equal(note.X,live.X); Equal(note.Y,live.Y);
            if(format=="png")
            {
                w.ShowInTaskbar=false; w.ShowActivated=false; w.Width=1160; w.Height=760; w.Show(); w.UpdateLayout();
                var content=(FrameworkElement)w.Content;
                var target=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);
                target.Render(content);
                var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
                using var picture=File.Create(state.Path("Windows-working-copy.png")); encoder.Save(picture);
            }
            AddText(w,"Reviewed on Windows");
            Assert(Call<bool>(w,"WriteRecovery"),"Working copy did not checkpoint.");
            var recoveryFolder=Path.GetDirectoryName(Field<string>(w,"_recoveryPath"))!;
            using(var restored=new WindowCase("recovered-portable-" + format))
            {
                SetField(restored.Window,"_recoveryPath",Path.Combine(recoveryFolder,"other-session.json"));
                restored.Interaction.Choices.Enqueue(0); restored.Window.OfferRecovery();
                Equal(layer,Field<string>(restored.Window,"_filePath"));
                Equal("Reviewed on Windows",Document(restored.Window).Pages[0].Items.Last().Text);
                Equal(note.Id,Document(restored.Window).Pages[notePage].Items.Single(x=>x.IsNote).Id);
            }
            string beforeLayer=DocumentStore.GetFileHash(layer);
            using(var blocker=File.Open(layer,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Assert(!Call<bool>(w,"Save",false),"Locked destination reported success.");
            Assert(Dirty(w),"Failed save cleared edits.");
            Equal(beforeLayer,DocumentStore.GetFileHash(layer));
            Assert(Call<bool>(w,"Save",false),"Editable working layer did not save.");
            var stored=DocumentStore.LoadJson(layer);
            if(format!="pdf")
            {
                Equal(original.SourceName,stored.SourceImagePath);
                Equal(source,Document(w).SourceImagePath);
            }
            Assert(!File.ReadAllText(layer).Contains(root,StringComparison.OrdinalIgnoreCase),"Neighbor sidecar leaked a directory.");
            w.OpenFile(source);
            Equal("Reviewed on Windows",Document(w).Pages[0].Items.Last().Text);
            // A layer saved outside the source folder keeps its working reference and reopens against the hash.
            if (format=="png")
            {
                var elsewhere=Path.Combine(Directory.CreateDirectory(state.Path("Alternate")).FullName,"Review.inklane.json");
                state.Interaction.SavePaths.Enqueue(elsewhere);
                Assert(Call<bool>(w,"Save",true),"Save As to another folder failed.");
                Equal(source,DocumentStore.LoadJson(elsewhere).SourceImagePath);
                w.OpenFile(elsewhere);
                Equal("Reviewed on Windows",Document(w).Pages[0].Items.Last().Text);
                Equal(source,Document(w).SourceImagePath);
                w.OpenFile(layer);
            }
            var exported=state.Path("Edited.inklane");
            state.Interaction.SavePaths.Enqueue(exported);Call(w,"ExportPortableWorkspace");
            var roundTrip=PortableWorkspace.Read(exported);
            Equal(original.SourceName,roundTrip.SourceName);
            Assert(roundTrip.SourceBytes.SequenceEqual(original.SourceBytes),"Export changed source bytes.");
            Equal(DocumentStore.Serialize(stored),DocumentStore.Serialize(roundTrip.Document));
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(exported)).Contains(root,StringComparison.OrdinalIgnoreCase),"Portable export included a machine path.");
            var bytesBefore=DocumentStore.GetFileHash(exported);
            state.Interaction.SavePaths.Enqueue(exported);Call(w,"ExportPortableWorkspace");
            Equal(bytesBefore,DocumentStore.GetFileHash(exported));
            File.AppendAllText(source,"changed externally");
            string blocked=state.Path("Changed.inklane");
            state.Interaction.SavePaths.Enqueue(blocked);Call(w,"ExportPortableWorkspace");
            Assert(!File.Exists(blocked),"An externally changed source was exported.");
            Equal(incomingHash,DocumentStore.GetFileHash(incoming));
        }
    }

    private static void ReviewOfNotebookPages()
    {
        // A reader may review a fixed notebook PDF as an external source. Its embedded
        // native model and the separate incoming remarks are two different editable layers.
        using var state=new WindowCase("portable-reviewed-notebook");
        var pdf=state.Path("Notebook.pdf");
        var native=Notebook("Original notebook text");
        DocumentStore.SavePdf(native,pdf);
        var originalHash=DocumentStore.GetFileHash(pdf);
        var p=native.Pages[0];
        var layer=new DocumentData { Kind=DocumentKind.Pdf, SchemaVersion=3,
            SourcePdfName="Notebook.pdf", SourcePdfSha256=originalHash,
            Pages=[new PageData { Width=p.Width, Height=p.Height, MarginMm=0,
                Items=[new TextItem {Title="External review",Text="Move this caption.",IsNote=true,X=12,Y=30}] }] };
        var bundle=state.Path("Review.inklane");
        using(var file=File.Create(bundle)) PortableWorkspace.Write(file,layer,"Notebook.pdf",pdf);
        var parent=Directory.CreateDirectory(state.Path("Review")).FullName;
        state.Interaction.WorkingFolders.Enqueue(parent);state.Window.OpenFile(bundle);
        Equal(DocumentKind.Pdf,Document(state.Window).Kind);
        var working=Path.Combine(parent,"Notebook - Inklane","Notebook.pdf");
        state.Window.OpenFile(working);
        Equal(DocumentKind.Pdf,Document(state.Window).Kind);
        Equal("External review",Document(state.Window).Pages[0].Items.Single().Title);
        Equal(originalHash,DocumentStore.GetFileHash(working));
        Equal("Original notebook text",DocumentStore.LoadPdf(working).Pages[0].Items.Single().Text);
        Equal(0,state.Interaction.Messages.Count);
    }

    private static void PortableExportCapacity()
    {
        static byte[] Png(int width, int height)
        {
            int stride=(width+7)/8;
            var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Indexed1,BitmapPalettes.BlackAndWhite,
                new byte[height*stride],stride);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
        }
        using var state=new WindowCase("portable-capacity");
        var doc=Document(state.Window);
        var png=Png(4000,4000);
        doc.Pages[0].Images.Add(new ImageItem {PngData=png, Width=60, Height=60});
        doc.Pages[0].Images.Add(new ImageItem {PngData=png, Width=60, Height=60});
        using (var allowed=new MemoryStream())
        {
            PortableWorkspace.Write(allowed,doc,"Review");
            Assert(allowed.Length>12,"Portable workspace at the exact pixel limit was refused.");
        }
        doc.Pages[0].Images.Add(new ImageItem {PngData=Png(1,1), Width=1, Height=1});
        DocumentStore.Validate(doc);
        var path=state.Path("Oversized.inklane");
        state.Interaction.SavePaths.Enqueue(path);Call(state.Window,"ExportPortableWorkspace");
        Assert(!File.Exists(path),"Oversized portable export produced an unreadable file.");
        Assert(state.Interaction.Messages.Any(m=>m.Text==L.T("PortableCapacity")),"The portable capacity limit was not explained.");
        Equal(3,doc.Pages[0].Images.Count);
    }

    private static void RejectPortableReviews()
    {
        using var state = new WindowCase("portable-rejected");
        var w=state.Window;
        string initial = State(w);
        var bytes=File.ReadAllBytes(ReviewFixture("jpg"));
        int size=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8,4));
        void Reject(string name, Action<JsonNode>? change=null, bool damage=false)
        {
            var metadata=JsonNode.Parse(bytes.AsSpan(12,size))!;
            metadata["sourceName"]=name;
            metadata["document"]!["sourceImagePath"]=name;
            change?.Invoke(metadata);
            var layer=Encoding.UTF8.GetBytes(metadata.ToJsonString());
            var dest=new byte[12+layer.Length+bytes.Length-12-size];
            bytes.AsSpan(0,8).CopyTo(dest);
            BinaryPrimitives.WriteInt32BigEndian(dest.AsSpan(8,4),layer.Length);
            layer.CopyTo(dest,12); bytes.AsSpan(12+size).CopyTo(dest.AsSpan(12+layer.Length));
            if(damage) dest[^1]^=31;
            var path=state.Path("Untrusted.inklane");File.WriteAllBytes(path,dest);
            int errors=state.Interaction.Messages.Count;
            w.OpenFile(path);
            Equal(errors+1,state.Interaction.Messages.Count);
            Equal(initial,State(w));
        }
        foreach(string unsafeName in new[]{@"..\outside.jpg", @"C:\outside.jpg","file.jpg:payload","NUL.jpg","COM¹.jpg","LPT4.foo.jpg",
            @"\\network\share.jpg","bad.jpg ","bad.jpg.",new string('a',250)+".jpg"}) Reject(unsafeName);
        Reject("Study.jpg",damage:true);
        Reject("Study.jpg",m=>m["document"]!["pages"]![0]!["width"]=900);
        Reject("Study.png"); // JPEG bytes must not be written with a PNG name.
        var pdf=File.ReadAllBytes(ReviewFixture("pdf"));
        var len=BinaryPrimitives.ReadInt32BigEndian(pdf.AsSpan(8,4));
        var json=JsonNode.Parse(pdf.AsSpan(12,len))!;
        json["document"]!["pages"]![1]!["height"]=250;
        var modified=Encoding.UTF8.GetBytes(json.ToJsonString());
        var bad=state.Path("Geometry.inklane");
        using(var stream=File.Create(bad))
        {
            stream.Write(pdf,0,8);Span<byte> header=stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header,modified.Length);
            stream.Write(header);stream.Write(modified);stream.Write(pdf.AsSpan(12+len));
        }
        int before=state.Interaction.Messages.Count;
        w.OpenFile(bad);
        Equal(before+1,state.Interaction.Messages.Count);Equal(initial,State(w));
        Equal(0,state.Interaction.UnexpectedCalls.Count);
    }
}
