"""Capture the simulator screen while the hosted test is displaying the bundled example."""
from pathlib import Path
import subprocess
import sys
import time

device, output = sys.argv[1:]
deadline = time.monotonic() + 1200
while time.monotonic() < deadline:
    bundle = subprocess.run(["xcrun", "simctl", "get_app_container", device, "io.github.dwolken.Inklane", "data"],
                            capture_output=True, text=True)
    if bundle.returncode == 0:
        cache = Path(bundle.stdout.strip()) / "Library/Caches"
        cache.mkdir(parents=True, exist_ok=True)
        (cache / "Inklane-screen.armed").touch()
        if (cache / "Inklane-screen.ready").is_file():
            subprocess.run(["xcrun", "simctl", "io", device, "screenshot", output], check=True, timeout=35)
            (cache / "Inklane-screen.done").touch()
            sys.exit(0)
    time.sleep(0.5)
raise SystemExit("The example workspace never requested its simulator screenshot.")
