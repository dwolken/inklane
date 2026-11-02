"""Print only the exception and crashing thread from a hosted Inklane test failure."""
import json
import os
from pathlib import Path
import subprocess
import sys

result, products = map(Path, sys.argv[1:])
tmp = Path(os.environ["RUNNER_TEMP"])
export = tmp / "inklane-crash-diagnostics"
if result.is_dir():
    subprocess.run(["xcrun", "xcresulttool", "export", "diagnostics", "--path", str(result),
                    "--output-path", str(export)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

roots = [Path.home() / "Library/Logs/DiagnosticReports", export]
candidates = [path for root in roots for path in root.rglob("Inklane*.ips")]
for path in sorted(candidates, key=lambda p: p.stat().st_mtime, reverse=True):
    try:
        # Apple writes a header JSON record, then the incident body.
        raw = path.read_text(encoding="utf-8")
        report, end = json.JSONDecoder().raw_decode(raw.lstrip())
        if "threads" not in report:
            report = json.loads(raw.lstrip()[end:])
        if report.get("procName") != "Inklane" or not report.get("threads"):
            continue
        threads = report["threads"]
        thread = next((t for t in threads if t.get("triggered")), threads[report.get("faultingThread", 0)])
    except (OSError, ValueError, IndexError, KeyError):
        continue
    error = report.get("exception", {})
    print("Inklane crashed:", error.get("type"), error.get("signal"))
    for frame in thread.get("frames", [])[:24]:
        image = report["usedImages"][frame.get("imageIndex", 0)]
        name = image.get("name", "unknown")
        symbol = frame.get("symbol")
        offset = frame.get("imageOffset", 0)
        if not symbol and name in {"Inklane", "InklaneTests"}:
            binary = next((p for p in (products / "Build/Products").rglob(name) if p.is_file()), None)
            if binary:
                base = image.get("base", 0)
                address = hex(base + offset)
                symbol = subprocess.run(["xcrun", "atos", "-arch", image["arch"], "-o", str(binary),
                                         "-l", hex(base), address], text=True, capture_output=True).stdout.strip()
        print(" ", name, symbol or f"+0x{offset:x}")
    break
else:
    print("No Inklane crash report was present on this runner.")
