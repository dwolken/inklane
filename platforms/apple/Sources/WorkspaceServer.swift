import Foundation
import Network

/// Serves the app bundle only. A stable origin keeps local drafts available after relaunch.
final class WorkspaceServer {
    private let queue = DispatchQueue(label: "Inklane.bundle")
    private let root: URL
    private let defaults: UserDefaults
    private var listener: NWListener?

    init(root: URL, defaults: UserDefaults = .standard) {
        self.root = root
        self.defaults = defaults
    }

    func start(_ ready: @escaping (Result<URL, Error>) -> Void) throws {
        let saved = defaults.integer(forKey: "workspaceLoopbackPort")
        let port = UInt16(exactly: saved).flatMap { NWEndpoint.Port(rawValue: $0) } ?? .any
        let parameters = NWParameters.tcp
        parameters.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: port)
        let service = try NWListener(using: parameters)
        listener = service
        service.stateUpdateHandler = { [weak self, weak service] state in
            guard let self, let service else { return }
            switch state {
            case .ready:
                guard let port = service.port else { return }
                self.defaults.set(Int(port.rawValue), forKey: "workspaceLoopbackPort")
                let url = URL(string: "http://127.0.0.1:\(port.rawValue)/editor/index.html")!
                DispatchQueue.main.async { ready(.success(url)) }
            case .failed(let error):
                DispatchQueue.main.async { ready(.failure(error)) }
            default: break
            }
        }
        service.newConnectionHandler = { [weak self, weak service] client in
            guard let self, let port = service?.port else { client.cancel(); return }
            client.start(queue: self.queue)
            self.read(client, port: port.rawValue, received: Data())
        }
        service.start(queue: queue)
    }

    private func read(_ client: NWConnection, port: UInt16, received: Data) {
        client.receive(minimumIncompleteLength: 1, maximumLength: 8192) { [weak self] bytes, _, ended, error in
            guard let self, error == nil, let bytes else { client.cancel(); return }
            let header = received + bytes
            guard header.count <= 8192 else { client.cancel(); return }
            guard let boundary = header.range(of: Data([13,10,13,10])) else {
                if ended { client.cancel() } else { self.read(client, port: port, received: header) }
                return
            }
            guard let request = String(data: header[..<boundary.lowerBound], encoding: .utf8) else {
                client.cancel()
                return
            }
            self.respond(client, request: request, port: port)
        }
    }

    private func respond(_ client: NWConnection, request: String, port: UInt16) {
        let lines = request.components(separatedBy: "\r\n")
        let first = lines.first?.split(separator: " ") ?? []
        guard first.count == 3, first[0] == "GET" || first[0] == "HEAD",
              lines.dropFirst().contains(where: { $0.lowercased() == "host: 127.0.0.1:\(port)" }),
              let encoded = first[1].split(separator: "?").first,
              let path = String(encoded).removingPercentEncoding,
              path.hasPrefix("/editor/") || path.hasPrefix("/assets/"),
              !path.split(separator: "/").contains(".."), !path.contains("\\"), !path.contains("\0") else {
            send(client, status: "404 Not Found", mime: "text/plain", data: Data(), head: true)
            return
        }
        let relative = String(path.dropFirst()) + (path.hasSuffix("/") ? "index.html" : "")
        let url = root.appendingPathComponent(relative)
        guard let data = try? Data(contentsOf: url, options: .mappedIfSafe) else {
            send(client, status: "404 Not Found", mime: "text/plain", data: Data(), head: true)
            return
        }
        let mime = ["html":"text/html; charset=utf-8", "js":"text/javascript; charset=utf-8",
                    "mjs":"text/javascript; charset=utf-8", "css":"text/css; charset=utf-8",
                    "png":"image/png", "svg":"image/svg+xml", "wasm":"application/wasm",
                    "json":"application/json", "webmanifest":"application/manifest+json",
                    "woff2":"font/woff2", "ttf":"font/ttf", "otf":"font/otf"]
        send(client, status: "200 OK", mime: mime[url.pathExtension] ?? "application/octet-stream",
             data: data, head: first[0] == "HEAD")
    }

    private func send(_ client: NWConnection, status: String, mime: String, data: Data, head: Bool) {
        let headers = "HTTP/1.1 \(status)\r\nContent-Type: \(mime)\r\nContent-Length: \(data.count)\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n"
        client.send(content: Data(headers.utf8) + (head ? Data() : data), completion: .contentProcessed { _ in client.cancel() })
    }

    func stop() { listener?.cancel(); listener = nil }
    deinit { stop() }
}
