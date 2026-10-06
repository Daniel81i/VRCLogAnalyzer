"""
README.md から配布物に同梱する README.pdf を作る。

    pip install markdown
    python tools/make_readme_pdf.py [出力先.pdf]

Markdown を HTML に変換し、Microsoft Edge（無ければ Google Chrome）のヘッドレス印刷で PDF にする。
画像（docs/img/...）を README と同じ相対パスで読み込むため、一時 HTML はリポジトリ直下に作って最後に消す。
日本語フォントが入っていない環境（GitHub Actions の Windows ランナーなど）でも文字化けしないよう、
Noto Sans JP を Web フォントとして読み込む。
"""
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

import markdown

ROOT = Path(__file__).resolve().parent.parent

CSS = """
@page { size: A4; margin: 16mm 14mm; }
body { font-family: "Noto Sans JP", "Yu Gothic", "Meiryo", sans-serif; font-size: 10.5pt; line-height: 1.7; color: #222; }
h1 { font-size: 20pt; border-bottom: 2px solid #4a5be0; padding-bottom: 4px; }
h2 { font-size: 14pt; border-bottom: 1px solid #ccd; padding-bottom: 2px; margin-top: 1.6em; }
h3 { font-size: 12pt; margin-top: 1.2em; }
img { max-width: 100%; }
table { border-collapse: collapse; margin: 0.6em 0; }
th, td { border: 1px solid #bbc; padding: 3px 8px; vertical-align: top; }
th { background: #eef0fb; }
code { font-family: Consolas, "Noto Sans JP", monospace; background: #f3f3f6; padding: 0 3px; }
pre { background: #f3f3f6; padding: 8px; white-space: pre-wrap; }
a { color: #2a5bd7; text-decoration: none; }
"""

TEMPLATE = """<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><title>VRCLogAnalyzer README</title>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Noto+Sans+JP:wght@400;700&display=block">
<style>{css}</style></head>
<body>{body}</body></html>
"""


def find_browser() -> str:
    candidates = [
        os.path.expandvars(r"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
        os.path.expandvars(r"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe"),
        os.path.expandvars(r"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
        shutil.which("msedge") or "",
        shutil.which("chrome") or "",
    ]
    for c in candidates:
        if c and Path(c).exists():
            return c
    sys.exit("Microsoft Edge / Google Chrome が見つかりません")


def main() -> None:
    out = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else ROOT / "README.pdf"
    body = markdown.markdown((ROOT / "README.md").read_text(encoding="utf-8"),
                             extensions=["tables", "fenced_code", "sane_lists"])
    html = ROOT / "_readme_print.html"
    html.write_text(TEMPLATE.format(css=CSS, body=body), encoding="utf-8")
    try:
        with tempfile.TemporaryDirectory() as profile:
            subprocess.run([
                find_browser(), "--headless", "--disable-gpu", "--no-first-run",
                f"--user-data-dir={profile}",
                "--no-pdf-header-footer",
                # Web フォントと画像の読み込みを待ってから印刷する
                "--virtual-time-budget=15000",
                f"--print-to-pdf={out}",
                html.as_uri(),
            ], check=True, timeout=120, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    finally:
        html.unlink(missing_ok=True)
    if not out.exists() or out.stat().st_size == 0:
        sys.exit("PDF の作成に失敗しました")
    print(f"wrote {out} ({out.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
