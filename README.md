# Docs2MD — PDF / DOCX → Markdown (Windows app)

.NET 8 WinForms app. Converts Word documents and PDFs (including scanned PDFs via OCR) to Markdown.

## How it works

| Input | Pipeline |
|-------|----------|
| `.docx` | Mammoth (docx → semantic HTML) → ReverseMarkdown (HTML → MD). Headings, lists, tables, links preserved. |
| `.pdf` (text) | PdfPig extracts words → lines/paragraph grouping → headings inferred from font size vs body median. |
| `.pdf` (scanned) | Docnet/PDFium rasterises page at ~300 dpi → Tesseract OCR. Triggered automatically when a page has almost no text layer. |

## Build (one-time)

Double-click **`build.cmd`** — it downloads OCR language data, compiles, and produces:

```
dist\Docs2MD.exe
```

(Requires the .NET 8 SDK, which you already use for WizAccountant.)

## Using the app

1. Run `Docs2MD.exe`.
2. Drag & drop PDF/DOCX files (or folders) onto the window — or use **Add files… / Add folder…**.
3. Optionally pick an output folder (default: `.md` is written next to each input file).
4. Options: **OCR scanned pages** (on by default), **Force OCR on all PDF pages**, OCR language (`eng`, `eng+deu`, …).
5. Click **Convert**. Progress and per-file results appear in the log. Click again to cancel.

## Notes & limitations

- PDF → Markdown is heuristic by nature: multi-column layouts may interleave,
  and complex PDF tables are emitted as plain text lines (DOCX tables convert properly).
- OCR quality depends on scan quality; OCR'd pages are marked `<!-- page n (OCR) -->`.
- PDF pages are separated with `---` rules in the output.
- If Tesseract init fails (missing `tessdata`), text PDFs and DOCX still convert;
  scanned pages are skipped with a warning in the log.
- Extra OCR languages: `powershell -File get-tessdata.ps1 -Lang eng,deu` then rebuild.
- All processing is local; nothing is uploaded anywhere.
