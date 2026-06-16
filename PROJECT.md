# Docs2MD

**Tagline:** Convert documents. View wireframes. Export anywhere.

---

## What is Docs2MD?

Docs2MD is a Windows desktop utility built for professionals who work with documents and Markdown. It does three things:

1. **PDF / DOCX → Markdown converter** — drag-and-drop PDF or Word files onto the app and it converts them to clean Markdown text, including OCR support for scanned pages.

2. **Wireframe Viewer** — open any `.md` file that contains ASCII/Unicode box-drawing wireframes and render it as a styled visual diagram. Saves as PNG or PDF.

3. **MD Viewer & Exporter** — open any Markdown file to view it as a formatted document, then export it to Word (.docx), Excel (.xlsx), or PDF with one click.

It is a standalone `.exe` file — no installer, no dependencies, no internet required.

---

## Key features

- Drag-and-drop PDF and DOCX files for batch conversion
- OCR support for scanned PDFs (powered by Tesseract)
- Wireframe rendering: ASCII box-drawing characters become styled UI diagrams
- Markdown viewer with smart element highlighting (buttons, dropdowns, status badges)
- Export to Word, Excel, and PDF from any Markdown file
- Fully offline — works without an internet connection

---

## Target users

- Business analysts and product managers who write wireframes in Markdown
- Developers who want to convert legacy Word/PDF documents into Markdown for use in wikis or repos
- Technical writers who need to repurpose content across multiple formats

---

## Technology

- .NET 8 WinForms (Windows desktop)
- Embedded Chromium browser (WebView2) for rendering
- Tesseract OCR, PDFium, Markdig, OpenXML

---

## Current logo concept

A logo concept has already been created (see `logo.svg` in this folder). The design uses:

- A **navy rounded square** as the icon background
- A **white document shape** with a folded top-right corner inside it
- A **teal `#` symbol** (the Markdown hash — the most recognisable Markdown character) overlaid on the document
- A small **green "MD" badge** anchored at the bottom-right of the document
- The wordmark **"Docs2MD"** in the same navy, with the `2` highlighted in teal to signal the conversion concept

---

## Brand colours

| Role        | Hex       | Description                          |
|-------------|-----------|--------------------------------------|
| Navy        | `#1b3560` | Primary — icon background, text      |
| Teal        | `#00a896` | Accent — hash symbol, "2" in wordmark |
| Green       | `#2e7d32` | Output/success — "MD" badge          |
| Paper       | `#f4f7fb` | Document surface                     |

---

## Logo brief (for a designer)

The logo should communicate **transformation** — taking messy document formats (PDF, DOCX) and turning them into clean, structured Markdown.

Key visual ideas to explore:
- The **`#` (hash) symbol** as the primary mark — it is universally recognised as the Markdown heading character
- A **document with a fold** — classic representation of a file/document
- The word **"Docs2MD"** — the `2` functions as both the number two and the word "to", so it can be styled as an arrow or accent
- Conversion / transformation metaphor — two shapes becoming one, an arrow, before/after

The tone is **professional and technical**, not playful. Think developer tools, not consumer apps. Clean, flat, no gradients or shadows.

---

## Files in this project

| File                 | Purpose                                  |
|----------------------|------------------------------------------|
| `logo.svg`           | Current logo concept (icon + wordmark)   |
| `Docs2MD.exe`        | The compiled application (in `dist/`)    |
| `MainForm.cs`        | Main application window                  |
| `WireframeViewerForm.cs` | Wireframe rendering form             |
| `MdViewerForm.cs`    | Markdown viewer and exporter form        |
| `MdExporter.cs`      | Word and Excel export logic              |
