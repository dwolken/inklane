import XCTest
import WebKit
import CoreGraphics
import SwiftUI
@testable import Inklane
#if os(iOS)
import UIKit
private typealias WorkspaceImage = UIImage
#else
import AppKit
private typealias WorkspaceImage = NSImage
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
        // WebKit can paint the new page while its outstanding draft transaction still owns the editor.
        // Wait for the same enabled document controls that a person can actually use.
        try await wait(host, "document.querySelector('#open')?.disabled === false")
        _ = try await host.webView.evaluateJavaScript(script + "; true")
    }
    private func openFile(_ bytes: Data, _ name: String, in host: WorkspaceHost) async throws {
        try await wait(host, "document.querySelector('#open')?.disabled === false")
        let input = """
        const bytes = Uint8Array.from(atob(payload), x => x.charCodeAt(0));
        const transfer = new DataTransfer(); transfer.items.add(new File([bytes], filename));
        const input = document.querySelector('#files'); input.files = transfer.files;
        input.dispatchEvent(new Event('change',{bubbles:true})); return true;
        """
        _ = try await host.webView.callAsyncJavaScript(input,
            arguments: ["payload": bytes.base64EncodedString(), "filename": name], in: nil, contentWorld: .page)
    }

    private func captureExample(_ host: WorkspaceHost) async throws {
        let config = WKSnapshotConfiguration()
        config.afterScreenUpdates = true
        let image: WorkspaceImage = try await withCheckedThrowingContinuation { continuation in
            host.webView.takeSnapshot(with: config) { image, error in
                if let image { continuation.resume(returning: image) }
                else { continuation.resume(throwing: error ?? CocoaError(.fileWriteUnknown)) }
            }
        }
        #if os(iOS)
        let png = try XCTUnwrap(image.pngData())
        #else
        let tiff = try XCTUnwrap(image.tiffRepresentation)
        let bitmap = try XCTUnwrap(NSBitmapImageRep(data: tiff))
        let png = try XCTUnwrap(bitmap.representation(using: .png, properties: [:]))
        #endif
        XCTAssertGreaterThan(image.size.width, 300)
        let cache = try XCTUnwrap(FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask).first)
        try FileManager.default.createDirectory(at: cache, withIntermediateDirectories: true)
        try png.write(to: cache.appendingPathComponent("Inklane-field-guide-review.png"), options: .atomic)
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
        let controller = UIHostingController(rootView: WorkspaceView(host: host))
        window.rootViewController = controller
        window.makeKeyAndVisible()
        defer { window.isHidden = true; oldWindow?.makeKeyAndVisible() }
        #else
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1050, height: 730),
                              styleMask: [.titled,.closable], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.contentViewController = NSHostingController(rootView: WorkspaceView(host: host))
        window.setContentSize(NSSize(width: 1050,height: 730))
        window.makeKeyAndOrderFront(nil)
        defer { window.close() }
        #endif

        host.start()
        NSLog("Apple workspace 1. Recover the renderer and restart its export bridge.")
        try await restartBridge(host)
        NSLog("Apple workspace 2. Render two PDF pages and create two notebook pages.")
        try await readPDF(host)
        try await createNotebook(host)
        NSLog("Apple workspace 3. Edit the example remark and cancel its native export.")
        try await editExample(host)
        let portable = try await cancelExport(host)
        NSLog("Apple workspace 4. Reopen the on-device draft and portable file.")
        try await recoverDraft(host)
        try await readPortable(portable,in: host)
        NSLog("Completed hosted document journeys. AppKit’s affirmative file-panel journey runs in the separate Mac UI scheme.")
    }

    private func restartBridge(_ host: WorkspaceHost) async throws {
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
    }

    private func readPDF(_ host: WorkspaceHost) async throws {
        // Exercise the bundled PDF worker and observe the pixels of two distinct source pages.
        let pdf = NSMutableData()
        let consumer = try XCTUnwrap(CGDataConsumer(data: pdf as CFMutableData))
        var bounds = CGRect(x: 0, y: 0, width: 240, height: 360)
        let graphics = try XCTUnwrap(CGContext(consumer: consumer, mediaBox: &bounds, nil))
        for color in [CGColor(red: 1, green: 0, blue: 0, alpha: 1), CGColor(red: 0, green: 0, blue: 1, alpha: 1)] {
            graphics.beginPDFPage(nil)
            graphics.setFillColor(color)
            graphics.fill(bounds)
            graphics.endPDFPage()
        }
        graphics.closePDF()
        try await openFile(pdf as Data, "Review.pdf", in: host)
        let pixel = "document.querySelector('#source').getContext('2d').getImageData(2,2,1,1).data"
        try await wait(host, "document.querySelector('#page-count').textContent.includes('2') && \(pixel)[0] > 220 && \(pixel)[2] < 30")
        try await perform(host, "document.querySelector('#next-page').click()")
        try await wait(host, "\(pixel)[2] > 220 && \(pixel)[0] < 30")
        try await perform(host, "document.querySelector('#home').click()")
        try await wait(host, "!document.querySelector('#welcome').hidden")
    }

    private func createNotebook(_ host: WorkspaceHost) async throws {
        // Exercise both the new-document dialog and real multipage navigation.
        try await perform(host, "document.querySelector('#new-notebook').click(); document.querySelector('#paper-size').value='A5'; document.querySelector('#paper-form').requestSubmit()")
        try await wait(host, "!document.querySelector('#pages').hidden && !document.querySelector('#save').disabled")
        try await perform(host, "document.querySelector('#add-page').click()")
        try await wait(host, "document.querySelector('#page-count').textContent.includes('2')")
        try await perform(host, "document.querySelector('#home').click()")
        try await wait(host, "!document.querySelector('#welcome').hidden && !!document.querySelector('#draft-list button')")
    }

    private func editExample(_ host: WorkspaceHost) async throws {
        try await perform(host, "document.querySelector('#sample-link').click()")
        try await wait(host, "document.querySelectorAll('.marker').length === 2")
        // Use the public notes index to open the same edit card at either screen width.
        try await perform(host, "document.querySelector('#index').click()")
        try await wait(host, "!!document.querySelector('#results button')")
        try await perform(host, "document.querySelector('#results button').click()")
        try await wait(host, "!document.querySelector('#card').hidden")
        try await captureExample(host)
        try await perform(host, """
            for(const [id,value] of [['#note-title','Recheck scale'],['#note-body','Keep the scale bar at this position.']]) {
              const field=document.querySelector(id); field.focus();field.value=value;
              field.dispatchEvent(new Event('input',{bubbles:true}));
            }
            document.querySelector('#close-card').click()
            """)
        try await wait(host, "!!document.querySelector('.marker[aria-label=\"Recheck scale\"]')")
    }

    private func cancelExport(_ host: WorkspaceHost) async throws -> Data {
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
        DispatchQueue.main.async { picker.cancel(nil) }
        #endif
        try await wait(host, "!document.querySelector('#save').disabled")
        XCTAssertNil(host.exporter.pendingFile)
        XCTAssertFalse(FileManager.default.fileExists(atPath: stagedURL.path))
        return portable
    }

    private func recoverDraft(_ host: WorkspaceHost) async throws {
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
    }

    private func readPortable(_ portable: Data, in host: WorkspaceHost) async throws {
        // Feed the exact staged portable bytes through the normal file input and check both attached marks.
        try await openFile(portable, "Review.inklane", in: host)
        try await wait(host, "document.querySelectorAll('.marker').length === 2 && !!document.querySelector('.marker[aria-label=\"Recheck scale\"]')")
        try await perform(host, "document.querySelector('#index').click(); document.querySelector('#results button').click()")
        try await wait(host, "document.querySelector('#note-body').value === 'Keep the scale bar at this position.'")
    }

}
