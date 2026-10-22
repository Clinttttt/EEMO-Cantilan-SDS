"""Print only synthetic, production-component HTML created by NpmExportAcceptanceTests.

No server is started. Chrome gets a private temporary profile and cannot resolve network hosts.
This verifies export artifacts, not authenticated Head/MAUI interaction or official RCD policy.
"""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import fitz

directory = Path(tempfile.gettempdir()) / "stalltrack-npm-acceptance"
chrome = Path(os.environ["ProgramFiles"]) / "Google/Chrome/Application/chrome.exe"
results = []
for name in ["october-as-known", "october-latest-corrected"]:
    html = directory / f"{name}.html"
    pdf = directory / f"{name}.pdf"
    expected = json.loads((directory / f"{name}.json").read_text(encoding="utf-8"))
    assert expected["authenticated"] is False and expected["rows"] == 1000
    started = time.perf_counter()
    with tempfile.TemporaryDirectory(prefix="stalltrack-npm-print-") as profile:
        subprocess.run([str(chrome), "--headless=new", f"--user-data-dir={profile}",
            "--disable-background-networking", "--host-resolver-rules=MAP * ~NOTFOUND",
            "--no-pdf-header-footer", f"--print-to-pdf={pdf}", html.as_uri()],
            check=True, timeout=120, capture_output=True)
    with fitz.open(pdf) as document:
        text = "\n".join(page.get_text() for page in document)
        assert "SYNTHETIC TEST OFFICE" in text
        assert f"{expected['collected']:,.2f}" in text
        assert f"{expected['outstanding']:,.2f}" in text
        assert all(text.count(f"EXPORT-{i:04d}") == 1 for i in range(1, 1001)), "Lost or repeated record"
        outside = []
        for page in document:
            assert abs(page.rect.width - 841.89) < 2 and abs(page.rect.height - 595.28) < 2
            outside.extend(word for word in page.get_text("words")
                if word[0] < -1 or word[1] < -1 or word[2] > page.rect.width + 1 or word[3] > page.rect.height + 1)
        assert not outside, "Text extends beyond paper boundaries"
        for index in sorted({0, 1, len(document) - 1}):
            document[index].get_pixmap(matrix=fitz.Matrix(1.3, 1.3)).save(directory / f"{name}-page-{index+1}.png")
        results.append({"file": pdf.name, "pages": len(document), "bytes": pdf.stat().st_size,
            "renderMs": round(1000 * (time.perf_counter() - started)), "rowsVerified": 1000,
            "collected": expected["collected"], "outstanding": expected["outstanding"],
            "offPageWords": len(outside), "paper": "A4 landscape", "authenticated": False})
(directory / "pdf-verification.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(json.dumps(results, indent=2))
