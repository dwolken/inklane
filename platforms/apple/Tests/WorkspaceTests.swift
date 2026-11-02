import XCTest
import WebKit
@testable import Inklane
#if os(iOS)
import UIKit
#else
import AppKit
#endif

@MainActor
final class WorkspaceTests: XCTestCase {
    private func wait(_ host: WorkspaceHost, _ script: String,
                      file: StaticString = #filePath, line: UInt = #line) async throws {
        for _ in 0..<180 {
            if let value = try? await host.webView.evaluateJavaScript(script), value as? Bool == true { return }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        XCTFail("Condition did not become true: \(script); URL \(host.webView.url?.path ?? "none"); host failed: \(host.failed)", file: file, line: line)
        throw CocoaError(.fileReadUnknown)
    }
    private func perform(_ host: WorkspaceHost, _ script: String) async throws {
        _ = try await host.webView.evaluateJavaScript(script + "; true")
    }

    func testEditExportCancelAndReopenBundledWorkspace() async throws {
        let suite = "inklane-test-" + UUID().uuidString
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let host = WorkspaceHost(defaults: defaults)
        #if os(iOS)
        let scene = try XCTUnwrap(UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }.first)
        let oldWindow = scene.windows.first { $0.isKeyWindow }
        let window = UIWindow(windowScene: scene)
        let controller = UIViewController()
        window.rootViewController = controller
        window.makeKeyAndVisible()
        host.webView.frame = controller.view.bounds
        host.webView.autoresizingMask = [.flexibleHeight,.flexibleWidth]
        controller.view.addSubview(host.webView)
        defer { window.isHidden = true; oldWindow?.makeKeyAndVisible() }
        #else
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1050, height: 730),
                              styleMask: [.titled,.closable], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.contentView = host.webView
        window.makeKeyAndOrderFront(nil)
        defer { window.close() }
        #endif

        host.start()
        try await wait(host, "typeof document.querySelector('#sample-link')?.onclick === 'function'")
        try await wait(host, "window.isSecureContext && !!crypto.subtle && typeof window.inklaneSaveCopy === 'function'")
        // Exercise both the new-document dialog and real multipage navigation.
        try await perform(host, "document.querySelector('#new-notebook').click(); document.querySelector('#paper-size').value='A5'; document.querySelector('#paper-form').requestSubmit()")
        try await wait(host, "!document.querySelector('#pages').hidden && !document.querySelector('#save').disabled")
        try await perform(host, "document.querySelector('#add-page').click()")
        try await wait(host, "document.querySelector('#page-count').textContent.includes('2')")
        try await perform(host, "document.querySelector('#home').click()")
        try await wait(host, "!document.querySelector('#welcome').hidden && !!document.querySelector('#draft-list button')")

        try await perform(host, "document.querySelector('#sample-link').click()")
        try await wait(host, "document.querySelectorAll('.marker').length === 2")
        // Use the public notes index to open the same edit card at either screen width.
        try await perform(host, "document.querySelector('#index').click()")
        try await wait(host, "!!document.querySelector('#results button')")
        try await perform(host, "document.querySelector('#results button').click()")
        try await wait(host, "!document.querySelector('#card').hidden")
        try await perform(host, """
            for(const [id,value] of [['#note-title','Recheck scale'],['#note-body','Keep the scale bar at this position.']]) {
              const field=document.querySelector(id); field.focus();field.value=value;
              field.dispatchEvent(new Event('input',{bubbles:true}));
            }
            document.querySelector('#close-card').click()
            """)
        try await wait(host, "!!document.querySelector('.marker[aria-label=\"Recheck scale\"]')")
        try await perform(host, "document.querySelector('#portable').click()")
        for _ in 0..<120 {
            if host.exporter.picker != nil { break }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        let stagedURL = try XCTUnwrap(host.exporter.pendingFile)
        let portable = try Data(contentsOf: stagedURL)
        XCTAssertGreaterThan(portable.count, 100_000)
        let picker = try XCTUnwrap(host.exporter.picker)
        // The OS owns the actual Files/Save panel. Cancellation must preserve the edited document.
        #if os(iOS)
        host.exporter.documentPickerWasCancelled(picker)
        picker.dismiss(animated: false)
        #else
        picker.cancel(nil)
        #endif
        try await wait(host, "!document.querySelector('#save').disabled")
        XCTAssertNil(host.exporter.pendingFile)
        XCTAssertFalse(FileManager.default.fileExists(atPath: stagedURL.path))

        // Reload the real WKWebView and restore the on-device IndexedDB draft.
        try await perform(host, "document.querySelector('#home').click()")
        try await wait(host, "!document.querySelector('#welcome').hidden && document.querySelectorAll('#draft-list button').length >= 4")
        host.webView.reload()
        try await wait(host, "!!document.querySelector('#draft-list button')")
        try await perform(host, "document.querySelector('#draft-list button').click()")
        try await wait(host, "!!document.querySelector('.marker[aria-label=\"Recheck scale\"]')")
        try await perform(host, "document.querySelector('#home').click()")
        try await wait(host, "!document.querySelector('#welcome').hidden")

        // Feed the exact staged portable bytes through the normal file input and check both attached marks.
        let input = """
        const bytes = Uint8Array.from(atob(payload), x => x.charCodeAt(0));
        const transfer = new DataTransfer(); transfer.items.add(new File([bytes], 'Review.inklane'));
        const input = document.querySelector('#files'); input.files = transfer.files;
        input.dispatchEvent(new Event('change',{bubbles:true})); return true;
        """
        _ = try await host.webView.callAsyncJavaScript(input, arguments: ["payload": portable.base64EncodedString()],
                                                      in: nil, contentWorld: .page)
        try await wait(host, "document.querySelectorAll('.marker').length === 2 && !!document.querySelector('.marker[aria-label=\"Recheck scale\"]')")
        try await perform(host, "document.querySelector('#index').click(); document.querySelector('#results button').click()")
        try await wait(host, "document.querySelector('#note-body').value === 'Keep the scale bar at this position.'")
    }
}
