namespace Docs2MD;

using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

/// <summary>
/// Opens a Markdown wireframe file and renders it as a styled HTML document
/// in an embedded Chromium (WebView2) browser. Supports Save PNG (viewport
/// capture) and Save PDF (full paginated document) export.
/// </summary>
public sealed class WireframeViewerForm : Form
{
    private readonly WebView2 _webView;
    private readonly Button   _openBtn;
    private readonly Button   _savePngBtn;
    private readonly Button   _savePdfBtn;
    private readonly Button   _copyBtn;
    private readonly Label    _statusLabel;
    private string?           _currentPath;

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // ── construction ────────────────────────────────────────────────────

    public WireframeViewerForm(string? initialFile = null)
    {
        Text            = "Wireframe Viewer — Docs2MD";
        Size            = new Size(1150, 860);
        MinimumSize     = new Size(700, 500);
        StartPosition   = FormStartPosition.CenterScreen;
        AllowDrop       = true;
        DragEnter      += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop       += OnDragDrop;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        // ── Toolbar ─────────────────────────────────────────────────────
        var toolbar = new FlowLayoutPanel
        {
            AutoSize  = true,
            Dock      = DockStyle.Fill,
            Padding   = new Padding(6, 4, 4, 4),
            BackColor = Color.FromArgb(245, 245, 245)
        };

        _openBtn    = Btn("📂 Open MD…",   () => OpenAsync());
        _savePngBtn = Btn("💾 Save PNG…",  () => SavePngAsync());
        _savePdfBtn = Btn("📄 Save PDF…",  () => SavePdfAsync());
        _copyBtn    = Btn("📋 Copy Image", () => CopyAsync());

        _savePngBtn.Enabled = _savePdfBtn.Enabled = _copyBtn.Enabled = false;

        _statusLabel = new Label
        {
            AutoSize  = true,
            Margin    = new Padding(12, 8, 0, 0),
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
            var html = Markdown.ToHtml(md, Pipeline);
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
    /// Wraps a Markdig-generated HTML fragment in a complete styled page.
    /// The CSS is tuned for wireframe documents: monospace code blocks with
    /// box-drawing characters, clean typography, bordered tables.
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
            max-width: 1040px;
            margin: 28px auto;
            padding: 0 32px 56px;
            color: #1a1a1a;
            background: #ffffff;
            line-height: 1.6;
        }
        h1 {
            font-size: 22px;
            border-bottom: 2px solid #2c2c2c;
            padding-bottom: 8px;
            margin-top: 6px;
        }
        h2 {
            font-size: 18px;
            border-bottom: 1px solid #ddd;
            padding-bottom: 5px;
            margin-top: 30px;
            color: #222;
        }
        h3 { font-size: 15px; margin-top: 20px; color: #333; }
        h4 { font-size: 14px; margin-top: 14px; color: #444; }

        /* ── Wireframe code blocks ── */
        pre {
            background: #f6f8fa;
            border: 1px solid #d0d7de;
            border-radius: 6px;
            padding: 14px 18px;
            margin: 14px 0;
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
        code {
            font-family: Consolas, 'Cascadia Code', 'Courier New', monospace;
            font-size: 12.5px;
            background: #f0f0f0;
            padding: 1px 5px;
            border-radius: 3px;
            color: #0550ae;
        }

        /* ── Tables ── */
        table {
            border-collapse: collapse;
            margin: 14px 0;
            width: auto;
            max-width: 100%;
        }
        th, td {
            border: 1px solid #c8c8c8;
            padding: 6px 14px;
            text-align: left;
            font-size: 13px;
        }
        th {
            background: #f0f0f0;
            font-weight: 600;
        }
        tr:nth-child(even) { background: #fafafa; }

        /* ── Misc ── */
        p  { margin: 8px 0; }
        hr { border: none; border-top: 1px solid #e0e0e0; margin: 22px 0; }
        strong { font-weight: 600; }
        ul, ol { padding-left: 26px; margin: 6px 0; }
        li { margin: 3px 0; }
        blockquote {
            border-left: 4px solid #0078d4;
            margin: 12px 0;
            padding: 6px 16px;
            background: #f0f7ff;
            color: #333;
            border-radius: 0 4px 4px 0;
        }
        </style>
        </head>
        <body>{{body}}</body>
        </html>
        """;

    /// <summary>Creates a toolbar button that runs an async action with error surfacing.</summary>
    private Button Btn(string text, Func<Task> action)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
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
