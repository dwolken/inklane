import SwiftUI
import WebKit
#if os(iOS)
import UIKit
#else
import AppKit
#endif

@main
struct InklaneApp: App {
    var body: some Scene {
        #if os(macOS)
        Window("Inklane", id: "main") { WorkspaceView().frame(minWidth: 600, minHeight: 420) }
            .defaultSize(width: 1100, height: 770)
        #else
        WindowGroup { WorkspaceView() }
        #endif
    }
}

struct WorkspaceView: View {
    @StateObject private var host: WorkspaceHost
    init(host: WorkspaceHost? = nil) {
        _host = StateObject(wrappedValue: host ?? WorkspaceHost())
    }
    var body: some View {
        ZStack {
            BrowserView(webView: host.webView)
            if host.failed {
                VStack(spacing: 12) {
                    Text("The local workspace could not open.")
                    Button("Try again") { host.start() }
                }.padding()
            }
        }.onAppear { host.start() }
    }
}

#if os(iOS)
private struct BrowserView: UIViewRepresentable {
    let webView: WKWebView
    func makeUIView(context: Context) -> WKWebView { webView }
    func updateUIView(_ view: WKWebView, context: Context) {}
}
#else
private struct BrowserView: NSViewRepresentable {
    let webView: WKWebView
    func makeNSView(context: Context) -> WKWebView { webView }
    func updateNSView(_ view: WKWebView, context: Context) {}
}
#endif

@MainActor
final class WorkspaceHost: NSObject, ObservableObject, WKNavigationDelegate, WKUIDelegate {
    let webView: WKWebView
    let exporter: FileExport
    private let root: URL?
    private let defaults: UserDefaults
    private var server: WorkspaceServer?
    private(set) var origin: URL?
    @Published var failed = false

    init(bundle: Bundle = .main, defaults: UserDefaults = .standard) {
        self.root = bundle.url(forResource: "Workspace", withExtension: nil)
        self.defaults = defaults
        let exporter = FileExport()
        self.exporter = exporter
        let config = WKWebViewConfiguration()
        config.websiteDataStore = .default()
        config.preferences.javaScriptCanOpenWindowsAutomatically = false
        let controller = WKUserContentController()
        controller.addScriptMessageHandler(exporter, contentWorld: .page, name: "exportFile")
        if let script = bundle.url(forResource: "NativeSave", withExtension: "js"),
           let source = try? String(contentsOf: script, encoding: .utf8) {
            controller.addUserScript(WKUserScript(source: source, injectionTime: .atDocumentStart, forMainFrameOnly: true))
        }
        config.userContentController = controller
        webView = WKWebView(frame: .zero, configuration: config)
        super.init()
        exporter.webView = webView
        webView.navigationDelegate = self
        webView.uiDelegate = self
        #if os(iOS)
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        webView.allowsLinkPreview = false
        #endif
    }

    func start() {
        guard server == nil || failed else { return }
        failed = false
        server?.stop()
        guard let root, FileManager.default.fileExists(atPath: root.appendingPathComponent("editor/index.html").path) else {
            failed = true
            return
        }
        let service = WorkspaceServer(root: root, defaults: defaults)
        server = service
        do {
            try service.start { [weak self, weak service] result in
                guard let self, let service, self.server === service else { return }
                switch result {
                case .success(let address):
                    self.origin = address
                    self.exporter.trustedURL = address
                    self.webView.load(URLRequest(url: address))
                case .failure: self.failed = true
                }
            }
        } catch { failed = true }
    }

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction,
                 decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let origin, let url = action.request.url, action.targetFrame?.isMainFrame == true,
              url.scheme == origin.scheme, url.host == origin.host, url.port == origin.port,
              url.path == "/editor/index.html" || url.path == "/editor/" || url.path == "/editor" else {
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        // Reopening the library makes the last persisted draft available.
        exporter.discardUnpresentedTransfer()
        if let origin { webView.load(URLRequest(url: origin)) }
    }
    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        exporter.discardUnpresentedTransfer()
    }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        if (error as NSError).code != NSURLErrorCancelled { failed = true }
    }

    #if os(macOS)
    func webView(_ webView: WKWebView, runOpenPanelWith parameters: WKOpenPanelParameters,
                 initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping ([URL]?) -> Void) {
        guard let window = webView.window, frame.isMainFrame else { completionHandler(nil); return }
        let panel = NSOpenPanel()
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = parameters.allowsMultipleSelection
        panel.beginSheetModal(for: window) { result in completionHandler(result == .OK ? panel.urls : nil) }
    }
    #endif

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping () -> Void) {
        ask(message, canCancel: false) { _ in completionHandler() }
    }
    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String,
                 initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (Bool) -> Void) {
        ask(message, canCancel: true, completionHandler)
    }
    private func ask(_ message: String, canCancel: Bool, _ completion: @escaping (Bool) -> Void) {
        let cancel = NSLocalizedString("Cancel", comment: "Dismiss an editing confirmation")
        #if os(iOS)
        guard let root = webView.window?.rootViewController else { completion(false); return }
        let alert = UIAlertController(title: "Inklane", message: message, preferredStyle: .alert)
        if canCancel { alert.addAction(UIAlertAction(title: cancel, style: .cancel) { _ in completion(false) }) }
        alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in completion(true) })
        topController(root).present(alert, animated: true)
        #else
        guard let window = webView.window else { completion(false); return }
        let alert = NSAlert()
        alert.messageText = "Inklane"
        alert.informativeText = message
        alert.addButton(withTitle: "OK")
        if canCancel { alert.addButton(withTitle: cancel) }
        alert.beginSheetModal(for: window) { completion($0 == .alertFirstButtonReturn) }
        #endif
    }
}
