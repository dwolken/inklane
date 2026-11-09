using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Inklane.Core;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Inklane;

internal static class OfficePdfConverter
{
    public static string? FindExecutable()
    {
        // Never search the document's folder, current directory or PATH for a renderer.
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string programFiles = Environment.GetFolderPath(folder);
            if (programFiles.Length == 0) continue;
            string program = Path.Combine(programFiles, "LibreOffice", "program", "soffice.com");
            if (File.Exists(program)) return program;
        }
        return null;
    }

    public static Task<OfficeReviewLink> ConvertAsync(string executable, string sourcePath, string destination, CancellationToken token = default)
        => Task.Run(() => ConvertLocalAsync(executable, sourcePath, destination, token), token);

    private static async Task<OfficeReviewLink> ConvertLocalAsync(string executable, string sourcePath, string destination, CancellationToken token)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destination = Path.GetFullPath(destination);
        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".docx" or ".pptx")) throw new NotSupportedException(L.T("OfficeConvertUnsupported"));
        if (!Path.GetExtension(destination).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(destination) || File.Exists(destination + ".inklane.json"))
            throw new IOException(L.T("OfficeNewPdfOnly"));
        using var source = File.OpenRead(sourcePath);
        if (source.Length > 64 * 1024 * 1024) throw new InvalidDataException(L.T("OfficeConvertUnsupported"));
        string sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(source,token).ConfigureAwait(false));
        source.Position = 0;
        var work = Directory.CreateTempSubdirectory("InklaneReview-");
        string stage = Path.Combine(Path.GetDirectoryName(destination)!, $".inklane-{Guid.NewGuid():N}.tmp");
        bool createdStage = false;
        try
        {
            string snapshot = Path.Combine(work.FullName, "review" + extension);
            using (var copy = new FileStream(snapshot,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                await source.CopyToAsync(copy,token).ConfigureAwait(false);
            try { OfficeConversionPolicy.Check(snapshot,token); }
            catch (System.Xml.XmlException ex) { throw new InvalidDataException(L.T("OfficeConvertUnsupported"),ex); }
            string profile = Path.Combine(work.FullName,"profile");
            Directory.CreateDirectory(Path.Combine(profile,"user"));
            await File.WriteAllTextAsync(Path.Combine(profile,"user","registrymodifications.xcu"),ProfileSettings,token).ConfigureAwait(false);

            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work.FullName };
            foreach (string arg in new[] { "-env:UserInstallation=" + new Uri(profile).AbsoluteUri, "--headless", "--nologo", "--norestore", "--nodefault",
                "--convert-to", extension == ".docx" ? "pdf:writer_pdf_Export" : "pdf:impress_pdf_Export", "--outdir", work.FullName, snapshot })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException(L.T("OfficeConvertFailed"));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(75));
            try
            {
                var exited = process.WaitForExitAsync(deadline.Token);
                while (!exited.IsCompleted)
                {
                    await Task.WhenAny(exited, Task.Delay(250,deadline.Token)).ConfigureAwait(false);
                    if (work.EnumerateFiles().Any(f => f.Length > DocumentStore.MaxPdfBytes))
                        throw new InvalidDataException(L.T("OfficeConvertFailed"));
                }
                await exited.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidDataException)
            {
                // Only terminate the worker launched with this fresh profile.
                try { if (!process.HasExited) process.Kill(entireProcessTree:true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (ex is not OperationCanceledException) throw;
                throw new IOException(L.T("OfficeConvertTimeout"));
            }
            var pdf = new FileInfo(Path.Combine(work.FullName,"review.pdf"));
            if (process.ExitCode != 0 || !pdf.Exists || pdf.Length is 0 or > DocumentStore.MaxPdfBytes)
                throw new InvalidDataException(L.T("OfficeConvertFailed"));
            await VerifyOutput(pdf.FullName).ConfigureAwait(false);
            using var result = File.OpenRead(pdf.FullName);
            string pdfHash = Convert.ToHexString(await SHA256.HashDataAsync(result,token).ConfigureAwait(false));
            result.Position = 0;
            using (var output = new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                createdStage = true;
                await result.CopyToAsync(output,token).ConfigureAwait(false);
                output.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            if (File.Exists(destination + ".inklane.json")) throw new IOException(L.T("OfficeNewPdfOnly"));
            File.Move(stage,destination,overwrite:false);
            return new OfficeReviewLink(sourcePath,sourceHash,destination,pdfHash);
        }
        finally
        {
            try { if (createdStage && File.Exists(stage)) File.Delete(stage); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { work.Delete(recursive:true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    internal static async Task VerifyOutput(string path)
    {
        using (var source = File.OpenRead(path))
            if (DocumentStore.ReadNotebook(source) != null) throw new InvalidDataException(L.T("OfficeConvertFailed"));
        using var file = File.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        var document = await PdfDocument.LoadFromStreamAsync(stream);
        if (document.IsPasswordProtected || document.PageCount is 0 or > 1000) throw new InvalidDataException(L.T("OfficeConvertFailed"));
        using var first = document.GetPage(0);
        using var preview = new InMemoryRandomAccessStream();
        await first.RenderToStreamAsync(preview,new PdfPageRenderOptions { DestinationWidth = 96, DestinationHeight = 96 });
        if (preview.Size == 0) throw new InvalidDataException(L.T("OfficeConvertFailed"));
    }

    private const string ProfileSettings = """
        <?xml version="1.0" encoding="UTF-8"?>
        <oor:items xmlns:oor="http://openoffice.org/2001/registry">
          <item oor:path="/org.openoffice.Office.Jobs/Jobs/org.openoffice.Office.Jobs:Job['UpdateCheck']/Arguments"><prop oor:name="AutoCheckEnabled" oor:op="fuse"><value>false</value></prop></item>
          <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="DisableMacrosExecution" oor:op="fuse"><value>true</value></prop></item>
          <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop></item>
          <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="BlockUntrustedRefererLinks" oor:op="fuse"><value>true</value></prop></item>
        </oor:items>
        """;
}
