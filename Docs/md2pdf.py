#!/usr/bin/env python3
"""Markdown -> styled HTML for headless Chrome to print.

Only handles the subset the store-page document uses: headings, tables, fenced
code, blockquotes, lists, rules, bold, inline code. Deliberately not a general
markdown parser.
"""
import html
import re
import sys
from pathlib import Path

FONT_DIR = Path("/home/user/MegaBonk/Assets/DownloadedFonts")


def inline(s: str) -> str:
    s = html.escape(s)
    s = re.sub(r"`([^`]+)`", r"<code>\1</code>", s)
    s = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", s)
    s = re.sub(r"(?<!\*)\*([^*]+)\*(?!\*)", r"<em>\1</em>", s)
    s = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', s)
    return s


def cells(line: str):
    return [c.strip() for c in line.strip().strip("|").split("|")]


def convert(md: str) -> str:
    lines = md.split("\n")
    out = []
    i = 0
    n = len(lines)

    while i < n:
        line = lines[i]

        # fenced code
        if line.startswith("```"):
            i += 1
            buf = []
            while i < n and not lines[i].startswith("```"):
                buf.append(lines[i])
                i += 1
            i += 1
            out.append("<pre><code>" + html.escape("\n".join(buf)) + "</code></pre>")
            continue

        # table: a header row followed by a |---| separator
        if line.strip().startswith("|") and i + 1 < n and re.match(
            r"^\s*\|[\s:|-]+\|\s*$", lines[i + 1]
        ):
            head = cells(line)
            i += 2
            rows = []
            while i < n and lines[i].strip().startswith("|"):
                rows.append(cells(lines[i]))
                i += 1
            t = ["<table><thead><tr>"]
            t += [f"<th>{inline(c)}</th>" for c in head]
            t.append("</tr></thead><tbody>")
            for r in rows:
                t.append("<tr>" + "".join(f"<td>{inline(c)}</td>" for c in r) + "</tr>")
            t.append("</tbody></table>")
            out.append("".join(t))
            continue

        # headings
        m = re.match(r"^(#{1,4})\s+(.*)$", line)
        if m:
            lvl = len(m.group(1))
            out.append(f"<h{lvl}>{inline(m.group(2))}</h{lvl}>")
            i += 1
            continue

        # rule
        if re.match(r"^-{3,}\s*$", line):
            out.append("<hr>")
            i += 1
            continue

        # blockquote
        if line.startswith(">"):
            buf = []
            while i < n and lines[i].startswith(">"):
                buf.append(lines[i].lstrip(">").strip())
                i += 1
            # a blank quoted line separates paragraphs inside the quote
            paras = "\n".join(buf).split("\n\n")
            body = "".join(
                f"<p>{inline(' '.join(p.split()))}</p>" for p in paras if p.strip()
            )
            out.append(f"<blockquote>{body}</blockquote>")
            continue

        # ordered list
        if re.match(r"^\d+\.\s+", line):
            items = []
            while i < n and re.match(r"^\d+\.\s+", lines[i]):
                items.append(re.sub(r"^\d+\.\s+", "", lines[i]))
                i += 1
                # continuation lines are indented
                while i < n and lines[i].startswith("   ") and lines[i].strip():
                    items[-1] += " " + lines[i].strip()
                    i += 1
            out.append("<ol>" + "".join(f"<li>{inline(x)}</li>" for x in items) + "</ol>")
            continue

        # unordered list
        if re.match(r"^[-*]\s+", line):
            items = []
            while i < n and re.match(r"^[-*]\s+", lines[i]):
                items.append(re.sub(r"^[-*]\s+", "", lines[i]))
                i += 1
                while i < n and lines[i].startswith("  ") and lines[i].strip():
                    items[-1] += " " + lines[i].strip()
                    i += 1
            out.append("<ul>" + "".join(f"<li>{inline(x)}</li>" for x in items) + "</ul>")
            continue

        # blank
        if not line.strip():
            i += 1
            continue

        # paragraph
        buf = []
        while i < n and lines[i].strip() and not re.match(
            r"^(#{1,4}\s|```|\||>|[-*]\s|\d+\.\s|-{3,}\s*$)", lines[i]
        ):
            buf.append(lines[i].strip())
            i += 1
        out.append(f"<p>{inline(' '.join(buf))}</p>")

    return "\n".join(out)


CSS = """
@font-face { font-family: 'Cinzel'; font-weight: 700;
  src: url('file://__FD__/Cinzel/static/Cinzel-Bold.ttf'); }
@font-face { font-family: 'Montserrat'; font-weight: 400;
  src: url('file://__FD__/Montserrat/static/Montserrat-Regular.ttf'); }
@font-face { font-family: 'Montserrat'; font-weight: 600;
  src: url('file://__FD__/Montserrat/static/Montserrat-SemiBold.ttf'); }
@font-face { font-family: 'Montserrat'; font-weight: 700;
  src: url('file://__FD__/Montserrat/static/Montserrat-Bold.ttf'); }

:root {
  --ink:      #1b1917;
  --muted:    #6b635c;
  --rule:     #ddd6cd;
  --accent:   #8a2b12;   /* the ember in the trailer's statue shot */
  --panel:    #f6f2eb;
  --code-bg:  #f1ece3;
}

@page { size: A4; margin: 17mm 16mm 18mm 16mm; }

* { box-sizing: border-box; }

body {
  font-family: 'Montserrat', 'DejaVu Sans', sans-serif;
  font-size: 9.6pt;
  line-height: 1.55;
  color: var(--ink);
  margin: 0;
  -webkit-font-smoothing: antialiased;
}

h1 {
  font-family: 'Cinzel', 'DejaVu Serif', serif;
  font-weight: 700;
  font-size: 26pt;
  letter-spacing: 0.02em;
  margin: 0 0 2mm;
  color: var(--ink);
}
h1 + p { color: var(--muted); font-size: 10.5pt; margin-top: 0; }

h2 {
  font-size: 13pt; font-weight: 700; letter-spacing: -0.01em;
  margin: 9mm 0 2.5mm; padding-bottom: 1.6mm;
  border-bottom: 2px solid var(--accent);
  break-after: avoid; page-break-after: avoid;
}
h3 {
  font-size: 10.6pt; font-weight: 700; margin: 6mm 0 1.5mm;
  color: var(--accent);
  break-after: avoid; page-break-after: avoid;
}

p { margin: 0 0 2.6mm; }
strong { font-weight: 700; }

hr { border: 0; border-top: 1px solid var(--rule); margin: 7mm 0; }

ul, ol { margin: 0 0 3mm; padding-left: 5.5mm; }
li { margin-bottom: 1.1mm; }

blockquote {
  margin: 0 0 3.5mm; padding: 3mm 4mm;
  background: var(--panel);
  border-left: 3px solid var(--accent);
  break-inside: avoid; page-break-inside: avoid;
}
blockquote p { margin: 0 0 2mm; }
blockquote p:last-child { margin-bottom: 0; }

code {
  font-family: 'DejaVu Sans Mono', monospace;
  font-size: 0.86em;
  background: var(--code-bg);
  padding: 0.3mm 1mm;
  border-radius: 1.2mm;
}

pre {
  background: var(--code-bg);
  border: 1px solid var(--rule);
  border-radius: 1.5mm;
  padding: 3.5mm 4mm;
  margin: 0 0 4mm;
  white-space: pre-wrap;
  word-wrap: break-word;
  break-inside: auto; page-break-inside: auto;
}
pre code {
  background: none; padding: 0; font-size: 8.3pt; line-height: 1.5;
}

table {
  width: 100%; border-collapse: collapse; margin: 0 0 4mm;
  font-size: 8.8pt;
  break-inside: avoid; page-break-inside: avoid;
}
thead th {
  text-align: left; font-weight: 700; font-size: 7.6pt;
  text-transform: uppercase; letter-spacing: 0.06em;
  color: var(--muted);
  border-bottom: 1.5px solid var(--ink);
  padding: 1.6mm 2.5mm 1.6mm 0;
}
tbody td {
  padding: 1.6mm 2.5mm 1.6mm 0;
  border-bottom: 1px solid var(--rule);
  vertical-align: top;
}
tbody tr:last-child td { border-bottom: 0; }
td code { font-size: 0.85em; }

a { color: var(--accent); text-decoration: none; }
""".replace("__FD__", str(FONT_DIR))


def main():
    src = Path(sys.argv[1])
    dst = Path(sys.argv[2])
    body = convert(src.read_text(encoding="utf-8"))
    dst.write_text(
        "<!doctype html><html lang='uk'><head><meta charset='utf-8'>"
        "<title>Hollow Siege — Steam store page</title>"
        f"<style>{CSS}</style></head><body>{body}</body></html>",
        encoding="utf-8",
    )
    print(f"wrote {dst}")


if __name__ == "__main__":
    main()
