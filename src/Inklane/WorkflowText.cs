namespace Inklane;

internal static class WorkflowText
{
    public static string T(string key) => Strings.TryGetValue(key, out var value) ? L.Language == "de" ? value.De : value.En : key;
    private static readonly Dictionary<string, (string En, string De)> Strings = new()
    {
        ["OfficeRendererMissing"] = ("Install LibreOffice to open Word and PowerPoint files directly on this computer, then restart Inklane. Your files stay local. You can also use a PDF exported from the original application.", "Installieren Sie LibreOffice, um Word- und PowerPoint-Dateien direkt auf diesem Computer zu öffnen. Starten Sie Inklane danach neu. Ihre Dateien bleiben lokal. Sie können auch einen PDF-Export aus der ursprünglichen Anwendung verwenden."),
        ["GetRenderer"] = ("Get LibreOffice…", "LibreOffice herunterladen …"),
        ["ExportPdf"] = ("Export annotated PDF…", "PDF mit Anmerkungen exportieren …"),
        ["ExportOffice"] = ("Export Office copy with feedback…", "Office-Kopie mit Feedback exportieren …"),
        ["OfficeExportInfo"] = ("The original document stays editable. A review appendix adds page images with your marks and complete explanations: text in Word, comments on the added slides in PowerPoint. Review images are flattened. Keep your Inklane file to edit the marks.", "Das Originaldokument bleibt bearbeitbar. Ein Feedback-Anhang ergänzt Seitenbilder mit Ihren Markierungen und vollständige Erklärungen: als Text in Word, als Kommentare auf den zusätzlichen Folien in PowerPoint. Die Anhangsbilder sind nicht einzeln bearbeitbar. Behalten Sie Ihre Inklane-Datei, um die Markierungen zu bearbeiten."),
        ["PdfExportInfo"] = ("This PDF preserves the source pages and adds standard annotations. Notes keep their full text. Keep the Inklane workspace to continue editing with all Inklane features.", "Dieses PDF erhält die Originalseiten und ergänzt Standard-Anmerkungen. Notizen behalten ihren vollständigen Text. Behalten Sie die Inklane-Arbeitsmappe, um alle Inklane-Funktionen weiter zu bearbeiten."),
        ["SourceChanged"] = ("The original document has changed. Open its new revision before exporting feedback to an Office copy.", "Das Originaldokument wurde geändert. Öffnen Sie die neue Version, bevor Sie Feedback in eine Office-Kopie exportieren."),
    };
}
