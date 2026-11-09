"""Capture timeout and invalid output must never produce a success artifact."""
import base64
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("capture_simulator", Path(__file__).with_name("capture-simulator.py"))
capture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(capture)
PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jS6YAAAAASUVORK5CYII=")


class CaptureTests(unittest.TestCase):
    def test_timeout_partial_file_is_discarded_before_success(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "screen.png"
            calls = []
            def run(command, *, check, timeout):
                self.assertTrue(check)
                self.assertEqual(timeout, 60)
                calls.append(command)
                Path(command[-1]).write_bytes(b"partial" if len(calls) == 1 else PNG)
                if len(calls) == 1:
                    raise subprocess.TimeoutExpired(command, timeout)
            capture.capture("disposable-device", output, run)
            self.assertEqual(output.read_bytes(), PNG)
            self.assertEqual(len(calls), 2)
            self.assertEqual(list(output.parent.iterdir()), [output])

    def test_incomplete_png_fails_both_attempts_without_publishing(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "screen.png"
            calls = []
            def run(command, **options):
                calls.append(command)
                Path(command[-1]).write_bytes(PNG[:24])
            with self.assertRaisesRegex(RuntimeError, "Both simulator"):
                capture.capture("disposable-device", output, run)
            self.assertEqual(len(calls), 2)
            self.assertFalse(output.exists())
            self.assertEqual(list(output.parent.iterdir()), [])


if __name__ == "__main__":
    unittest.main()
