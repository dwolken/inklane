using System.IO;
using Inklane.Core;
using Microsoft.Win32;

namespace Inklane;

public sealed partial class MainWindow
{
    private void ExportFeedback()
    {
        FinishRenameNotebook(true);
        _surface.CommitEdits();
        try
        {
            var notebook = _document.Kind == DocumentKind.Notebook;
            if (!CaptureHistory()) return;
            if (notebook && (IsDirty || _filePath == null) &&
                (_interaction.Choose(this,L.T("ExportFeedback"),L.T("SaveBeforeFeedback"),L.T("Save"),L.T("Cancel")) != 0 || !Save()))
                return;
            var source = notebook ? _filePath : SourcePath;
            if (source == null) return;
            const string suffix = ".feedback.json";
            var picker = new SaveFileDialog { Title = L.T("ExportFeedback"), Filter = L.T("FeedbackFilter"),
                DefaultExt = suffix, AddExtension = true, FileName = Path.GetFileName(source) + suffix, OverwritePrompt = true };
            if (_interaction.ChooseSavePath(this,picker) is not { } path || !DestinationAllowed(path,".json",true)) return;
            if (!path.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)) { Error(L.F("WrongExtension",suffix)); return; }
            var hash = notebook ? _diskHash : _document.Kind == DocumentKind.Pdf ? _document.SourcePdfSha256 : _document.SourceImageSha256;
            if (hash == null || !File.Exists(source) || !string.Equals(hash,DocumentStore.GetFileHash(source),StringComparison.OrdinalIgnoreCase))
            { Error(L.T(notebook ? "FeedbackNotebookChanged" : _document.Kind == DocumentKind.Pdf ? "PdfChanged" : "ImageChanged")); return; }
            // Keep a prior report read-locked while rendering. When a new name was chosen,
            // commit with a non-overwriting move in case another application created it meanwhile.
            using var prior = File.Exists(path)
                ? new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read | FileShare.Delete) : null;
            if (prior != null && !FeedbackExport.IsMatchingReport(prior,hash)) { Error(L.T("FeedbackCollision")); return; }
            AtomicWrite(path, stream => FeedbackExport.Write(stream,_document,source,hash), allowReplace:prior != null);
        }
        catch (Exception ex) when (IsExpected(ex)) { Error(L.T("SaveFailed"),ex); }
    }
}
