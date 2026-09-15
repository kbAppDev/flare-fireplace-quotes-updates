"""Rebuild the existing Render-compatible seven-chunk static site package.

Run from any directory: python3 bubblelab/package.py
Only application files are shipped; QA and user uploads are never included.
"""
import base64
import io
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parent
source = root / "source" / "BubbleLab"
files = ["app.js", "styles.css", "index.html", "sw.js", "manifest.webmanifest", "README.md",
         "icons/icon-180.png", "icons/icon-192.png", "icons/icon-512.png"]
archive = io.BytesIO()
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for name in files:
        info = zipfile.ZipInfo("BubbleLab/" + name, (2026, 9, 15, 0, 0, 0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o100644 << 16
        z.writestr(info, (source / name).read_bytes())
encoded = base64.b64encode(archive.getvalue()).decode("ascii")
size = ((len(encoded) + 27) // 28) * 4
for i in range(7):
    (root / f"chunk{i+1:02d}.txt").write_text(encoded[i*size:(i+1)*size], encoding="ascii")
rebuilt = base64.b64decode("".join((root / f"chunk{i+1:02d}.txt").read_text() for i in range(7)))
assert rebuilt == archive.getvalue()
with zipfile.ZipFile(io.BytesIO(rebuilt)) as z:
    assert len(z.namelist()) == len(files)
    assert z.testzip() is None
    for name in files:
        assert z.read("BubbleLab/" + name) == (source / name).read_bytes()
print(f"Verified {len(files)} application files in seven chunks ({len(rebuilt)} ZIP bytes).")
