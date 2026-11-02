using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using Inklane.Core;
using Microsoft.Win32;

namespace Inklane;

// Local recent-file information, never part of an exported document.
public sealed record OfficeReviewLink(string OfficePath, string OfficeHash, string PdfPath, string PdfHash);

public sealed partial class MainWindow
{
    private OfficeReviewLink? _activeOfficeReview;
    private string? _officeRenderer = OfficePdfConverter.FindExecutable();
    private static bool IsOfficeFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".pptx";

    private OfficeReviewLink? PickOfficeReview(string officePath)
    {
        var source = new FileInfo(officePath);
        if (!source.Exists) throw new FileNotFoundException(null, officePath);
        if (source.Length > 256L * 1024 * 1024) throw new InvalidDataException(L.T("OfficeSizeLimit"));
        // Read only the ZIP directory. Never execute document code or resolve Office links.
        using (var package = ZipFile.OpenRead(officePath))
        {
            var mainPart = source.Extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
                ? "word/document.xml" : "ppt/presentation.xml";
            if (package.GetEntry("[Content_Types].xml") == null || package.GetEntry(mainPart) == null)
                throw new InvalidDataException(L.T("OfficeInvalid"));
        }
        var sourceHash = DocumentStore.GetFileHash(officePath);
        var previous = Settings.Current.OfficeReviews.FirstOrDefault(link => SamePath(link.OfficePath, officePath));
        var sourceChanged = previous != null && previous.OfficeHash != sourceHash;
        if (previous != null && !sourceChanged && File.Exists(previous.PdfPath)
            && new FileInfo(previous.PdfPath).Length <= 128L * 1024 * 1024
            && DocumentStore.GetFileHash(previous.PdfPath) == previous.PdfHash) return previous;

        string message = previous == null ? L.F("OfficeReviewIntro", source.Name) : L.T("OfficeReviewChanged");
        if (_officeRenderer != null)
        {
            int action = _interaction.Choose(this,L.T("OfficeReview"), previous == null ? L.F("OfficeReviewCreateIntro",source.Name) : message,
                L.T("CreateReviewPdf"),L.T("ChooseReviewPdf"),L.T("Cancel"));
            if (action == 0)
            {
                try { return CreateOfficeCopy(_officeRenderer,source); }
                catch (OperationCanceledException) { return null; }
                catch (Exception ex) when (IsExpected(ex) || ex is System.Xml.XmlException or System.ComponentModel.Win32Exception)
                {
                    var reason = L.IsOpenProblem(ex.Message) ? ex.Message : L.T("OfficeConvertFailed");
                    if (_interaction.Choose(this,L.T("OfficeReview"),reason,L.T("ChooseReviewPdf"),L.T("Cancel")) != 0) return null;
                }
            }
            else if (action != 1) return null;
        }
        else if (_interaction.Choose(this,L.T("OfficeReview"),message,L.T("ChooseReviewPdf"),L.T("Cancel")) != 0) return null;
        var picker = new OpenFileDialog { Title = L.T("ChooseReviewPdf"), Filter = L.T("SourcePdfFilter"),
            InitialDirectory = source.DirectoryName, FileName = Path.ChangeExtension(source.Name, ".pdf"),
            CheckFileExists = true, Multiselect = false };
        if (_interaction.ChooseSourcePdf(this, picker) is not { } pdf) return null;
        pdf = Path.GetFullPath(pdf);
        if (!Path.GetExtension(pdf).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || new FileInfo(pdf).Length > 128L * 1024 * 1024) throw new InvalidDataException(L.T("OfficeNeedsPdf"));
        var pdfHash = DocumentStore.GetFileHash(pdf);
        if (sourceChanged && previous!.PdfHash == pdfHash) throw new InvalidDataException(L.T("OfficeOldCopy"));
        return new(officePath, sourceHash, pdf, pdfHash);
    }

    private OfficeReviewLink? CreateOfficeCopy(string renderer, FileInfo source)
    {
        var picker = new SaveFileDialog { Title = L.T("CreateReviewPdf"), Filter = L.T("SourcePdfFilter"),
            FileName = Path.GetFileNameWithoutExtension(source.Name) + L.T("ReviewSuffix") + ".pdf", InitialDirectory = source.DirectoryName,
            AddExtension = true, DefaultExt = ".pdf", OverwritePrompt = false };
        var destination = _interaction.ChooseSavePath(this,picker);
        if (destination == null) return null;
        using var cancellation = new CancellationTokenSource();
        var convert = OfficePdfConverter.ConvertAsync(renderer,source.FullName,destination,cancellation.Token);
        if (IsVisible)
        {
            var progress = SaveProgress.CreateWindow(this,source.FullName,L.T("RenderingReview"));
            var cancel = new Button { Content = L.T("Cancel"), HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0,14,0,0) };
            ((StackPanel)progress.Content).Children.Add(cancel);
            void RequestCancel() { cancellation.Cancel(); cancel.IsEnabled = false; }
            cancel.Click += (_,_) => RequestCancel();
            progress.Closing += (_,e) => { if (!convert.IsCompleted) { e.Cancel = true; RequestCancel(); } };
            progress.Loaded += async (_,_) =>
            {
                try { await convert; }
                catch { /* GetResult below propagates conversion and cancellation failures to the caller. */ }
                finally { progress.Close(); }
            };
            progress.ShowDialog();
        }
        return convert.GetAwaiter().GetResult();
    }

    private static void RememberOfficeReview(OfficeReviewLink link)
    {
        var links = Settings.Current.OfficeReviews;
        links.RemoveAll(item => SamePath(item.OfficePath, link.OfficePath));
        links.Insert(0, link);
        if (links.Count > 12) links.RemoveRange(12, links.Count - 12);
    }
}
