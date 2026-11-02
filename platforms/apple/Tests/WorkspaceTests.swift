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
    private func javascript(_ host: WorkspaceHost, _ script: String,
                            arguments: [String: Any]? = nil) async throws -> Any? {
        try await withCheckedThrowingContinuation { continuation in
            InklaneTestJavaScript(host.webView, script, arguments) { value, error in
                if let error { continuation.resume(throwing: error) }
                else { continuation.resume(returning: value) }
            }
        }
    }

    private func wait(_ host: WorkspaceHost, _ script: String,
                      file: StaticString = #filePath, line: UInt = #line) async throws {
        for _ in 0..<180 {
            if let value = try? await javascript(host, script), value as? Bool == true { return }
            try await Task.sleep(nanoseconds: 100_000_000)
        }
        let state = try? await javascript(host, """
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
        _ = try await javascript(host, script + "; true")
    }
    private func openFile(_ bytes: Data, _ name: String, in host: WorkspaceHost) async throws {
        try await wait(host, "document.querySelector('#open')?.disabled === false")
        let input = """
        const bytes = Uint8Array.from(atob(payload), x => x.charCodeAt(0));
        const transfer = new DataTransfer(); transfer.items.add(new File([bytes], filename));
        const input = document.querySelector('#files'); input.files = transfer.files;
        input.dispatchEvent(new Event('change',{bubbles:true})); return true;
        """
        _ = try await javascript(host, input, arguments: ["payload": bytes.base64EncodedString(), "filename": name])
    }

    #if os(iOS)
    private func measureViewport(_ host: WorkspaceHost, _ label: String) async throws -> Bool {
        let result = try await javascript(host, """
          (() => {
            const v = visualViewport, f = document.activeElement, r = s => {
              const b = document.querySelector(s).getBoundingClientRect(); return [b.left,b.top,b.right,b.bottom];
            };
            return { width: v.width, height: v.height, scale: v.scale, top: v.offsetTop, left: v.offsetLeft,
              layout: document.documentElement.clientWidth, active: f.id || f.tagName, font: getComputedStyle(f).fontSize,
              header: r('header'), save: r('#save'), footer: r('footer') };
          })()
          """)
        let m = try XCTUnwrap(result as? [String: Any])
        let view = host.webView
        let details = try JSONSerialization.data(withJSONObject: m, options: [.sortedKeys])
        NSLog("iPhone viewport %@ — webView %.2f x %.2f points, scrollViewZoom %.3f %@",
              label, Double(view.bounds.width), Double(view.bounds.height), Double(view.scrollView.zoomScale),
              String(decoding: details, as: UTF8.self))
        guard let width = m["width"] as? Double, let height = m["height"] as? Double,
              let scale = m["scale"] as? Double, let left = m["left"] as? Double, let top = m["top"] as? Double,
              let header = m["header"] as? [Double], let footer = m["footer"] as? [Double],
              let save = m["save"] as? [Double] else { return false }
        return abs(scale-1) < 0.025 && abs(width-Double(view.bounds.width)) < 2 &&
               header[0] >= left-2 && header[2] <= left+width+2 && save[2] <= left+width+2 &&
               footer[3] <= top+height+2 && footer[1] >= top
    }
    #endif

    private func captureExample(_ host: WorkspaceHost) async throws {
        #if os(iOS)
        var fullWorkspace = false
        for attempt in 0..<12 {
            if try await measureViewport(host,"field guide after index dismissal (\(attempt))") {
                try await Task.sleep(nanoseconds: 250_000_000)
                if try await measureViewport(host,"field guide settled") { fullWorkspace = true; break }
            }
            try await Task.sleep(nanoseconds: 200_000_000)
        }
        #endif
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
        #if os(iOS)
        // The CI helper takes an independent simulator screenshot on request. Keep the same
        // native window and unmodified viewport visible until the compositor capture finishes.
        if FileManager.default.fileExists(atPath: cache.appendingPathComponent("Inklane-screen.armed").path) {
            try Data().write(to: cache.appendingPathComponent("Inklane-screen.ready"), options: .atomic)
            let done = cache.appendingPathComponent("Inklane-screen.done").path
            for _ in 0..<400 {
                if FileManager.default.fileExists(atPath: done) { break }
                try await Task.sleep(nanoseconds: 100_000_000)
            }
            XCTAssertTrue(FileManager.default.fileExists(atPath: done),"Simulator compositor capture timed out")
        }
        XCTAssertTrue(fullWorkspace,"The field guide should show the complete toolbar and footer after text entry, without a pinch gesture.")
        #endif
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
        NSLog("Completed hosted document journeys. The Mac UI scheme checks affirmative file selection separately.")
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
        _ = try await javascript(host, partial, arguments: [:])
        let interrupted = try XCTUnwrap(host.exporter.pendingFile)
        host.webViewWebContentProcessDidTerminate(host.webView)
        XCTAssertFalse(FileManager.default.fileExists(atPath: interrupted.path))
        try await wait(host, "!globalThis.previousProcess && typeof document.querySelector('#sample-link')?.onclick === 'function'")
        let restart = """
        const bridge = window.webkit.messageHandlers.exportFile;
        const token = await bridge.postMessage({op:'begin',filename:'New transfer.inklane',size:0});
        return await bridge.postMessage({op:'discard',token});
        """
        _ = try await javascript(host, restart, arguments: [:])
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
        #if os(iOS)
        _ = try await measureViewport(host,"field guide before opening search")
        #endif
        // Use the public notes index to open the same edit card at either screen width.
        try await perform(host, "document.querySelector('#index').click()")
        try await wait(host, "!!document.querySelector('#results button')")
        #if os(iOS)
        _ = try await measureViewport(host,"focused note search")
        #endif
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
