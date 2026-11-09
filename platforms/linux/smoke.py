"""Exercise the installed GTK/WebKit application under a disposable X11 display."""
from pathlib import Path
from time import monotonic, sleep
from unittest.mock import patch
import base64
import errno
import hashlib
import json
import subprocess
import sys
import tempfile
import traceback

sys.path.insert(0, "/usr/share/inklane")
from inklane import Exports, Inklane, Gtk, GLib, WebKit2
import gi
gi.require_version("GdkX11","3.0")
from gi.repository import GdkX11

def check_exports_without_hardlinks():
    with tempfile.TemporaryDirectory() as directory:
        target = Path(directory) / "Reviewed.inklane"
        exports = Exports(lambda _: target)

        def export(payload):
            token = exports.accept({"op": "begin", "filename": target.name, "size": len(payload)})
            exports.accept({"op": "append", "token": token, "bytes": base64.b64encode(payload).decode("ascii")})
            return exports.accept({"op": "export", "token": token})

        unavailable = OSError(errno.EOPNOTSUPP, "Hard links unavailable")
        with patch("inklane.os.link", side_effect=unavailable):
            assert export(b"first review") == "saved"
            try:
                export(b"second review")
            except FileExistsError:
                pass
            else:
                raise AssertionError("A second export replaced an existing copy")
        assert target.read_bytes() == b"first review"
        assert list(target.parent.iterdir()) == [target]


def tick():
    while Gtk.events_pending():
        Gtk.main_iteration_do(False)
    GLib.MainContext.default().iteration(False)
    sleep(.025)


def evaluate(view, script):
    result = []
    def done(web, task, _):
        try:
            value = web.evaluate_javascript_finish(task)
            result.append(value.to_string())
        except Exception as error:
            result.append(error)
    view.evaluate_javascript(script,-1,None,None,None,done,None)
    end=monotonic()+8
    while not result and monotonic()<end:
        tick()
    if not result: raise AssertionError("Timed out evaluating test interaction.")
    if isinstance(result[0],Exception): raise result[0]
    return result[0]


def until(view, condition, label, timeout=25):
    end = monotonic() + timeout
    while monotonic() < end:
        if evaluate(view,condition)=="true": return
        tick()
    raise AssertionError("Timed out: " + label + " (" + evaluate(view,"document.querySelector('#message')?.textContent") + ")")


class Check(Inklane):
    failed = False

    def do_activate(self):
        super().do_activate()
        self.view.get_settings().set_enable_write_console_messages_to_stdout(True)
        GLib.idle_add(self.exercise)

    def exercise(self):
        try:
            view=self.view
            until(view,"typeof window.inklaneSaveCopy === 'function' && !!document.querySelector('#start')?.onclick","native shell")
            assert evaluate(view,"globalThis.inklaneReducedMotion") == json.dumps(not Gtk.Settings.get_default().get_property("gtk-enable-animations"))
            until(view,"document.querySelector('.brand img')?.complete && document.querySelector('.brand img')?.naturalWidth > 0","local brand asset")
            settings=Gtk.Settings.get_default()
            original_motion=settings.get_property("gtk-enable-animations")
            settings.set_property("gtk-enable-animations",False)
            until(view,"globalThis.inklaneReducedMotion === true && !document.querySelector('.brand').classList.contains('launch')","live native reduced-motion preference")
            settings.set_property("gtk-enable-animations",original_motion)
            until(view,"globalThis.inklaneReducedMotion === " + json.dumps(not original_motion),"restored native motion preference")
            xid=GdkX11.X11Window.get_xid(self.window.get_window())
            self.view.grab_focus()
            def open_file(path):
                def choose(_,request):
                    request.select_files([str(path)])
                    return True
                handler=view.connect("run-file-chooser",choose)
                try:
                    subprocess.run(["xdotool","windowfocus","--sync",str(xid),"key","ctrl+o"],check=True)
                    until(view,"document.querySelector('#name').textContent === 'Field corrections' && !document.querySelector('#viewport').hidden","GTK file chooser result")
                finally:
                    view.disconnect(handler)
            open_file(input_path)
            until(view,"document.querySelectorAll('.marker').length===1","restored note marker")
            evaluate(view,"document.querySelector('.marker').click();document.querySelector('#note-body').focus();'ready'")
            evaluate(view,"let field=document.querySelector('#note-body');field.value='Confirmed in GTK';field.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#close-card').click();document.querySelector('#save').click();'sent'")
            until(view,"document.querySelector('#state').textContent==='Annotations saved'","native filesystem save")
            assert output.is_file() and output.read_bytes()[:8] == b"INKLANE1"
            metadata=output.read_bytes(); n=int.from_bytes(metadata[8:12],"big")
            record=json.loads(metadata[12:12+n])
            assert record["document"]["pages"][0]["items"][0]["text"]=="Confirmed in GTK"
            evaluate(view,"document.querySelector('#home').click();'ok'")
            until(view,"!document.querySelector('#welcome').hidden","document library")
            open_file(output)
            evaluate(view,"document.querySelector('.marker').click();'ok'")
            until(view,"document.querySelector('#note-body').value==='Confirmed in GTK'","edited note reopened")
            assert input_path.read_bytes() != output.read_bytes()
            self.check_pdf()
            self.check_renderer_recovery()
            print("Installed GTK host opened a portable notebook through its file chooser, edited a pinned note, saved it through native file transfer, and reopened the saved content.")
        except BaseException:
            self.failed=True
            traceback.print_exc()
            print("Startup state:", evaluate(self.view, "JSON.stringify({path:location.pathname,state:document.readyState,bridge:typeof window.inklaneSaveCopy,start:typeof document.querySelector('#start')?.onclick,status:document.querySelector('#state')?.textContent,secure:isSecureContext})"),flush=True)
        finally:
            self.quit()
        return False

    def check_pdf(self):
        view = self.view
        source_before = input_path.read_bytes()
        checks = (input_path.parents[1] / "browser-pdf-host.js").read_text()
        fixture = (input_path.parent / "rich-host-review.json").read_text()
        def pdf_check(action, payload=""):
            evaluate(view, "window.pdfStep=null;(" + checks + ")(" + json.dumps(action) + "," + json.dumps(payload) + ").then(value=>window.pdfStep={value},error=>window.pdfStep={error:String(error)});'started'")
            until(view, "window.pdfStep !== null", "native PDF readback", 90)
            result = json.loads(evaluate(view, "JSON.stringify(pdfStep)"))
            assert "error" not in result, result
            return result["value"]
        payload = pdf_check("fixture", fixture)
        evaluate(view, "(()=>{const bytes=Uint8Array.from(atob(" + json.dumps(payload) + "),c=>c.charCodeAt(0));const files=new DataTransfer();files.items.add(new File([bytes],'Native PDF review.inklane'));const input=document.querySelector('#files');input.files=files.files;input.dispatchEvent(new Event('change',{bubbles:true}));return true})()")
        until(view,"document.querySelector('#name').textContent === 'Native PDF review' && !document.querySelector('#open').disabled","long PDF review fixture")
        evaluate(view,"document.querySelector('.marker').click();const title=document.querySelector('#note-title');title.value='Complete native review';title.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#close-card').click();'edited'")
        before = evaluate(view,"document.querySelector('#state').textContent")
        assert before == "Unsaved changes"
        self.pdf_selections = 0
        destination = output.with_name("Native PDF review-review.pdf")
        pdf_check("observe")
        try:
            evaluate(view,"document.querySelector('#export-pdf').click();'preparing'")
            until(view,"!document.querySelector('#export-pdf').disabled","cancelled PDF destination",90)
            assert self.pdf_selections == 1 and not destination.exists()
            assert evaluate(view,"document.querySelector('#state').textContent") == before
            evaluate(view,"document.querySelector('#export-pdf').click();'preparing'")
            until(view,"!document.querySelector('#export-pdf').disabled","complete PDF destination",90)
            assert self.pdf_selections == 2
            data = destination.read_bytes()
            assert data.startswith(b"%PDF-")
            expected = json.loads(evaluate(view,"JSON.stringify(nativePdfExpected)"))
            assert len(data) == expected["size"] and hashlib.sha256(data).hexdigest() == expected["hash"]
            rendered = pdf_check("inspect",base64.b64encode(data).decode("ascii"))
            assert rendered["pages"] >= 3 and rendered["green"] > 40, rendered
            assert all(abs(a-b) <= 15 for a,b in zip(rendered["pixel"][:3],[234,179,8])), rendered
            assert evaluate(view,"document.querySelector('#state').textContent") == before
            assert input_path.read_bytes() == source_before
            print("GTK native PDF cancellation, complete saved bytes, paginated long notes and source diagram pixels passed.")
        finally:
            pdf_check("restore")

    def check_renderer_recovery(self):
        view = self.view
        evaluate(view, "void webkit.messageHandlers.exportFile.postMessage({op:'begin',filename:'Partial.inklane',size:50}).then(t=>window.partialToken=t);'begun'")
        until(view,"typeof window.partialToken === 'string'","partial transfer started")
        partial = self.exports.pending["stream"]
        recovered = []
        def loaded(_, phase):
            if phase == WebKit2.LoadEvent.FINISHED: recovered.append(True)
        handler = view.connect("load-changed", loaded)
        try:
            view.terminate_web_process()
            end = monotonic()+15
            while not recovered and monotonic()<end: tick()
            assert recovered and self.exports.pending is None and partial.closed
            until(view,"typeof window.inklaneSaveCopy === 'function' && !!document.querySelector('#start')?.onclick","restarted workspace")
            recovered.clear()
            evaluate(view,"void window.inklaneSaveCopy(new Blob(['Fully staged review']), 'Complete.inklane');'started'")
            path = output.with_name("Complete.inklane")
            end = monotonic()+15
            while (not recovered or not path.exists()) and monotonic()<end: tick()
            assert recovered and path.read_bytes() == b"Fully staged review"
            until(view,"typeof window.inklaneSaveCopy === 'function' && !!document.querySelector('#start')?.onclick","second restored workspace")
            print("A terminated renderer returned to the library, released a partial transfer and preserved the completed file in an active native save.")
        finally:
            view.disconnect(handler)

    def choose_copy(self, name):
        if name.endswith(".pdf"):
            self.pdf_selections += 1
            return None if self.pdf_selections == 1 else output.with_name(name)
        if name == "Complete.inklane":
            # A native GTK dialog runs a nested main loop. Terminate the actual web process
            # while the complete payload is held for that destination decision.
            generation = self.renderer_generation
            self.view.terminate_web_process()
            end = monotonic()+10
            while self.renderer_generation == generation and monotonic()<end: tick()
            assert self.renderer_generation > generation and self.exports.presenting
            return output.with_name(name)
        # The real file writer receives an isolated path in place of the user's destination selection.
        assert name.endswith(".inklane")
        return output


if __name__ == "__main__":
    check_exports_without_hardlinks()
    input_path = Path(sys.argv[1]).resolve()
    output = Path(tempfile.mkdtemp(prefix="inklane-linux-test-")) / "Reviewed.inklane"
    app = Check()
    app.run(["inklane"])
    sys.exit(1 if app.failed else 0)
