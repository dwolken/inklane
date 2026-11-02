"""Exercise the installed GTK/WebKit application under a disposable X11 display."""
from pathlib import Path
from time import monotonic, sleep
import json
import subprocess
import sys
import tempfile
import traceback

sys.path.insert(0, "/usr/share/inklane")
from inklane import Inklane, Gtk, GLib
import gi
gi.require_version("GdkX11","3.0")
from gi.repository import GdkX11

input_path = Path(sys.argv[1]).resolve()
output = Path(tempfile.mkdtemp(prefix="inklane-linux-test-")) / "Reviewed.inklane"


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


def until(view, condition, label):
    end = monotonic() + 25
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
            print("Installed GTK host opened a portable notebook through its file chooser, edited a pinned note, saved it through native file transfer, and reopened the saved content.")
        except BaseException:
            self.failed=True
            traceback.print_exc()
            print("Startup state:", evaluate(self.view, "JSON.stringify({path:location.pathname,state:document.readyState,bridge:typeof window.inklaneSaveCopy,start:typeof document.querySelector('#start')?.onclick,status:document.querySelector('#state')?.textContent,secure:isSecureContext})"),flush=True)
        finally:
            self.quit()
        return False

    def choose_copy(self, name):
        # The real file writer receives an isolated path in place of the user's destination selection.
        assert name.endswith(".inklane")
        return output


app=Check()
app.run(["inklane"])
sys.exit(1 if app.failed else 0)
