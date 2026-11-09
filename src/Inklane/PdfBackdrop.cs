using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;
using Inklane.Core;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Inklane;

// Keep a bounded snapshot of the original so replacing the file on disk can never
// change a page underneath the user's handwriting. Only two rendered pages are cached.
internal sealed class PdfBackdrop : IDisposable
{
    private const int WrongPassword = unchecked((int)0x8007052B); // HRESULT_FROM_WIN32(ERROR_WRONG_PASSWORD)
    private readonly IRandomAccessStream _stream;
    private readonly PdfDocument _pdf;
    private readonly Queue<(int Index, BitmapSource Image)> _cache = new();
    public string Path { get; private set; }
    private readonly Size[] _sizes;
    private readonly string _hash;

    private PdfBackdrop(string path, string hash, IRandomAccessStream stream, PdfDocument pdf)
    {
        Path = path; _hash = hash; _stream = stream; _pdf = pdf;
        if (pdf.IsPasswordProtected) throw new NotSupportedException(L.T("PdfEncrypted"));
        if (pdf.PageCount == 0) throw new InvalidDataException(L.T("PdfUnreadable"));
        if (pdf.PageCount > 1000) throw new NotSupportedException(L.T("PdfTooManyPages"));
        _sizes = new Size[pdf.PageCount];
        for (int i = 0; i < _sizes.Length; i++)
        {
            using var page = pdf.GetPage((uint)i);
            var size = page.Size; // Windows resolves the displayed crop box and page rotation.
            if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) ||
                size.Width is < 1 or > 100000 || size.Height is < 1 or > 100000)
                throw new InvalidDataException(L.T("PdfGeometryUnsupported"));
            _sizes[i] = new Size(size.Width,size.Height);
        }
    }

    public static (DocumentData Model, PdfBackdrop? Backdrop, string Hash) Open(Window owner, string path, bool asSource = false, byte[]? snapshot = null)
    {
        return ReadWithProgress(owner,path,async () =>
        {
            byte[] bytes;
            if (snapshot == null)
            {
                using var file = File.OpenRead(path);
                if (file.Length > DocumentStore.MaxPdfBytes) throw new InvalidDataException(L.T("PdfTooLarge"));
                bytes = new byte[(int)file.Length];
                file.ReadExactly(bytes);
            }
            else bytes = snapshot;
            string hash = Convert.ToHexString(SHA256.HashData(bytes));
            using var check = new MemoryStream(bytes,false);
            var notebook = asSource ? null : ReadNotebook(check);
            if (notebook != null) return (notebook, (PdfBackdrop?)null, hash);

            var stream = new MemoryStream(bytes,false).AsRandomAccessStream();
            try
            {
                PdfDocument pdf;
                try { pdf = await PdfDocument.LoadFromStreamAsync(stream); }
                catch (Exception ex) when (ex.HResult == WrongPassword)
                { throw new NotSupportedException(L.T("PdfEncrypted"),ex); }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
                { throw new InvalidDataException(L.T("PdfUnreadable"),ex); }
                var source = new PdfBackdrop(path,hash,stream,pdf);
                return (new DocumentData
                {
                    Kind = DocumentKind.Pdf, SourcePdfName = System.IO.Path.GetFileName(path), SourcePdfSha256 = hash,
                    Pages = source._sizes.Select(s => new PageData { Width = s.Width, Height = s.Height, MarginMm = 0 }).ToList()
                }, source, hash);
            }
            catch { stream.Dispose(); throw; }
        });
    }

    private static DocumentData? ReadNotebook(Stream input)
    {
        try { return DocumentStore.ReadNotebook(input); }
        catch (PdfReadException ex)
        {
            throw new NotSupportedException(L.T(ex.Problem switch
            {
                PdfReadProblem.Encrypted => "PdfEncrypted",
                PdfReadProblem.NotebookChanged => "NotebookPdfChanged",
                _ => "NotebookPdfUnsupported"
            }),ex);
        }
        catch (NotSupportedException ex) { throw new NotSupportedException(L.T("PdfUnsupportedFeature"),ex); }
        catch (InvalidDataException ex) { throw new InvalidDataException(L.T("PdfUnreadable"),ex); }
    }

    public void AttachWorkingCopy(string path)
    {
        if (System.IO.Path.IsPathFullyQualified(Path) || !System.IO.Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("A PDF source already has a location.");
        Path = path;
    }

    public void Verify(DocumentData model)
    {
        if (model.Kind != DocumentKind.Pdf || !string.Equals(_hash,model.SourcePdfSha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("PdfChanged"));
        if (_sizes.Length != model.Pages.Count || model.Pages.Where((p,i) =>
            Math.Abs(p.Width - _sizes[i].Width) > .01 || Math.Abs(p.Height - _sizes[i].Height) > .01).Any())
            throw new InvalidDataException(L.T("PdfGeometry"));
    }

    public BitmapSource Render(Window owner, int index)
    {
        foreach (var entry in _cache) if (entry.Index == index) return entry.Image;
        var image = ReadWithProgress(owner, Path,async () =>
        {
            using var page = _pdf.GetPage((uint)index);
            var size = _sizes[index];
            var scale = Math.Min(2, Math.Min(4096/Math.Max(size.Width,size.Height), Math.Sqrt(8_000_000/(size.Width*size.Height))));
            using var output = new InMemoryRandomAccessStream();
            try
            {
                await page.RenderToStreamAsync(output,new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1,Math.Round(size.Width*scale)),
                    DestinationHeight = (uint)Math.Max(1,Math.Round(size.Height*scale)),
                    BackgroundColor = Windows.UI.Color.FromArgb(255,255,255,255)
                });
            }
            catch (System.Runtime.InteropServices.COMException ex)
            { throw new InvalidDataException(L.T("PdfRenderFailed"),ex); }
            output.Seek(0);
            using var input = output.AsStreamForRead();
            var bitmap = BitmapFrame.Create(input,BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad);
            bitmap.Freeze();
            return bitmap;
        });
        if (_cache.Count == 2) _cache.Dequeue();
        _cache.Enqueue((index,image));
        return image;
    }

    private static T ReadWithProgress<T>(Window owner, string path, Func<Task<T>> read)
    {
        var task = Task.Run(read);
        // Most page changes finish immediately. Slow decodes get a modal, pumping
        // UI without allowing a second open or edit against the candidate document.
        if (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(160) && owner.IsVisible)
        {
            var dialog = SaveProgress.CreateWindow(owner,path,L.T("ReadingPdf"));
            dialog.Closing += (_, e) => e.Cancel = !task.IsCompleted;
            dialog.Loaded += async (_, _) =>
            {
                try { await task; }
                catch { /* Propagate the original exception below, outside the dispatcher callback. */ }
                finally { if (dialog.IsVisible) dialog.Close(); }
            };
            dialog.ShowDialog();
        }
        return task.GetAwaiter().GetResult();
    }

    public void Dispose() { _cache.Clear(); _stream.Dispose(); }
}
