"""
Generate PDF user manual from wiki markdown pages.
Usage: python generate_manual.py
Output: X3D-CCD-Inspector-User-Manual.pdf in the same directory
Requires: pip install markdown fpdf2
"""
import os
import re
import markdown
from fpdf import FPDF
from html.parser import HTMLParser


WIKI_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "wiki docs", "X3D-CCD-Optimizer.wiki")
OUTPUT_PDF = os.path.join(os.path.dirname(os.path.abspath(__file__)), "X3D-CCD-Inspector-User-Manual.pdf")
OUTPUT_HTML = os.path.join(os.path.dirname(os.path.abspath(__file__)), "X3D-CCD-Inspector-User-Manual.html")

# Pages in logical reading order
PAGES = [
    ("Home.md", "Introduction"),
    ("Getting-Started.md", "Getting Started"),
    ("How-It-Works.md", "How It Works"),
    ("AMD-X3D-Scheduling-Explained.md", "AMD X3D Scheduling Explained"),
    ("Settings-Explained.md", "Settings Explained"),
    ("Process-Rules.md", "Process Rules"),
    ("The-Overlay.md", "The Overlay"),
    ("AMD-V-Cache-Driver-Preference.md", "AMD V-Cache Driver Preference"),
    ("Installation.md", "Installation"),
    ("Troubleshooting.md", "Troubleshooting"),
    ("FAQ.md", "FAQ"),
]


def strip_wiki_links(text):
    """Convert [[Page Name]] wiki links to plain text."""
    return re.sub(r'\[\[([^\]]+)\]\]', r'\1', text)


def read_page(filename):
    path = os.path.join(WIKI_DIR, filename)
    with open(path, "r", encoding="utf-8") as f:
        return f.read()


def sanitize_latin1(text):
    """Make text safe for latin-1 encoding used by fpdf core fonts."""
    replacements = {
        "\u2014": "--", "\u2013": "-", "\u2018": "'", "\u2019": "'",
        "\u201c": '"', "\u201d": '"', "\u2022": "-", "\u2026": "...",
        "\u2192": "->", "\u2190": "<-", "\u26a0": "[!]",
        "\u00a0": " ", "\u200b": "", "\u2011": "-", "\u2012": "-",
        "\u2032": "'", "\u2033": '"', "\u2212": "-", "\u00d7": "x",
        "&mdash;": "--", "&ndash;": "-", "&amp;": "&",
        "&gt;": ">", "&lt;": "<",
        "&#x2192;": "->", "&#x26A0;": "[!]",
    }
    for k, v in replacements.items():
        text = text.replace(k, v)
    return text.encode("latin-1", errors="replace").decode("latin-1")


class ManualPDF(FPDF):
    """Custom PDF class with headers and footers."""

    def __init__(self):
        super().__init__()
        self.set_auto_page_break(auto=True, margin=20)

    def header(self):
        if self.page_no() > 2:  # Skip cover and TOC
            self.set_font("Helvetica", "I", 8)
            self.set_text_color(150, 150, 150)
            self.cell(0, 8, "X3D CCD Inspector - User Manual", align="R")
            self.ln(4)

    def footer(self):
        if self.page_no() > 1:  # Skip cover
            self.set_y(-15)
            self.set_font("Helvetica", "", 8)
            self.set_text_color(150, 150, 150)
            self.cell(0, 10, str(self.page_no()), align="C")

    def cover_page(self):
        self.add_page()
        self.ln(80)
        self.set_font("Helvetica", "B", 32)
        self.set_text_color(26, 26, 46)
        self.cell(0, 16, "X3D CCD Inspector", align="C", new_x="LMARGIN", new_y="NEXT")
        self.ln(4)
        self.set_font("Helvetica", "", 16)
        self.set_text_color(100, 100, 100)
        self.cell(0, 10, "User Manual", align="C", new_x="LMARGIN", new_y="NEXT")
        self.ln(30)
        self.set_font("Helvetica", "", 13)
        self.set_text_color(160, 160, 160)
        self.cell(0, 8, "Version 1.0.0", align="C", new_x="LMARGIN", new_y="NEXT")
        self.ln(40)
        self.set_font("Helvetica", "", 10)
        self.set_text_color(170, 170, 170)
        self.cell(0, 6, "AMD Ryzen Dual-CCD Scheduling Optimization", align="C", new_x="LMARGIN", new_y="NEXT")
        self.cell(0, 6, "Open Source - GPL v2", align="C", new_x="LMARGIN", new_y="NEXT")
        self.cell(0, 6, "github.com/LordBlacksun/x3d-ccd-optimizer", align="C", new_x="LMARGIN", new_y="NEXT")

    def toc_page(self, pages):
        self.add_page()
        self.set_font("Helvetica", "B", 22)
        self.set_text_color(26, 26, 46)
        self.cell(0, 14, "Table of Contents", new_x="LMARGIN", new_y="NEXT")
        self.ln(4)
        self.set_draw_color(231, 76, 60)
        self.line(self.l_margin, self.get_y(), self.w - self.r_margin, self.get_y())
        self.ln(10)
        for i, (_, title) in enumerate(pages, 1):
            self.set_font("Helvetica", "", 12)
            self.set_text_color(44, 62, 80)
            self.cell(0, 10, f"  {i}.  {title}", new_x="LMARGIN", new_y="NEXT")
            self.set_draw_color(200, 200, 200)
            y = self.get_y()
            self.line(self.l_margin + 4, y, self.w - self.r_margin, y)

    def chapter_title(self, title):
        self.add_page()
        self.set_font("Helvetica", "B", 22)
        self.set_text_color(26, 26, 46)
        self.cell(0, 14, sanitize_latin1(title), new_x="LMARGIN", new_y="NEXT")
        self.ln(2)
        self.set_draw_color(231, 76, 60)
        self.line(self.l_margin, self.get_y(), self.w - self.r_margin, self.get_y())
        self.ln(8)

    def section_title(self, title):
        self.ln(6)
        self.set_font("Helvetica", "B", 14)
        self.set_text_color(44, 62, 80)
        self.cell(0, 10, sanitize_latin1(title), new_x="LMARGIN", new_y="NEXT")
        self.set_draw_color(220, 220, 220)
        y = self.get_y()
        self.line(self.l_margin, y, self.w - self.r_margin, y)
        self.ln(4)

    def subsection_title(self, title):
        self.ln(4)
        self.set_font("Helvetica", "B", 12)
        self.set_text_color(52, 73, 94)
        self.cell(0, 8, sanitize_latin1(title), new_x="LMARGIN", new_y="NEXT")
        self.ln(2)

    def body_text(self, text):
        self.set_font("Helvetica", "", 10)
        self.set_text_color(34, 34, 34)
        text = sanitize_latin1(text.strip())
        if text:
            self.multi_cell(0, 5.5, text)
            self.ln(2)

    def bold_text(self, text):
        self.set_font("Helvetica", "B", 10)
        self.set_text_color(34, 34, 34)
        text = sanitize_latin1(text.strip())
        if text:
            self.multi_cell(0, 5.5, text)
            self.ln(2)

    def bullet_item(self, text, indent=0):
        self.set_font("Helvetica", "", 10)
        self.set_text_color(34, 34, 34)
        x = self.l_margin + 6 + indent
        self.set_x(x)
        bullet = "-  "
        self.multi_cell(self.w - self.r_margin - x, 5.5, bullet + sanitize_latin1(text))
        self.ln(1)

    def code_block(self, text):
        self.set_font("Courier", "", 9)
        self.set_text_color(50, 50, 50)
        self.set_fill_color(245, 245, 245)
        self.set_draw_color(220, 220, 220)
        x = self.l_margin + 4
        w = self.w - self.l_margin - self.r_margin - 8
        self.set_x(x)
        self.multi_cell(w, 4.5, sanitize_latin1(text), border=1, fill=True)
        self.ln(3)

    def blockquote(self, text):
        self.set_font("Helvetica", "I", 10)
        self.set_text_color(85, 85, 85)
        self.set_fill_color(253, 246, 246)
        self.set_draw_color(231, 76, 60)
        x = self.l_margin + 6
        w = self.w - self.l_margin - self.r_margin - 12
        old_x = self.get_x()
        # Draw left border
        y_start = self.get_y()
        self.set_x(x)
        self.multi_cell(w, 5.5, sanitize_latin1(text), fill=True)
        y_end = self.get_y()
        self.line(x - 2, y_start, x - 2, y_end)
        self.ln(3)

    def render_table(self, headers, rows):
        """Render a table with headers and rows."""
        col_count = len(headers)
        available_w = self.w - self.l_margin - self.r_margin
        col_w = available_w / col_count

        # Adjust column widths based on content
        if col_count == 2:
            col_widths = [available_w * 0.35, available_w * 0.65]
        elif col_count == 3:
            col_widths = [available_w * 0.22, available_w * 0.25, available_w * 0.53]
        else:
            col_widths = [col_w] * col_count

        # Header row
        self.set_font("Helvetica", "B", 9)
        self.set_fill_color(44, 62, 80)
        self.set_text_color(255, 255, 255)
        for i, h in enumerate(headers):
            self.cell(col_widths[i], 7, sanitize_latin1(f" {h}"), border=1, fill=True)
        self.ln()

        # Data rows
        self.set_font("Helvetica", "", 9)
        for row_idx, row in enumerate(rows):
            self.set_fill_color(248, 249, 250) if row_idx % 2 == 0 else self.set_fill_color(255, 255, 255)
            self.set_text_color(34, 34, 34)
            fill = row_idx % 2 == 0

            # Calculate max height needed
            max_lines = 1
            for i, cell in enumerate(row):
                w = col_widths[i] if i < len(col_widths) else col_w
                lines = max(1, len(cell) * self.get_string_width("x") / (w - 2) + 0.5)
                max_lines = max(max_lines, int(lines))

            row_h = max(7, max_lines * 4.5)

            # Check if we need a page break
            if self.get_y() + row_h > self.h - 25:
                self.add_page()
                # Reprint header
                self.set_font("Helvetica", "B", 9)
                self.set_fill_color(44, 62, 80)
                self.set_text_color(255, 255, 255)
                for i, h in enumerate(headers):
                    self.cell(col_widths[i], 7, sanitize_latin1(f" {h}"), border=1, fill=True)
                self.ln()
                self.set_font("Helvetica", "", 9)
                self.set_text_color(34, 34, 34)

            x_start = self.get_x()
            y_start = self.get_y()
            max_y = y_start

            for i, cell in enumerate(row):
                w = col_widths[i] if i < len(col_widths) else col_w
                self.set_xy(x_start + sum(col_widths[:i]), y_start)
                self.multi_cell(w, 4.5, sanitize_latin1(f" {cell}"), border="LR", fill=fill)
                max_y = max(max_y, self.get_y())

            # Draw bottom border for each cell
            for i in range(col_count):
                w = col_widths[i] if i < len(col_widths) else col_w
                x = x_start + sum(col_widths[:i])
                # Fill remaining space
                if self.get_y() < max_y:
                    pass
                self.line(x, max_y, x + w, max_y)

            self.set_y(max_y)

        self.ln(4)


class MarkdownRenderer:
    """Parse markdown and render to ManualPDF."""

    def __init__(self, pdf):
        self.pdf = pdf

    def render_page(self, md_content, chapter_title):
        """Render a markdown page to the PDF."""
        self.pdf.chapter_title(chapter_title)
        lines = md_content.split("\n")
        i = 0
        while i < len(lines):
            line = lines[i]

            # Skip the first H1 (it's the chapter title, already rendered)
            if line.startswith("# ") and i == 0:
                i += 1
                continue

            # H2
            if line.startswith("## "):
                self.pdf.section_title(line[3:].strip())
                i += 1
                continue

            # H3
            if line.startswith("### "):
                self.pdf.subsection_title(line[4:].strip())
                i += 1
                continue

            # H4
            if line.startswith("#### "):
                self.pdf.subsection_title(line[5:].strip())
                i += 1
                continue

            # Table
            if "|" in line and i + 1 < len(lines) and re.match(r'\s*\|[\s\-|:]+\|', lines[i + 1]):
                headers, rows, i = self._parse_table(lines, i)
                if headers:
                    self.pdf.render_table(headers, rows)
                continue

            # Code block
            if line.strip().startswith("```"):
                code_lines = []
                i += 1
                while i < len(lines) and not lines[i].strip().startswith("```"):
                    code_lines.append(lines[i])
                    i += 1
                i += 1  # skip closing ```
                if code_lines:
                    self.pdf.code_block("\n".join(code_lines))
                continue

            # Blockquote
            if line.startswith("> "):
                quote_lines = []
                while i < len(lines) and (lines[i].startswith("> ") or lines[i].startswith(">")):
                    quote_lines.append(lines[i].lstrip("> ").strip())
                    i += 1
                self.pdf.blockquote(" ".join(quote_lines))
                continue

            # Bullet list
            if re.match(r'^[-*] ', line.strip()):
                text = self._clean_inline(line.strip()[2:].strip())
                indent = len(line) - len(line.lstrip())
                self.pdf.bullet_item(text, indent=indent)
                i += 1
                continue

            # Numbered list
            if re.match(r'^\d+\.\s', line.strip()):
                text = re.sub(r'^\d+\.\s*', '', line.strip())
                text = self._clean_inline(text)
                self.pdf.bullet_item(text)
                i += 1
                continue

            # Horizontal rule
            if re.match(r'^-{3,}$', line.strip()) or re.match(r'^\*{3,}$', line.strip()):
                self.pdf.ln(3)
                y = self.pdf.get_y()
                self.pdf.set_draw_color(200, 200, 200)
                self.pdf.line(self.pdf.l_margin, y, self.pdf.w - self.pdf.r_margin, y)
                self.pdf.ln(5)
                i += 1
                continue

            # Checklist items
            if re.match(r'^-\s*\[[ xX]\]', line.strip()):
                text = re.sub(r'^-\s*\[[ xX]\]\s*', '', line.strip())
                text = self._clean_inline(text)
                self.pdf.bullet_item(text)
                i += 1
                continue

            # Empty line
            if not line.strip():
                self.pdf.ln(2)
                i += 1
                continue

            # Regular paragraph
            para_lines = []
            while i < len(lines) and lines[i].strip() and not lines[i].startswith("#") and not lines[i].startswith("```") and not lines[i].startswith("> ") and not re.match(r'^[-*] ', lines[i].strip()) and not re.match(r'^\d+\.\s', lines[i].strip()) and not ("|" in lines[i] and i + 1 < len(lines) and re.match(r'\s*\|[\s\-|:]+\|', lines[i + 1])):
                para_lines.append(lines[i].strip())
                i += 1

            if para_lines:
                text = " ".join(para_lines)
                text = self._clean_inline(text)
                # Check if it starts with bold (like **Note:**)
                if text.startswith("Note:") or text.startswith("Important:"):
                    self.pdf.bold_text(text)
                else:
                    self.pdf.body_text(text)

    def _clean_inline(self, text):
        """Remove markdown inline formatting for plain text output."""
        # Remove bold
        text = re.sub(r'\*\*(.+?)\*\*', r'\1', text)
        # Remove italic
        text = re.sub(r'\*(.+?)\*', r'\1', text)
        # Remove inline code backticks
        text = re.sub(r'`(.+?)`', r'\1', text)
        # Remove links, keep text
        text = re.sub(r'\[([^\]]+)\]\([^\)]+\)', r'\1', text)
        # Remove HTML entities
        text = text.replace("&mdash;", "--").replace("&ndash;", "-")
        text = text.replace("&amp;", "&").replace("&gt;", ">").replace("&lt;", "<")
        text = text.replace("&#x2192;", "->").replace("&#x26A0;", "[!]")
        # Remove remaining HTML tags
        text = re.sub(r'<[^>]+>', '', text)
        # Replace Unicode characters that latin-1 can't encode
        replacements = {
            "\u2014": "--", "\u2013": "-", "\u2018": "'", "\u2019": "'",
            "\u201c": '"', "\u201d": '"', "\u2022": "-", "\u2026": "...",
            "\u2192": "->", "\u2190": "<-", "\u26a0": "[!]",
            "\u00a0": " ", "\u200b": "", "\u2011": "-", "\u2012": "-",
        }
        for k, v in replacements.items():
            text = text.replace(k, v)
        # Encode for fpdf (latin-1 safe)
        text = text.encode("latin-1", errors="replace").decode("latin-1")
        return text

    def _parse_table(self, lines, start):
        """Parse a markdown table starting at line index start."""
        headers = []
        rows = []

        # Header row
        header_line = lines[start].strip().strip("|")
        headers = [self._clean_inline(h.strip()) for h in header_line.split("|")]

        # Skip separator
        sep_idx = start + 1
        if sep_idx >= len(lines):
            return headers, rows, sep_idx + 1

        # Data rows
        i = sep_idx + 1
        while i < len(lines) and "|" in lines[i] and lines[i].strip():
            row_line = lines[i].strip().strip("|")
            cells = [self._clean_inline(c.strip()) for c in row_line.split("|")]
            # Pad or trim to match header count
            while len(cells) < len(headers):
                cells.append("")
            cells = cells[:len(headers)]
            rows.append(cells)
            i += 1

        return headers, rows, i


def build_html():
    """Build an HTML version of the manual for reference."""
    md = markdown.Markdown(extensions=["tables", "fenced_code"])
    css = """
    body { font-family: 'Segoe UI', sans-serif; max-width: 800px; margin: 40px auto; padding: 0 20px; color: #222; line-height: 1.6; }
    h1 { color: #1a1a2e; border-bottom: 2px solid #e74c3c; padding-bottom: 6px; page-break-before: always; }
    h2 { color: #2c3e50; border-bottom: 1px solid #ddd; }
    h3 { color: #34495e; }
    table { border-collapse: collapse; width: 100%; margin: 12px 0; }
    th { background: #2c3e50; color: white; padding: 8px 10px; text-align: left; }
    td { border: 1px solid #ddd; padding: 6px 10px; }
    tr:nth-child(even) { background: #f8f9fa; }
    code { background: #f0f0f0; padding: 1px 4px; border-radius: 3px; font-family: Consolas, monospace; }
    pre { background: #f5f5f5; border: 1px solid #ddd; padding: 10px; overflow-x: auto; }
    blockquote { border-left: 3px solid #e74c3c; margin-left: 0; padding: 8px 16px; background: #fdf6f6; color: #555; }
    a { color: #2980b9; }
    .cover { text-align: center; padding: 100px 0; page-break-after: always; }
    .cover h1 { border: none; font-size: 2.5em; }
    """
    parts = [f"<html><head><meta charset='utf-8'><style>{css}</style></head><body>"]
    parts.append("<div class='cover'><h1>X3D CCD Inspector</h1><p>User Manual</p><p>Version 1.0.0-beta</p></div>")

    for filename, title in PAGES:
        content = read_page(filename)
        content = strip_wiki_links(content)
        md.reset()
        parts.append(md.convert(content))

    parts.append("</body></html>")
    return "\n".join(parts)


def main():
    print("Building PDF user manual...")

    # Generate HTML reference
    html = build_html()
    with open(OUTPUT_HTML, "w", encoding="utf-8") as f:
        f.write(html)
    print(f"  HTML reference written to {OUTPUT_HTML}")

    # Generate PDF
    pdf = ManualPDF()
    renderer = MarkdownRenderer(pdf)

    # Cover page
    pdf.cover_page()

    # Table of contents
    pdf.toc_page(PAGES)

    # Content pages
    for filename, title in PAGES:
        content = read_page(filename)
        content = strip_wiki_links(content)
        renderer.render_page(content, title)

    pdf.output(OUTPUT_PDF)
    size_kb = os.path.getsize(OUTPUT_PDF) / 1024
    print(f"  PDF written to {OUTPUT_PDF} ({size_kb:.0f} KB)")
    print("Done.")


if __name__ == "__main__":
    main()
