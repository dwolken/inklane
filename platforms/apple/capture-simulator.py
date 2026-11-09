"""Capture the simulator screen while the hosted test is displaying the bundled example."""
from pathlib import Path
import subprocess
import sys
import time

def capture(device, output, run=subprocess.run):
    """Publish only a complete compositor PNG, with two bounded attempts."""
    output = Path(output)
    failure = None
    for attempt in range(1, 3):
        candidate = output.with_name(output.name + f".attempt-{attempt}.png")
        try:
            run(["xcrun", "simctl", "io", device, "screenshot", str(candidate)], check=True, timeout=60)
            with candidate.open("rb") as image:
                header = image.read(24)
                image.seek(-12, 2)
                end = image.read(12)
            if (len(header) != 24 or header[:16] != b"\x89PNG\r\n\x1a\n\0\0\0\rIHDR"
                    or int.from_bytes(header[16:20], "big") < 1
                    or int.from_bytes(header[20:24], "big") < 1
                    or end != b"\0\0\0\0IEND\xaeB`\x82"):
                raise ValueError("CoreSimulator did not produce a complete PNG with valid dimensions")
            candidate.replace(output)
            return
        except (subprocess.TimeoutExpired, subprocess.CalledProcessError, OSError, ValueError) as error:
            failure = error
            print(f"Simulator capture attempt {attempt}/2 failed: {error}", flush=True)
        finally:
            candidate.unlink(missing_ok=True)
    raise RuntimeError("Both simulator compositor capture attempts failed") from failure


def main(device, output):
    deadline = time.monotonic() + 1200
    while time.monotonic() < deadline:
        bundle = subprocess.run(["xcrun", "simctl", "get_app_container", device, "io.github.dwolken.Inklane", "data"],
                                capture_output=True, text=True)
        if bundle.returncode == 0:
            cache = Path(bundle.stdout.strip()) / "Library/Caches"
            cache.mkdir(parents=True, exist_ok=True)
            (cache / "Inklane-screen.armed").touch()
            # Poll the local handshake files, leaving CoreSimulator alone while Xcode
            # launches the app. Reacquire the container if a reinstall replaces it.
            while cache.is_dir() and time.monotonic() < deadline:
                if (cache / "Inklane-screen.ready").is_file():
                    capture(device, output)
                    (cache / "Inklane-screen.done").touch()
                    return
                time.sleep(0.25)
        time.sleep(2)
    raise SystemExit("The example workspace never requested its simulator screenshot.")


if __name__ == "__main__":
    main(*sys.argv[1:])
