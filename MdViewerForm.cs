namespace Docs2MD;

using Markdig;
using MaterialSkin;
using MaterialSkin.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

/// <summary>
/// General-purpose Markdown viewer and document exporter.
/// Opens any .md file, renders it as a styled HTML document, and can export to
/// Word (.docx), Excel (.xlsx), or PDF. Wireframe code blocks (those containing
/// box-drawing characters) get the same smart visual enhancements as the
/// dedicated Wireframe Viewer.
/// </summary>
public sealed class MdViewerForm : MaterialForm
{
    private readonly WebView2  _webView;
    private readonly Button    _openBtn;
    private readonly Button    _wordBtn;
    private readonly Button    _excelBtn;
    private readonly Button    _pdfBtn;
    private readonly Button    _copyBtn;
    private readonly Label          _statusLabel;
    private string?                 _currentPath;
    private string?                 _currentMd;
    private string                  _pendingHtml = "";
    private int                     _navVersion  = 0;

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // ── construction ────────────────────────────────────────────────────

    public MdViewerForm(string? initialFile = null)
    {
        MaterialSkinManager.Instance.AddFormToManage(this);

        Text          = "MD Viewer / Export — Docs2MD";
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

        _openBtn  = Btn("📂  Open MD…",      () => OpenAsync(),        primary: true);
        _wordBtn  = Btn("📝  Export Word…",  () => ExportWordAsync());
        _excelBtn = Btn("📊  Export Excel…", () => ExportExcelAsync());
        _pdfBtn   = Btn("📄  Save PDF…",     () => SavePdfAsync());
        _copyBtn  = Btn("📋  Copy Image",    () => CopyAsync());

        _wordBtn.Enabled = _excelBtn.Enabled = _pdfBtn.Enabled = _copyBtn.Enabled = false;

        _statusLabel = new Label
        {
            AutoSize  = true,
            Margin    = new Padding(12, 12, 0, 0),
            ForeColor = Color.Gray,
            Font      = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f)
        };

        toolbar.Controls.AddRange([_openBtn, _wordBtn, _excelBtn, _pdfBtn, _copyBtn, _statusLabel]);
        root.Controls.Add(toolbar, 0, 0);

        // ── WebView2 ────────────────────────────────────────────────────
        _webView = new WebView2 { Dock = DockStyle.Fill };
        root.Controls.Add(_webView, 0, 1);

        Load += async (_, _) =>
        {
            try
            {
                await _webView.EnsureCoreWebView2Async();
                _webView.CoreWebView2.Settings.IsStatusBarEnabled            = false;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.IsZoomControlEnabled          = false;

                // Serve generated HTML via a virtual HTTPS host so Chromium renders
                // it as trusted HTML without any data-URL or file-URL restrictions.
                _webView.CoreWebView2.AddWebResourceRequestedFilter(
                    "https://docs2md.local/*", CoreWebView2WebResourceContext.All);
                _webView.CoreWebView2.WebResourceRequested += (_, e) =>
                {
                    if (!e.Request.Uri.StartsWith("https://docs2md.local/")) return;
                    var bytes = System.Text.Encoding.UTF8.GetBytes(_pendingHtml);
                    e.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(
                        new MemoryStream(bytes), 200, "OK",
                        "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-cache");
                };

                // When a file is dragged onto the WebView2 area, Chromium opens a NEW
                // popup window for the file:// URL rather than navigating in-place.
                // Intercept that popup and route .md files through LoadAsync instead.
                _webView.CoreWebView2.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    if (e.Uri.StartsWith("file://"))
                    {
                        var localPath = Uri.UnescapeDataString(new Uri(e.Uri).LocalPath);
                        if (localPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                            _ = LoadAsync(localPath);
                    }
                };

                // Belt-and-suspenders: also cancel any in-page file:// navigation
                // in case the WebView2 version navigates in-place instead of popup.
                _webView.CoreWebView2.NavigationStarting += (_, e) =>
                {
                    if (!e.Uri.StartsWith("file://")) return;
                    e.Cancel = true;
                    var localPath = Uri.UnescapeDataString(new Uri(e.Uri).LocalPath);
                    if (localPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        _ = LoadAsync(localPath);
                };

                if (initialFile != null) await LoadAsync(initialFile);
                else ShowPlaceholder();
            }
            catch (Exception ex) { Status($"WebView2 init failed: {ex.Message}", error: true); }
        };
    }

    // ── file loading ────────────────────────────────────────────────────

    private async Task OpenAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Open Markdown file",
            Filter = "Markdown (*.md)|*.md|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            await LoadAsync(dlg.FileName);
    }

    private async Task LoadAsync(string path)
    {
        Status("Rendering…");
        try
        {
            _currentPath = path;
            _currentMd   = await File.ReadAllTextAsync(path);
            Text         = $"MD Viewer — {Path.GetFileName(path)}";

            await _webView.EnsureCoreWebView2Async();
            var html = Markdown.ToHtml(MdExporter.NormalizeMarkdown(_currentMd), Pipeline);
            NavigateHtml(WrapHtml(html));

            _wordBtn.Enabled = _excelBtn.Enabled = _pdfBtn.Enabled = _copyBtn.Enabled = true;
            Status(path);
        }
        catch (Exception ex) { Status($"Error: {ex.Message}", error: true); }
    }

    private void ShowPlaceholder() =>
        NavigateHtml(WrapHtml(
            "<p style='color:#bbb;padding:60px 40px;font-size:18px'>" +
            "Open or drag-drop a <code>.md</code> file to view and export it.</p>"));

    private void NavigateHtml(string fullHtml)
    {
        _pendingHtml = fullHtml;
        _webView.CoreWebView2.Navigate($"https://docs2md.local/v{++_navVersion}");
    }

    // ── export ──────────────────────────────────────────────────────────

    private async Task ExportWordAsync()
    {
        if (_currentMd is null) return;
        using var dlg = new SaveFileDialog
        {
            Filter   = "Word document (*.docx)|*.docx",
            FileName = BaseName() + ".docx"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        Status("Exporting to Word…");
        var path = dlg.FileName;
        await Task.Run(() => MdExporter.ToWord(_currentMd, path));
        Status($"Saved: {Path.GetFileName(path)}");
    }

    private async Task ExportExcelAsync()
    {
        if (_currentMd is null) return;
        using var dlg = new SaveFileDialog
        {
            Filter   = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = BaseName() + ".xlsx"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        Status("Exporting to Excel…");
        var path = dlg.FileName;
        await Task.Run(() => MdExporter.ToExcel(_currentMd, path));
        Status($"Saved: {Path.GetFileName(path)}");
    }

    private async Task SavePdfAsync()
    {
        using var dlg = new SaveFileDialog
        {
            Filter   = "PDF document (*.pdf)|*.pdf",
            FileName = BaseName() + ".pdf"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        Status("Generating PDF…");
        var ps = _webView.CoreWebView2.Environment.CreatePrintSettings();
        ps.ShouldPrintBackgrounds = true;
        ps.PageWidth  = 21.0; ps.PageHeight = 29.7;  // A4 portrait
        ps.MarginLeft = ps.MarginRight = ps.MarginTop = ps.MarginBottom = 1.5;
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
        _currentPath is null ? "document" : Path.GetFileNameWithoutExtension(_currentPath);

    private void Status(string text, bool error = false)
    {
        _statusLabel.ForeColor = error ? Color.Crimson : Color.Gray;
        _statusLabel.Text      = text;
    }

    /// <summary>
    /// Clean, neutral document styling — suitable for any markdown content.
    /// Wireframe code blocks (detected by box-drawing chars) also get the
    /// same smart element highlighting as the dedicated Wireframe Viewer.
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
            max-width: 900px;
            margin: 36px auto;
            padding: 0 44px 64px;
            color: #1a1a1a;
            background: #ffffff;
            line-height: 1.7;
        }

        /* ── Headings ── */
        h1 { font-size: 26px; font-weight: 700; border-bottom: 3px solid #1a1a1a; padding-bottom: 10px; margin-top: 6px; }
        h2 { font-size: 19px; font-weight: 700; border-bottom: 1px solid #ccc; padding-bottom: 5px; margin-top: 34px; }
        h3 { font-size: 15px; font-weight: 600; margin-top: 24px; color: #333; }
        h4 { font-size: 14px; font-weight: 600; margin-top: 16px; color: #555; }

        /* ── Body ── */
        p  { margin: 10px 0; }
        hr { border: none; border-top: 1px solid #e0e0e0; margin: 26px 0; }
        strong { font-weight: 700; }
        em { font-style: italic; color: #444; }
        ul, ol { padding-left: 24px; margin: 10px 0; }
        li { margin: 4px 0; }
        blockquote {
            border-left: 4px solid #0078d4;
            margin: 16px 0; padding: 8px 16px;
            background: #f0f7ff; border-radius: 0 4px 4px 0;
        }
        code {
            font-family: Consolas, 'Cascadia Code', 'Courier New', monospace;
            font-size: 12.5px; background: #f0f0f0;
            padding: 2px 5px; border-radius: 3px; color: #333;
        }

        /* ── Code blocks (general) ── */
        pre {
            background: #f8f8f8; border: 1px solid #ddd;
            border-radius: 5px; padding: 14px 18px;
            margin: 16px 0; overflow-x: auto;
        }
        pre code {
            background: none; padding: 0;
            font-size: 12.5px; white-space: pre; line-height: 1.45; color: #24292e;
        }

        /* ── Wireframe code blocks (JS-enhanced) ── */
        pre.wf-frame {
            background: #ffffff;
            border: 1.5px solid #999;
            box-shadow: 2px 4px 14px rgba(0,0,0,0.10);
            border-radius: 5px;
        }
        .wf-btn  { background:#e4ecf7; border:1px solid #4a72a8; border-radius:3px; padding:0 4px; color:#0a3d7a; font-weight:700; }
        .wf-dd   { background:#fff; border:1px solid #888; border-radius:2px; padding:0 4px; color:#333; }
        .wf-r-on  { color:#0f62ae; font-weight:900; }
        .wf-r-off { color:#bbb; }
        .wf-s-draft     { color:#888; }
        .wf-s-approved  { color:#1d6fd1; font-weight:700; }
        .wf-s-running   { color:#d97706; font-weight:700; }
        .wf-s-complete  { color:#16a34a; font-weight:700; }
        .wf-s-cancelled { color:#dc2626; }
        .wf-s-default   { color:#555; }

        /* ── Tables ── */
        table { border-collapse:collapse; margin:16px 0; width:auto; max-width:100%; }
        th, td { border:1px solid #ccc; padding:7px 14px; text-align:left; font-size:13.5px; }
        th { background:#f2f2f2; font-weight:700; }
        tr:nth-child(even) { background:#fafafa; }
        </style>
        </head>
        <body>
        {{body}}
        <script>
        // Wireframe code-block enhancer — same logic as WireframeViewerForm
        document.querySelectorAll('pre code').forEach(function(el) {
            if (!/[┌┐└┘│─├┤┬┴┼╔╗╚╝║═]/.test(el.textContent)) return;
            el.parentElement.classList.add('wf-frame');
            var h = el.innerHTML;
            h = h.replace(/\[([^\]\n]{0,80}▼\s*)\]/g, '<span class="wf-dd">[$1]</span>');
            h = h.replace(/\[([^\]\n]{0,80})\]/g,     '<span class="wf-btn">[$1]</span>');
            h = h.replace(/◉/g, '<span class="wf-r-on">◉</span>');
            h = h.replace(/○/g, '<span class="wf-r-off">○</span>');
            h = h.replace(/● ?(Draft|Open)[^\n<]*/gi,                function(m){ return '<span class="wf-s-draft">'     + m + '</span>'; });
            h = h.replace(/● ?(Approved)[^\n<]*/gi,                  function(m){ return '<span class="wf-s-approved">'  + m + '</span>'; });
            h = h.replace(/● ?(Running|Active|In.?Progress)[^\n<]*/gi,function(m){ return '<span class="wf-s-running">'  + m + '</span>'; });
            h = h.replace(/● ?(Complete|Done|Closed)[^\n<]*/gi,      function(m){ return '<span class="wf-s-complete">'  + m + '</span>'; });
            h = h.replace(/● ?(Cancelled|Rejected|Failed)[^\n<]*/gi, function(m){ return '<span class="wf-s-cancelled">' + m + '</span>'; });
            h = h.replace(/●([^\n<]*)/g, '<span class="wf-s-default">●$1</span>');
            el.innerHTML = h;
        });
        </script>
        </body>
        </html>
        """;

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
        b.FlatAppearance.BorderColor = primary ? navy : Color.White;
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
