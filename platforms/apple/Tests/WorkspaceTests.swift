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
        let state = try? await host.webView.evaluateJavaScript("""
          [document.readyState, document.querySelector('#state')?.textContent,
           document.querySelector('#message')?.textContent, document.querySelectorAll('.marker').length,
           document.querySelectorAll('#draft-list button strong').length].join(' | ')
          """)
        XCTFail("Condition did not become true: \(script); URL \(host.webView.url?.path ?? "none"); state \(state ?? "unknown"); host failed: \(host.failed)", file: file, line: line)
        throw CocoaError(.fileReadUnknown)
    }
    private func perform(_ host: WorkspaceHost, _ script: String) async throws {
        _ = try await host.webView.evaluateJavaScript(script + "; true")
    }

    func testBundleReusesItsLoopbackOriginAndRejectsOtherPaths() async throws {
        let suite = "inklane-origin-" + UUID().uuidString
        let defaults = try XCTUnwrap(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        let root = try XCTUnwrap(Bundle.main.url(forResource: "Workspace", withExtension: nil))
        var expected: URL?
        for _ in 0..<2 {
            let server = WorkspaceServer(root: root, defaults: defaults)
            var result: Result<URL,Error>?
            try server.start { result = $0 }
            for _ in 0..<100 {
                if result != nil { break }
                try await Task.sleep(nanoseconds: 50_000_000)
            }
            let origin = try XCTUnwrap(result).get()
            defer { server.stop() }
            XCTAssertEqual(origin.host, "127.0.0.1")
            if let expected { XCTAssertEqual(origin, expected) }
            expected = origin
            let publicAsset = try await URLSession.shared.data(from: origin)
            XCTAssertEqual((publicAsset.1 as? HTTPURLResponse)?.statusCode, 200)
            let outside = try XCTUnwrap(URL(string: "/Documents/example.txt", relativeTo: origin)?.absoluteURL)
            let rejected = try await URLSession.shared.data(from: outside)
            XCTAssertEqual((rejected.1 as? HTTPURLResponse)?.statusCode, 404)
            server.stop()
            try await Task.sleep(nanoseconds: 250_000_000)
        }
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
        let partial = """
        globalThis.previousProcess = true;
        const bridge = window.webkit.messageHandlers.exportFile;
        const token = await bridge.postMessage({op:'begin',filename:'Partial.inklane',size:2});
        await bridge.postMessage({op:'append',token,bytes:'YQ=='}); return token;
        """
        _ = try await host.webView.callAsyncJavaScript(partial, arguments: [:], in: nil, contentWorld: .page)
        let interrupted = try XCTUnwrap(host.exporter.pendingFile)
        host.webViewWebContentProcessDidTerminate(host.webView)
        XCTAssertFalse(FileManager.default.fileExists(atPath: interrupted.path))
        try await wait(host, "!globalThis.previousProcess && typeof document.querySelector('#sample-link')?.onclick === 'function'")
        let restart = """
        const bridge = window.webkit.messageHandlers.exportFile;
        const token = await bridge.postMessage({op:'begin',filename:'New transfer.inklane',size:0});
        return await bridge.postMessage({op:'discard',token});
        """
        _ = try await host.webView.callAsyncJavaScript(restart, arguments: [:], in: nil, contentWorld: .page)
        XCTAssertNil(host.exporter.pendingFile)
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
        host.exporter.discardUnpresentedTransfer()
        XCTAssertEqual(host.exporter.pendingFile, stagedURL, "The native picker still owns this file.")
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
        try await perform(host, "globalThis.previousDraftLibrary = true")
        host.webView.reload()
        let savedImage = "[...document.querySelectorAll('#draft-list button')].find(b => b.querySelector('strong')?.textContent.endsWith('.png'))"
        try await wait(host, "!globalThis.previousDraftLibrary && !!(\(savedImage))")
        try await perform(host, "(\(savedImage)).click()")
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
        #if os(macOS)
        // Let AppKit approve an actual destination and verify what the application wrote there.
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
        defer { try? FileManager.default.removeItem(at: folder) }
        try await perform(host, "document.querySelector('#close-card').click(); document.querySelector('#save').click()")
        for _ in 0..<120 {
            if host.exporter.picker != nil { break }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        let save = try XCTUnwrap(host.exporter.picker)
        save.directoryURL = folder
        save.nameFieldStringValue = "Review.inklane"
        save.ok(nil)
        let destination = folder.appendingPathComponent("Review.inklane")
        for _ in 0..<120 {
            if FileManager.default.fileExists(atPath: destination.path) { break }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        let saved = try Data(contentsOf: destination)
        XCTAssertEqual(saved, portable)
        #endif
    }
}
