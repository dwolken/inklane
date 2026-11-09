#!/usr/bin/python3
"""GTK host for the bundled Inklane workspace. User documents stay inside WebKit."""
import base64
import binascii
import ctypes
import errno
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import secrets
import shutil
import sys
import tempfile
from threading import Thread
from urllib.parse import unquote, urlsplit

import gi

gi.require_version("Gtk", "3.0")
gi.require_version("WebKit2", "4.1")
gi.require_version("JavaScriptCore", "4.1")
from gi.repository import Gio, GLib, Gtk, JavaScriptCore as JSC, WebKit2

INSTALL = Path(__file__).resolve().parent
MAX_BYTES = 100 * 1024 * 1024
ASSETS = INSTALL / "workspace"
MIME = {".html": "text/html; charset=utf-8", ".css": "text/css", ".mjs": "text/javascript",
        ".js": "text/javascript", ".svg": "image/svg+xml", ".png": "image/png",
        ".woff2": "font/woff2", ".json": "application/json", ".wasm": "application/wasm", ".webmanifest": "application/manifest+json"}


class AssetRequest(BaseHTTPRequestHandler):
    def do_GET(self):
        # Read-only loopback assets; no document, filesystem or export RPC endpoint.
        if self.headers.get("Host") != f"127.0.0.1:{self.server.server_port}":
            self.send_error(403)
            return
        route = unquote(urlsplit(self.path).path)
        if not route.startswith(("/editor/", "/assets/")) or any(p in (".", "..") for p in route.split("/")):
            self.send_error(404)
            return
        if route.endswith("/"):
            route += "index.html"
        target = (ASSETS / route.lstrip("/")).resolve()
        if not target.is_relative_to(ASSETS) or not target.is_file():
            self.send_error(404)
            return
        try:
            with target.open("rb") as stream:
                self.send_response(200)
                self.send_header("Content-Length", str(os.fstat(stream.fileno()).st_size))
                self.send_header("Content-Type", MIME.get(target.suffix, "application/octet-stream"))
                self.send_header("X-Content-Type-Options", "nosniff")
                self.send_header("Referrer-Policy", "no-referrer")
                self.end_headers()
                shutil.copyfileobj(stream, self.wfile)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, *args):
        pass


class Exports:
    def __init__(self, select_destination):
        self.select_destination = select_destination
        self.pending = None
        self.presenting = False

    def discard(self):
        if self.pending:
            self.pending["stream"].close()
            self.pending = None

    def accept(self, msg):
        op = msg["op"]
        if op == "begin":
            name, size = msg["filename"], msg["size"]
            if (self.pending or self.presenting or not isinstance(size, int) or not 0 <= size <= MAX_BYTES
                    or not isinstance(name,str) or name in ("",".","..") or Path(name).name != name
                    or len(name)>255 or any(ord(c)<32 for c in name)
                    or not name.lower().endswith((".inklane", ".inklane.json", ".feedback.json", ".png"))):
                raise ValueError("Invalid export.")
            self.pending = dict(name=name,size=size,token=secrets.token_hex(24),stream=tempfile.TemporaryFile())
            return self.pending["token"]
        if not self.pending or msg.get("token") != self.pending["token"]:
            raise ValueError("Invalid transfer.")
        transfer = self.pending
        stream = transfer["stream"]
        if op == "discard":
            self.discard()
            return ""
        if op == "append":
            data = msg["bytes"]
            if not isinstance(data,str) or len(data)>262144:
                raise ValueError("Invalid block.")
            decoded = base64.b64decode(data, validate=True)
            if stream.tell()+len(decoded)>transfer["size"]:
                raise ValueError("Invalid size.")
            stream.write(decoded)
            return ""
        if op != "export" or stream.tell()!=transfer["size"]:
            raise ValueError("Incomplete transfer.")
        # A complete payload now belongs to the native save dialog. A renderer crash
        # may discard a partial upload, but cannot invalidate the user's pending copy.
        self.pending = None
        self.presenting = True
        try:
            destination = self.select_destination(transfer["name"])
            if destination is None:
                return "cancelled"
            destination = Path(destination)
            suffix = next(ext for ext in (".inklane.json",".feedback.json",".inklane",".png") if transfer["name"].lower().endswith(ext))
            if not destination.name.lower().endswith(suffix):
                raise ValueError("Unexpected file extension.")
            # Publish an entirely written file. Never replace a source or a newly appeared destination.
            with tempfile.NamedTemporaryFile(dir=destination.parent, prefix=".inklane-", delete=False) as staged:
                moved = False
                try:
                    stream.seek(0)
                    shutil.copyfileobj(stream,staged)
                    staged.flush()
                    os.fsync(staged.fileno())
                    try:
                        os.link(staged.name, destination)
                    except OSError as error:
                        if error.errno not in (errno.EOPNOTSUPP, errno.EPERM, errno.EACCES, errno.ENOSYS, errno.EMLINK):
                            raise
                        # FAT volumes and some network shares reject hard links. RENAME_NOREPLACE
                        # performs the same atomic collision check while moving our completed file.
                        rename = getattr(ctypes.CDLL(None, use_errno=True), "renameat2", None)
                        if rename is None:
                            raise
                        at_fdcwd, noreplace = -100, 1
                        if rename(at_fdcwd, os.fsencode(staged.name), at_fdcwd, os.fsencode(destination), noreplace) != 0:
                            reason = ctypes.get_errno()
                            raise OSError(reason, os.strerror(reason), destination) from error
                        moved = True
                finally:
                    if not moved:
                        os.unlink(staged.name)
            return "saved"
        finally:
            stream.close()
            self.presenting = False


class Inklane(Gtk.Application):
    def __init__(self):
        super().__init__(application_id="io.github.dwolken.Inklane", flags=Gio.ApplicationFlags.FLAGS_NONE)
        self.window = self.server = self.view = None
        self.exports = Exports(self.choose_copy)
        self.renderer_generation = 0

    def choose_copy(self, name):
        german = GLib.get_language_names()[0].lower().startswith("de")
        title = "Kopie speichern" if german else "Save a copy"
        dialog = Gtk.FileChooserDialog(title=title,transient_for=self.window,action=Gtk.FileChooserAction.SAVE)
        dialog.add_buttons(Gtk.STOCK_CANCEL,Gtk.ResponseType.CANCEL,Gtk.STOCK_SAVE,Gtk.ResponseType.ACCEPT)
        dialog.set_current_name(name)
        if Path(name).suffix:
            filt=Gtk.FileFilter(); filt.set_name("Inklane"); filt.add_pattern("*"+Path(name).suffix); dialog.add_filter(filt)
        try:
            while dialog.run() == Gtk.ResponseType.ACCEPT:
                path = Path(dialog.get_filename())
                if not path.exists():
                    return path
                text = "Wählen Sie einen neuen Dateinamen." if german else "Choose an unused filename for this copy."
                notice=Gtk.MessageDialog(transient_for=dialog,buttons=Gtk.ButtonsType.OK,text=text)
                notice.run();notice.destroy()
            return None
        finally:
            dialog.destroy()

    def on_message(self, manager, value, reply):
        generation = self.renderer_generation
        try:
            if not self.view or not self.valid_document(self.view.get_uri()):
                raise ValueError("Untrusted page.")
            raw = value.to_json(0)
            if not raw or len(raw)>270000:
                raise ValueError("Invalid message.")
            result = self.exports.accept(json.loads(raw))
            if generation == self.renderer_generation:
                reply.return_value(JSC.Value.new_string(value.get_context(), result))
        except (ValueError, KeyError, TypeError, OSError, binascii.Error) as error:
            if generation == self.renderer_generation:
                self.exports.discard()
                reply.return_error_message(str(error))
        return True

    def renderer_terminated(self, view, reason):
        self.renderer_generation += 1
        self.exports.discard()
        # Reopen the trusted library with the same origin and persistent draft store.
        view.load_uri(f"http://127.0.0.1:{self.server.server_port}/editor/")

    def valid_document(self, uri):
        if not uri or not self.server:
            return False
        parts=urlsplit(uri)
        return (parts.scheme=="http" and parts.netloc==f"127.0.0.1:{self.server.server_port}"
                and parts.path in ("/editor/","/editor/index.html"))

    def navigation(self, view, decision, kind):
        if kind == WebKit2.PolicyDecisionType.NEW_WINDOW_ACTION:
            decision.ignore()
            return True
        if kind == WebKit2.PolicyDecisionType.NAVIGATION_ACTION:
            action=decision.get_navigation_action()
            uri=action.get_request().get_uri()
            if (action.get_navigation_type()==WebKit2.NavigationType.LINK_CLICKED
                    and self.valid_document(view.get_uri()) and urlsplit(uri).scheme in ("https","http","mailto")
                    and not self.valid_document(uri)):
                try: Gio.AppInfo.launch_default_for_uri(uri,None)
                except GLib.Error: pass
                decision.ignore()
                return True
            if not self.valid_document(uri):
                decision.ignore()
                return True
        return False

    def do_activate(self):
        if self.window:
            self.window.present()
            return
        self.window=Gtk.ApplicationWindow(application=self,title="Inklane")
        self.window.set_default_size(1180,800)
        self.window.set_icon_name("io.github.dwolken.Inklane")
        config=Path(GLib.get_user_config_dir())/"inklane"
        config.mkdir(mode=0o700,parents=True,exist_ok=True)
        port_file=config/"workspace-port"
        try:
            # Reuse this installation's origin on next launch to recover IndexedDB drafts.
            port=int(port_file.read_text()) if port_file.exists() else 0
            self.server=ThreadingHTTPServer(("127.0.0.1",port),AssetRequest)
            port_file.write_text(str(self.server.server_port))
        except (OSError,ValueError,OverflowError):
            german=GLib.get_language_names()[0].lower().startswith("de")
            detail="Der lokale Arbeitsbereich konnte nicht geöffnet werden." if german else "The local workspace could not be opened."
            error=Gtk.MessageDialog(transient_for=self.window,text=detail,buttons=Gtk.ButtonsType.CLOSE)
            error.run();error.destroy();self.window.destroy();return
        Thread(target=self.server.serve_forever, daemon=True).start()
        manager=WebKit2.UserContentManager()
        manager.register_script_message_handler_with_reply("exportFile",None)
        manager.connect("script-message-with-reply-received::exportFile",self.on_message)
        origin=f"http://127.0.0.1:{self.server.server_port}"
        bridge=(INSTALL/"NativeSave.js").read_text()
        # WebKit URL match patterns do not consistently handle explicit loopback ports.
        # The script itself checks the exact bound origin before exposing the native adapter.
        scoped = ("if(location.origin===" + json.dumps(origin) +
                  " && ['/editor/','/editor/index.html'].includes(location.pathname)){" + bridge + "\n}")
        manager.add_script(WebKit2.UserScript.new(scoped,WebKit2.UserContentInjectedFrames.TOP_FRAME,
            WebKit2.UserScriptInjectionTime.START,None,None))
        storage=Path(GLib.get_user_data_dir())/"inklane"/"web"
        cache=Path(GLib.get_user_cache_dir())/"inklane"/"web"
        stores=WebKit2.WebsiteDataManager(base_data_directory=str(storage),base_cache_directory=str(cache))
        context=WebKit2.WebContext.new_with_website_data_manager(stores)
        self.view=WebKit2.WebView(web_context=context,user_content_manager=manager)
        self.view.connect("decide-policy",self.navigation)
        self.view.connect("web-process-terminated",self.renderer_terminated)
        self.view.connect("permission-request",lambda _,request: (request.deny(),True)[1])
        self.view.connect("close",lambda *_:self.window.destroy())
        self.window.connect("delete-event",lambda *_:(self.view.try_close(),True)[1])
        self.window.add(self.view);self.window.show_all()
        self.view.load_uri(origin+"/editor/")

    def do_shutdown(self):
        self.exports.discard()
        if self.server:
            self.server.shutdown();self.server.server_close()
        Gtk.Application.do_shutdown(self)


if __name__ == "__main__":
    GLib.set_application_name("Inklane")
    sys.exit(Inklane().run(sys.argv))
