import Foundation
import UniformTypeIdentifiers
import WebKit
#if os(iOS)
import UIKit
#else
import AppKit
#endif

private struct Transfer {
    let id: String
    let file: URL
    let expected: Int
    var count = 0
    let writer: FileHandle
}

@MainActor
final class FileExport: NSObject, WKScriptMessageHandlerWithReply {
    weak var webView: WKWebView?
    var trustedURL: URL?
    private var transfer: Transfer?
    var pendingFile: URL? { transfer?.file }
    private var exporting = false
    private var pickerAnswer: ((Bool) -> Void)?
    #if os(iOS)
    private(set) weak var picker: UIDocumentPickerViewController?
    #else
    private(set) weak var picker: NSSavePanel?
    #endif

    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage,
                               replyHandler reply: @escaping (Any?, String?) -> Void) {
        guard message.frameInfo.isMainFrame, let origin = trustedURL,
              let request = message.frameInfo.request.url,
              request.scheme == origin.scheme, request.host == origin.host, request.port == origin.port,
              request.path == "/editor/" || request.path == "/editor/index.html",
              let body = message.body as? [String: Any], let op = body["op"] as? String else {
            reply(nil, "The workspace is unavailable.")
            return
        }
        do {
            if op == "begin" {
                guard !exporting, transfer == nil, let name = body["filename"] as? String,
                      let size = body["size"] as? Int, (0...512_000_000).contains(size) else { throw CocoaError(.fileWriteUnknown) }
                let cleaned = name.components(separatedBy: CharacterSet(charactersIn: "/\\:").union(.controlCharacters)).joined(separator: "-")
                guard !cleaned.isEmpty, cleaned != ".", cleaned != "..", cleaned.utf8.count <= 250 else { throw CocoaError(.fileWriteInvalidFileName) }
                let id = UUID().uuidString
                let folder = FileManager.default.temporaryDirectory.appendingPathComponent("Inklane-export-" + id, isDirectory: true)
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
                let temp = folder.appendingPathComponent(cleaned)
                do {
                    try Data().write(to: temp, options: .withoutOverwriting)
                    transfer = Transfer(id: id, file: temp, expected: size, writer: try FileHandle(forWritingTo: temp))
                } catch { try? FileManager.default.removeItem(at: folder); throw error }
                reply(id,nil)
                return
            }
            guard let active = transfer, active.id == body["token"] as? String, !exporting else { throw CocoaError(.fileWriteUnknown) }
            switch op {
            case "append":
                guard let text = body["bytes"] as? String, text.utf8.count <= 262144,
                      let data = Data(base64Encoded: text), active.count + data.count <= active.expected else {
                    throw CocoaError(.fileWriteInapplicableStringEncoding)
                }
                try active.writer.write(contentsOf: data)
                transfer?.count += data.count
                reply(true,nil)
            case "export":
                guard active.count == active.expected else { throw CocoaError(.fileWriteUnknown) }
                try active.writer.close()
                exporting = true
                try present(active.file) { [weak self] saved, error in
                    self?.clearTransfer()
                    reply(error == nil ? (saved ? "saved" : "cancelled") : nil, error?.localizedDescription)
                }
            case "discard":
                clearTransfer()
                reply(true,nil)
            default: throw CocoaError(.fileWriteUnknown)
            }
        } catch {
            if body["token"] as? String == transfer?.id && !exporting { clearTransfer() }
            reply(nil,error.localizedDescription)
        }
    }

    private func clearTransfer() {
        if let active = transfer {
            try? active.writer.close()
            try? FileManager.default.removeItem(at: active.file.deletingLastPathComponent())
        }
        transfer = nil
        exporting = false
        picker = nil
    }

    private func present(_ file: URL, completion: @escaping (Bool, Error?) -> Void) throws {
        #if os(iOS)
        guard let controller = webView?.window?.rootViewController else { clearTransfer(); throw CocoaError(.fileWriteUnknown) }
        let prompt = UIDocumentPickerViewController(forExporting: [file], asCopy: true)
        prompt.delegate = self
        pickerAnswer = { completion($0, nil) }
        picker = prompt
        topController(controller).present(prompt, animated: true)
        #else
        guard let window = webView?.window else { clearTransfer(); throw CocoaError(.fileWriteUnknown) }
        let panel = NSSavePanel()
        panel.nameFieldStringValue = file.lastPathComponent
        panel.canCreateDirectories = true
        if let type = UTType(filenameExtension: file.pathExtension) { panel.allowedContentTypes = [type] }
        picker = panel
        panel.beginSheetModal(for: window) { result in
            guard result == .OK, let destination = panel.url else { completion(false,nil); return }
            do {
                try Data(contentsOf: file, options: .mappedIfSafe).write(to: destination, options: .atomic)
                completion(true,nil)
            } catch { completion(false,error) }
        }
        #endif
    }
}

#if os(iOS)
@MainActor func topController(_ root: UIViewController) -> UIViewController {
    var active = root
    while let presented = active.presentedViewController { active = presented }
    return active
}
extension FileExport: UIDocumentPickerDelegate {
    func documentPickerWasCancelled(_ controller: UIDocumentPickerViewController) {
        let answer = pickerAnswer
        pickerAnswer = nil
        answer?(false)
    }
    func documentPicker(_ controller: UIDocumentPickerViewController, didPickDocumentsAt urls: [URL]) {
        let answer = pickerAnswer
        pickerAnswer = nil
        answer?(!urls.isEmpty)
    }
}
#endif
