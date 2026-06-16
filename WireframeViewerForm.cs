namespace Docs2MD;

using Markdig;
using MaterialSkin;
using MaterialSkin.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

/// <summary>
/// Opens a Markdown wireframe file and renders it as a styled HTML document
/// in an embedded Chromium (WebView2) browser. Supports Save PNG (viewport
/// capture) and Save PDF (full paginated document) export.
/// </summary>
public sealed class WireframeViewerForm : MaterialForm
{
    private readonly WebView2  _webView;
    private readonly Button    _openBtn;
    private readonly Button    _savePngBtn;
    private readonly Button    _savePdfBtn;
    private readonly Button    _copyBtn;
    private readonly Label          _statusLabel;
    private string?                 _currentPath;

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // ── construction ────────────────────────────────────────────────────

    public WireframeViewerForm(string? initialFile = null)
    {
        MaterialSkinManager.Instance.AddFormToManage(this);

        Text          = "Wireframe Viewer — Docs2MD";
        Size          = new Size(1150, 860);
        MinimumSize   = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop     = true;
        DragEnter    += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop     += OnDragDrop;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        // ── Toolbar ─────────────────────────────────────────────────────
        var toolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock     = DockStyle.Fill,
            Padding  = new Padding(6, 4, 4, 4)
        };

        _openBtn    = Btn("📂  Open MD…",   () => OpenAsync(),   primary: true);
        _savePngBtn = Btn("💾  Save PNG…",  () => SavePngAsync());
        _savePdfBtn = Btn("📄  Save PDF…",  () => SavePdfAsync());
        _copyBtn    = Btn("📋  Copy Image", () => CopyAsync());

        _savePngBtn.Enabled = _savePdfBtn.Enabled = _copyBtn.Enabled = false;

        _statusLabel = new Label
        {
            AutoSize  = true,
            Margin    = new Padding(12, 12, 0, 0),
            ForeColor = Color.Gray,
            Font      = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f)
        };

        toolbar.Controls.AddRange([_openBtn, _savePngBtn, _savePdfBtn, _copyBtn, _statusLabel]);
        root.Controls.Add(toolbar, 0, 0);

        // ── WebView2 ────────────────────────────────────────────────────
        _webView = new WebView2 { Dock = DockStyle.Fill };
        root.Controls.Add(_webView, 0, 1);

        // Initialise once the form handle exists
        Load += async (_, _) =>
        {
            try
            {
                await _webView.EnsureCoreWebView2Async();
                _webView.CoreWebView2.Settings.IsStatusBarEnabled          = false;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.IsZoomControlEnabled        = false;

                if (initialFile != null)
                    await LoadAsync(initialFile);
                else
                    ShowPlaceholder();
            }
            catch (Exception ex)
            {
                Status($"WebView2 init failed: {ex.Message}", error: true);
            }
        };
    }

    // ── file loading ────────────────────────────────────────────────────

    private async Task OpenAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Open wireframe markdown file",
            Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            await LoadAsync(dlg.FileName);
    }

    private async Task LoadAsync(string path)
    {
        Status("Rendering…");
        try
        {
            _currentPath = path;
            Text         = $"Wireframe Viewer — {Path.GetFileName(path)}";

            var md   = await File.ReadAllTextAsync(path);
            var html = Markdown.ToHtml(NormalizeMarkdown(md), Pipeline);
            _webView.NavigateToString(WrapHtml(html));

            _savePngBtn.Enabled = _savePdfBtn.Enabled = _copyBtn.Enabled = true;
            Status(path);
        }
        catch (Exception ex)
        {
            Status($"Error: {ex.Message}", error: true);
        }
    }

    private void ShowPlaceholder() =>
        _webView.NavigateToString(WrapHtml(
            "<p style='color:#bbb;padding:60px 40px;font-size:18px'>" +
            "📄 Open or drag-drop a <code>.md</code> wireframe file to render it here.</p>"));

    // ── export ──────────────────────────────────────────────────────────

    private async Task SavePngAsync()
    {
        using var dlg = new SaveFileDialog
        {
            Filter   = "PNG image (*.png)|*.png",
            FileName = BaseName() + ".png"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        Status("Capturing…");
        using var ms  = new MemoryStream();
        await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
        ms.Seek(0, SeekOrigin.Begin);
        using var img = new Bitmap(ms);
        img.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
        Status($"Saved: {Path.GetFileName(dlg.FileName)}");
    }

    private async Task SavePdfAsync()
    {
        using var dlg = new SaveFileDialog
        {
            Filter   = "PDF document (*.pdf)|*.pdf",
            FileName = BaseName() + ".pdf"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        Status("Generating PDF…");
        var ps = _webView.CoreWebView2.Environment.CreatePrintSettings();
        ps.ShouldPrintBackgrounds = true;
        ps.PageWidth    = 29.7;   // A4 landscape width (cm)
        ps.PageHeight   = 21.0;
        ps.MarginLeft   = ps.MarginRight  = 1.5;
        ps.MarginTop    = ps.MarginBottom = 1.5;
        await _webView.CoreWebView2.PrintToPdfAsync(dlg.FileName, ps);
        Status($"Saved: {Path.GetFileName(dlg.FileName)}");
    }

    private async Task CopyAsync()
    {
        Status("Capturing…");
        using var ms  = new MemoryStream();
        await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
        ms.Seek(0, SeekOrigin.Begin);
        using var img = new Bitmap(ms);
        Clipboard.SetImage(img);

        // Brief green flash
        _statusLabel.ForeColor = Color.ForestGreen;
        _statusLabel.Text      = "✓ Copied to clipboard!";
        await Task.Delay(2000);
        _statusLabel.ForeColor = Color.Gray;
        _statusLabel.Text      = _currentPath ?? "";
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            var md = paths.FirstOrDefault(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
            if (md != null) _ = LoadAsync(md);
        }
    }

    private string BaseName() =>
        _currentPath is null ? "wireframe" : Path.GetFileNameWithoutExtension(_currentPath);

    private void Status(string text, bool error = false)
    {
        _statusLabel.ForeColor = error ? Color.Crimson : Color.Gray;
        _statusLabel.Text      = text;
    }

    /// <summary>
    /// Ensures list items are always separated from the preceding paragraph by
    /// a blank line so Markdig reliably parses them (and their inline bold/italic).
    /// </summary>
    private static string NormalizeMarkdown(string md)
    {
        // Insert blank line before a list item that directly follows a non-blank,
        // non-heading, non-list line (e.g. "Two phases:\n- **X**" → two blank lines).
        md = System.Text.RegularExpressions.Regex.Replace(
            md,
            @"([^\n])\n([ \t]*[-*+] )",
            "$1\n\n$2",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return md;
    }

    /// <summary>
    /// Wraps a Markdig-generated HTML fragment in a complete styled page.
    /// Includes a JavaScript post-processor that detects wireframe code blocks
    /// (those containing box-drawing characters) and applies visual styling to
    /// buttons, dropdowns, status indicators and radio buttons.
    /// </summary>
    private static string WrapHtml(string body) => $$"""
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <style>
        * { box-sizing: border-box; }
        body {
            font-family: 'Segoe UI', Arial, sans-serif;
            font-size: 14px;
            max-width: 1060px;
            margin: 28px auto;
            padding: 0 32px 60px;
            color: #1a1a1a;
            background: #f4f6f9;
            line-height: 1.65;
        }

        /* ── Document headings ── */
        h1 {
            font-size: 22px;
            font-weight: 700;
            color: #fff;
            background: #0f62ae;
            padding: 12px 20px;
            margin: 0 -32px 24px;
            letter-spacing: 0.2px;
        }
        h2 {
            font-size: 15px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.8px;
            color: #0f62ae;
            border-bottom: 2px solid #0f62ae;
            padding-bottom: 4px;
            margin-top: 36px;
        }
        h3 {
            font-size: 14px;
            font-weight: 600;
            color: #333;
            margin-top: 22px;
        }
        h4 { font-size: 13px; font-weight: 600; color: #555; margin-top: 14px; }

        /* ── Body text ── */
        p  { margin: 6px 0 10px; color: #2c2c2c; }
        hr { border: none; border-top: 2px solid #d0d8e4; margin: 24px 0; }
        strong { font-weight: 700; color: #0a4d8c; }
        em { font-style: italic; color: #444; }
        ul, ol { padding-left: 22px; margin: 8px 0; }
        li { margin: 4px 0; }
        li::marker { color: #0f62ae; font-weight: bold; }
        blockquote {
            border-left: 4px solid #0078d4;
            margin: 12px 0;
            padding: 8px 16px;
            background: #eaf3fb;
            color: #333;
            border-radius: 0 4px 4px 0;
        }
        code {
            font-family: Consolas, 'Cascadia Code', 'Courier New', monospace;
            font-size: 12px;
            background: #dde5f0;
            padding: 1px 5px;
            border-radius: 3px;
            color: #0a4d8c;
        }

        /* ── Markdown tables ── */
        table {
            border-collapse: collapse;
            margin: 14px 0;
            width: auto;
            max-width: 100%;
            background: #fff;
            border-radius: 4px;
            overflow: hidden;
            box-shadow: 0 1px 4px rgba(0,0,0,0.08);
        }
        th, td {
            border: 1px solid #cdd5e0;
            padding: 7px 14px;
            text-align: left;
            font-size: 13px;
        }
        th { background: #e8eef6; font-weight: 700; color: #1a3a5c; }
        tr:nth-child(even) { background: #f8fafc; }

        /* ── Plain (non-wireframe) code blocks ── */
        pre {
            background: #fff;
            border: 1px solid #d0d7de;
            border-radius: 6px;
            padding: 14px 18px;
            margin: 16px 0;
            overflow-x: auto;
            line-height: 1.45;
        }
        pre code {
            font-family: Consolas, 'Cascadia Code', 'Courier New', monospace;
            font-size: 12.5px;
            background: none;
            padding: 0;
            white-space: pre;
            color: #24292e;
        }

        /* ── Wireframe code blocks (enhanced by JS) ── */
        pre.wf-frame {
            background: #ffffff;
            border: 1.5px solid #8a9bb5;
            border-radius: 6px;
            box-shadow: 0 3px 16px rgba(0,0,0,0.13);
            padding: 16px 20px;
        }
        pre.wf-frame code { font-size: 13px; line-height: 1.5; }

        /* Buttons  [text] */
        .wf-btn {
            display: inline;
            background: #e4ecf7;
            border: 1px solid #4a72a8;
            border-radius: 3px;
            padding: 0 4px;
            color: #0a3d7a;
            font-weight: 700;
        }
        /* Dropdowns  [text ▼] */
        .wf-dd {
            display: inline;
            background: #fff;
            border: 1px solid #777;
            border-radius: 2px;
            padding: 0 4px;
            color: #333;
        }
        /* Radio selected  ◉ */
        .wf-r-on  { color: #0f62ae; font-weight: 900; }
        /* Radio unselected  ○ */
        .wf-r-off { color: #aaa; }
        /* Status dots  ● */
        .wf-s-draft     { color: #888; }
        .wf-s-approved  { color: #1d6fd1; font-weight: 700; }
        .wf-s-running   { color: #d97706; font-weight: 700; }
        .wf-s-complete  { color: #16a34a; font-weight: 700; }
        .wf-s-cancelled { color: #dc2626; }
        .wf-s-default   { color: #555; }
        </style>
        </head>
        <body>
        {{body}}
        <script>
        // ── Wireframe code block enhancer ──────────────────────────────────────
        // Detects code blocks that contain box-drawing characters and applies
        // visual styling to common wireframe elements.
        document.querySelectorAll('pre code').forEach(function(el) {
            if (!/[┌┐└┘│─├┤┬┴┼╔╗╚╝║═]/.test(el.textContent)) return;

            el.parentElement.classList.add('wf-frame');
            var h = el.innerHTML;

            // ── Dropdowns: [text ▼] ──────────────────────────────────────────
            h = h.replace(/\[([^\]\n]{0,80}▼\s*)\]/g,
                '<span class="wf-dd">[$1]</span>');

            // ── Buttons: remaining [text] ────────────────────────────────────
            h = h.replace(/\[([^\]\n]{0,80})\]/g,
                '<span class="wf-btn">[$1]</span>');

            // ── Radio buttons ────────────────────────────────────────────────
            h = h.replace(/◉/g, '<span class="wf-r-on">◉</span>');
            h = h.replace(/○/g, '<span class="wf-r-off">○</span>');

            // ── Status dots ● (colour by keyword that follows) ───────────────
            h = h.replace(/● ?(Draft|Open)[^\n<]*/gi, function(m) {
                return '<span class="wf-s-draft">' + m + '</span>'; });
            h = h.replace(/● ?(Approved)[^\n<]*/gi, function(m) {
                return '<span class="wf-s-approved">' + m + '</span>'; });
            h = h.replace(/● ?(Running|Active|In.?Progress)[^\n<]*/gi, function(m) {
                return '<span class="wf-s-running">' + m + '</span>'; });
            h = h.replace(/● ?(Complete|Done|Closed)[^\n<]*/gi, function(m) {
                return '<span class="wf-s-complete">' + m + '</span>'; });
            h = h.replace(/● ?(Cancelled|Rejected|Failed)[^\n<]*/gi, function(m) {
                return '<span class="wf-s-cancelled">' + m + '</span>'; });
            // Any remaining ● gets default style
            h = h.replace(/●([^\n<]*)/g,
                '<span class="wf-s-default">●$1</span>');

            el.innerHTML = h;
        });
        </script>
        </body>
        </html>
        """;

    /// <summary>Creates a toolbar button that runs an async action with error surfacing.</summary>
    private Button Btn(string text, Func<Task> action, bool primary = false)
    {
        var navy = Color.FromArgb(27, 53, 96);
        var b = new Button
        {
            Text      = text,
            AutoSize  = true,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 9f),
            Cursor    = Cursors.Hand,
            Margin    = new Padding(0, 2, 4, 2),
            Padding   = new Padding(8, 4, 8, 4),
            BackColor = Color.White,
            ForeColor = navy
        };
        b.FlatAppearance.BorderColor = primary ? navy : Color.Transparent;
        b.FlatAppearance.BorderSize  = primary ? 1 : 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 240, 250);
        b.Click += async (_, _) =>
        {
            try   { await action(); }
            catch (Exception ex) { Status($"Error: {ex.Message}", error: true); }
        };
        return b;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _webView.Dispose();
        base.Dispose(disposing);
    }
}
