import XCTest
import CryptoKit

final class SavePanelTests: XCTestCase {
    private func find(_ element: XCUIElement, within timeout: TimeInterval = 12,
                      file: StaticString = #filePath, line: UInt = #line) throws -> XCUIElement {
        guard element.waitForExistence(timeout: timeout) else {
            XCTFail("Expected a native workspace or file chooser control.",file: file,line: line)
            throw CocoaError(.fileReadUnknown)
        }
        return element
    }

    private func chooser(_ app: XCUIApplication, button: String) throws -> XCUIElement {
        let sheet = app.sheets.firstMatch
        if sheet.waitForExistence(timeout: 6), sheet.buttons[button].exists { return sheet }
        let service = XCUIApplication(bundleIdentifier: "com.apple.appkit.xpc.openAndSavePanelService")
        for scope in [service.dialogs.firstMatch, service.windows.firstMatch, app.dialogs.firstMatch] {
            if scope.exists && scope.buttons[button].exists { return scope }
        }
        XCTFail("The native \(button) panel was not accessible.")
        throw CocoaError(.fileReadUnknown)
    }

    private func go(_ app: XCUIApplication, to directory: URL) {
        app.typeKey("g", modifierFlags: [.command,.shift])
        app.typeKey("a", modifierFlags: .command)
        app.typeText(directory.path + "/")
        app.typeKey(.return, modifierFlags: [])
    }

    private func matching(_ value: String, in panel: XCUIElement) -> XCUIElement {
        panel.descendants(matching: .any).matching(NSPredicate(
            format: "label == %@ OR title == %@ OR value == %@", value,value,value)).firstMatch
    }

    /// Require visible evidence of the disposable directory before ever approving a write.
    private func identifyFolder(_ folder: URL, marker: URL, in panel: XCUIElement) throws {
        let directoryName = matching(folder.lastPathComponent, in: panel)
        let sentinel = matching(marker.lastPathComponent, in: panel)
        let hiddenExtension = matching(marker.deletingPathExtension().lastPathComponent, in: panel)
        if directoryName.waitForExistence(timeout: 3) || sentinel.exists || hiddenExtension.exists { return }
        let disclosure = panel.buttons.matching(NSPredicate(
            format: "label CONTAINS[c] 'show details' OR label CONTAINS[c] 'expand'")).firstMatch
        if disclosure.exists { disclosure.click() }
        else if panel.disclosureTriangles.firstMatch.exists { panel.disclosureTriangles.firstMatch.click() }
        if !directoryName.exists && !sentinel.waitForExistence(timeout: 5) && !hiddenExtension.exists {
            panel.buttons["Cancel"].click()
            XCTFail("The chooser did not display the disposable review folder.")
            throw CocoaError(.fileNoSuchFile)
        }
    }

    private func save(_ baseName: String, app: XCUIApplication, folder: URL, marker: URL) throws -> Data {
        try find(app.webViews.buttons["Save"]).click()
        let panel = try chooser(app,button: "Save")
        go(app,to: folder)
        try identifyFolder(folder,marker: marker,in: panel)
        let name = try find(panel.textFields.firstMatch)
        name.click()
        name.typeKey("a", modifierFlags: .command)
        name.typeText(baseName + ".inklane")
        let destination = folder.appendingPathComponent(baseName + ".inklane")
        XCTAssertFalse(FileManager.default.fileExists(atPath: destination.path))
        try find(panel.buttons["Save"]).click()
        let exists = XCTNSPredicateExpectation(predicate: NSPredicate { _, _ in
            FileManager.default.fileExists(atPath: destination.path)
        }, object: nil)
        XCTAssertEqual(XCTWaiter.wait(for: [exists],timeout: 12), .completed)
        return try Data(contentsOf: destination)
    }

    func testUserSavesAndReopensAPortableReviewThroughSystemPanels() throws {
        continueAfterFailure = false
        let manager = FileManager.default
        let folder = manager.temporaryDirectory.resolvingSymlinksInPath()
            .appendingPathComponent("Inklane-review-" + UUID().uuidString, isDirectory:true)
        try manager.createDirectory(at: folder,withIntermediateDirectories: false)
        let id = UUID().uuidString
        let marker = folder.appendingPathComponent("Location-" + id + ".inklane")
        try Data().write(to: marker,options: .withoutOverwriting)
        let first = "Review-" + id, second = "Reopened-" + id
        defer {
            for url in [marker,folder.appendingPathComponent(first + ".inklane"),folder.appendingPathComponent(second + ".inklane")] {
                try? manager.removeItem(at: url)
            }
            if (try? manager.contentsOfDirectory(atPath: folder.path).isEmpty) == true {
                try? manager.removeItem(at: folder)
            }
        }

        let app = XCUIApplication()
        app.launchArguments = ["-AppleLanguages","(en)","-AppleLocale","en_US"]
        app.launch()
        try find(app.webViews.buttons["Open the field guide"]).click()
        let heading = app.webViews.textFields["Note title"]
        if !heading.waitForExistence(timeout: 3) { try find(app.webViews.buttons["Petal colour"]).click() }
        _ = try find(heading)
        heading.click()
        heading.typeKey("a",modifierFlags: .command)
        heading.typeText("Check the specimen")
        heading.typeKey(.escape,modifierFlags: [])
        let saved = try save(first,app: app,folder: folder,marker: marker)
        XCTAssertEqual(saved.prefix(8),Data("INKLANE1".utf8))
        let length = saved[8..<12].reduce(0) { $0 * 256 + Int($1) }
        XCTAssertTrue((1..<(saved.count-12)).contains(length))
        let envelope = try XCTUnwrap(JSONSerialization.jsonObject(with: saved[12..<12+length]) as? [String:Any])
        let document = try XCTUnwrap(envelope["document"] as? [String:Any])
        let source = saved.suffix(from: 12+length)
        let digest = SHA256.hash(data: source).map { String(format:"%02X",$0) }.joined()
        XCTAssertEqual(document["sourceImageSha256"] as? String,digest)
        let pages = try XCTUnwrap(document["pages"] as? [[String:Any]])
        let items = try XCTUnwrap(pages.first?["items"] as? [[String:Any]])
        XCTAssertTrue(items.contains { $0["title"] as? String == "Check the specimen" })

        // Select the saved file in the actual Open panel and save it to a second, unused destination.
        app.terminate()
        app.launch()
        try find(app.webViews.buttons["Open"]).click()
        let open = try chooser(app,button: "Open")
        go(app,to: folder)
        let row = matching(first + ".inklane",in: open)
        let stem = matching(first,in: open)
        if row.waitForExistence(timeout: 4) { row.doubleClick() }
        else { try find(stem).doubleClick() }
        _ = try find(app.webViews.buttons["Check the specimen"])
        let reopened = try save(second,app: app,folder: folder,marker: marker)
        XCTAssertEqual(saved,reopened)
    }
}
