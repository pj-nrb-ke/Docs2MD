namespace Docs2MD;

using System.Reflection;
using System.Runtime.InteropServices;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Svg;

/// <summary>
/// General-purpose Markdown viewer and document exporter.
/// Opens any .md file, renders it as a styled HTML document, and can export to
/// Word (.docx), Excel (.xlsx), or PDF. Wireframe code blocks (those containing
/// box-drawing characters) get the same smart visual enhancements as the
/// dedicated Wireframe Viewer.
/// </summary>
public sealed class MdViewerForm : Form
{
    // ── P/Invoke ─────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int  SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION       = 0x02;

    private static readonly Color Navy   = Color.FromArgb(27, 53, 96);
    private static readonly Color BgGray = Color.FromArgb(242, 244, 247);

    // ── Fields ───────────────────────────────────────────────────────────
    private readonly WebView2  _webView;
    private readonly Button    _openBtn;
    private readonly Button    _pasteBtn;
    private readonly Button    _wordBtn;
    private readonly Button    _excelBtn;
    private readonly Button    _pdfBtn;
    private readonly Button    _copyBtn;
    private readonly Button    _copyRtfBtn;
    private readonly Button    _copySelBtn;
    private readonly Label     _statusLabel;
    private readonly Label     _titleLabel;
    private string?            _currentPath;
    private string?            _currentMd;
    private string             _pendingHtml = "";
    private int                _navVersion  = 0;

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // ── Construction ─────────────────────────────────────────────────────

    public MdViewerForm(string? initialFile = null)
    {
        FormBorderStyle = FormBorderStyle.None;
        Text            = "MD Viewer / Export — Docs2MD";
        Size            = new Size(1500, 860);
        MinimumSize     = new Size(960, 500);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = BgGray;
        AllowDrop       = true;
        DragEnter      += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop       += OnDragDrop;

        var iconBmp = LoadLogo(64, 43);
        if (iconBmp != null) Icon = Icon.FromHandle(iconBmp.GetHicon());

        // ── Outer shell: title bar (68 px) + content ────────────────────
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = Padding.Empty, Margin = Padding.Empty, BackColor = BgGray
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));   // title bar
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // content
        Controls.Add(outer);

        _titleLabel = new Label();
        outer.Controls.Add(BuildTitleBar(), 0, 0);

        // ── Content: toolbar + WebView2 ──────────────────────────────────
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = Padding.Empty, Margin = Padding.Empty, BackColor = Color.White
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Toolbar panel (white background + bottom divider)
        var toolbarPanel = new Panel { Dock = DockStyle.Fill, AutoSize = true, BackColor = Color.White };
        toolbarPanel.Controls.Add(new Panel
        {
            Height = 1, Dock = DockStyle.Bottom, BackColor = Color.FromArgb(218, 224, 236)
        });

        var toolbar = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill,
            Padding  = new Padding(6, 4, 4, 4), BackColor = Color.White
        };

        _openBtn    = Btn("📂  Open MD…",        () => OpenAsync(),           primary: true);
        _pasteBtn   = Btn("📋  Paste Markdown",  () => PasteMarkdownAsync(),  primary: true);
        _wordBtn    = Btn("📝  Export Word…",    () => ExportWordAsync());
        _excelBtn   = Btn("📊  Export Excel…",   () => ExportExcelAsync());
        _pdfBtn     = Btn("📄  Save PDF…",       () => SavePdfAsync());
        _copyBtn    = Btn("🖼️  Copy Image",      () => CopyImageAsync());
        _copyRtfBtn = Btn("📧  Copy Rich Text",  () => CopyRichTextAsync());
        _copySelBtn = Btn("✂️  Copy Selected",  () => CopySelectedAsync());

        _wordBtn.Enabled = _excelBtn.Enabled = _pdfBtn.Enabled =
            _copyBtn.Enabled = _copyRtfBtn.Enabled = _copySelBtn.Enabled = false;

        _statusLabel = new Label
        {
            AutoSize  = true,
            Margin    = new Padding(12, 12, 0, 0),
            ForeColor = Color.Gray,
            Font      = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f)
        };

        toolbar.Controls.AddRange([_openBtn, _pasteBtn, _wordBtn, _excelBtn, _pdfBtn, _copyBtn, _copyRtfBtn, _copySelBtn, _statusLabel]);
        toolbarPanel.Controls.Add(toolbar);
        content.Controls.Add(toolbarPanel, 0, 0);

        // ── WebView2 ─────────────────────────────────────────────────────
        _webView = new WebView2 { Dock = DockStyle.Fill };
        content.Controls.Add(_webView, 0, 1);
        outer.Controls.Add(content, 0, 1);

        Load += async (_, _) =>
        {
            try
            {
                await _webView.EnsureCoreWebView2Async();
                _webView.CoreWebView2.Settings.IsStatusBarEnabled            = false;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _webView.CoreWebView2.Settings.IsZoomControlEnabled          = false;

                // Right-click context menu: Copy as Rich Text / Copy as Plain Text
                _webView.CoreWebView2.ContextMenuRequested += (_, e) =>
                {
                    e.Handled = true;
                    var screenPt = _webView.PointToScreen(e.Location);
                    var strip = new ContextMenuStrip();
                    var copyRich  = new ToolStripMenuItem("Copy as Rich Text");
                    var copyPlain = new ToolStripMenuItem("Copy as Plain Text");
                    copyRich.Click  += async (_, _) => await CopySelectedAsync(rich: true);
                    copyPlain.Click += async (_, _) => await CopySelectedAsync(rich: false);
                    strip.Items.Add(copyRich);
                    strip.Items.Add(copyPlain);
                    strip.Show(screenPt);
                };

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

                // Belt-and-suspenders: also cancel any in-page file:// navigation.
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

    // ── Custom title bar (same design as MainForm) ───────────────────────

    private Panel BuildTitleBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Navy };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Navy,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // logo + title
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // spacer (drag)
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // win buttons

        var left = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, BackColor = Navy,
            Dock = DockStyle.Fill, Padding = new Padding(10, 12, 0, 0)
        };
        var logoBmp = LoadLogo(64, 43);
        if (logoBmp != null)
        {
            var logoPb = new PictureBox
            {
                Image = logoBmp, SizeMode = PictureBoxSizeMode.AutoSize,
                BackColor = Navy, Margin = new Padding(0, 0, 8, 0)
            };
            logoPb.MouseDown += TitleBarDrag;
            left.Controls.Add(logoPb);
        }

        _titleLabel.Text      = "MD Viewer / Export";
        _titleLabel.AutoSize  = true;
        _titleLabel.Font      = new Font("Segoe UI", 11f, FontStyle.Bold);
        _titleLabel.ForeColor = Color.White;
        _titleLabel.BackColor = Navy;
        _titleLabel.Margin    = new Padding(0, 13, 0, 0);
        _titleLabel.MouseDown   += TitleBarDrag;
        _titleLabel.DoubleClick += (_, _) => { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; };
        left.Controls.Add(_titleLabel);
        left.MouseDown += TitleBarDrag;
        layout.Controls.Add(left, 0, 0);

        var spacer = new Label { Dock = DockStyle.Fill, BackColor = Navy };
        spacer.MouseDown += TitleBarDrag;
        layout.Controls.Add(spacer, 1, 0);

        var minBtn   = WinBtn("─", hoverRed: false);
        var maxBtn   = WinBtn("□", hoverRed: false);
        var closeBtn = WinBtn("✕", hoverRed: true);
        minBtn.Click   += (_, _) => WindowState = FormWindowState.Minimized;
        maxBtn.Click   += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        closeBtn.Click += (_, _) => Close();

        var winBtns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Navy };
        winBtns.Controls.AddRange([minBtn, maxBtn, closeBtn]);

        var winBtnsWrap = new Panel { Dock = DockStyle.Fill, BackColor = Navy };
        winBtnsWrap.Controls.Add(winBtns);
        winBtnsWrap.Resize += (_, _) =>
            winBtns.Location = new Point(
                winBtnsWrap.Width  - winBtns.Width,
                (winBtnsWrap.Height - winBtns.Height) / 2);
        layout.Controls.Add(winBtnsWrap, 2, 0);

        bar.Controls.Add(layout);
        bar.MouseDown   += TitleBarDrag;
        bar.DoubleClick += (_, _) => maxBtn.PerformClick();

        return bar;
    }

    private void TitleBarDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
    }

    private static Button WinBtn(string text, bool hoverRed)
    {
        var b = new Button
        {
            Text      = text,
            Width     = 48,
            Height    = 38,
            FlatStyle = FlatStyle.Flat,
            BackColor = Navy,
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 13f, FontStyle.Bold),
            Cursor    = Cursors.Arrow
        };
        b.FlatAppearance.BorderSize             = 0;
        b.FlatAppearance.MouseOverBackColor     = hoverRed
            ? Color.FromArgb(196, 43, 28)
            : Color.FromArgb(50, 80, 130);
        return b;
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            int lp = m.LParam.ToInt32();
            int sx = lp & 0xFFFF;          if (sx > 32767) sx -= 65536;
            int sy = (lp >> 16) & 0xFFFF;  if (sy > 32767) sy -= 65536;
            var pt = PointToClient(new Point(sx, sy));
            int x = pt.X, y = pt.Y, w = ClientSize.Width, h = ClientSize.Height;
            const int t = 6;
            if      (x < t    && y < t)    { m.Result = (IntPtr)13; return; }
            else if (x >= w-t && y < t)    { m.Result = (IntPtr)14; return; }
            else if (x < t    && y >= h-t) { m.Result = (IntPtr)16; return; }
            else if (x >= w-t && y >= h-t) { m.Result = (IntPtr)17; return; }
            else if (y < t)                { m.Result = (IntPtr)12; return; }
            else if (y >= h-t)             { m.Result = (IntPtr)15; return; }
            else if (x < t)                { m.Result = (IntPtr)10; return; }
            else if (x >= w-t)             { m.Result = (IntPtr)11; return; }
        }
        base.WndProc(ref m);
    }

    private static Bitmap? LoadLogo(int width, int height)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("Docs2MD.logo.svg");
            if (stream == null) return null;
            var svg = SvgDocument.Open<SvgDocument>(stream);
            return svg.Draw(width, height);
        }
        catch { return null; }
    }

    // ── File loading ─────────────────────────────────────────────────────

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
            _titleLabel.Text = $"MD Viewer  —  {Path.GetFileName(path)}";

            await _webView.EnsureCoreWebView2Async();
            var html = Markdown.ToHtml(MdExporter.NormalizeMarkdown(_currentMd), Pipeline);
            NavigateHtml(WrapHtml(html));

            _wordBtn.Enabled = _excelBtn.Enabled = _pdfBtn.Enabled =
                _copyBtn.Enabled = _copyRtfBtn.Enabled = _copySelBtn.Enabled = true;
            Status(path);
        }
        catch (Exception ex) { Status($"Error: {ex.Message}", error: true); }
    }

    private async Task PasteMarkdownAsync()
    {
        var text = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            Status("Clipboard is empty or contains no text.", error: true);
            return;
        }

        Status("Rendering pasted content…");
        try
        {
            _currentPath = null;
            _currentMd   = text;
            _titleLabel.Text = "MD Viewer  —  Pasted content";

            await _webView.EnsureCoreWebView2Async();
            var html = Markdown.ToHtml(MdExporter.NormalizeMarkdown(_currentMd), Pipeline);
            NavigateHtml(WrapHtml(html));

            _wordBtn.Enabled = _excelBtn.Enabled = _pdfBtn.Enabled =
                _copyBtn.Enabled = _copyRtfBtn.Enabled = _copySelBtn.Enabled = true;
            Status("Pasted content rendered");
        }
        catch (Exception ex) { Status($"Error: {ex.Message}", error: true); }
    }

    private void ShowPlaceholder() =>
        NavigateHtml(WrapHtml(
            "<p style='color:#bbb;padding:60px 40px;font-size:18px'>" +
            "Open or drag-drop a <code>.md</code> file, or click <b>Paste Markdown</b> to paste text.</p>"));

    private void NavigateHtml(string fullHtml)
    {
        _pendingHtml = fullHtml;
        _webView.CoreWebView2.Navigate($"https://docs2md.local/v{++_navVersion}");
    }

    // ── Export ───────────────────────────────────────────────────────────

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
        ps.PageWidth  = 21.0; ps.PageHeight = 29.7;
        ps.MarginLeft = ps.MarginRight = ps.MarginTop = ps.MarginBottom = 1.5;
        await _webView.CoreWebView2.PrintToPdfAsync(dlg.FileName, ps);
        Status($"Saved: {Path.GetFileName(dlg.FileName)}");
    }

    private async Task CopyImageAsync()
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
        _statusLabel.Text      = _currentPath ?? "Pasted content";
    }

    private async Task CopyRichTextAsync()
    {
        if (_currentMd is null) return;

        Status("Copying rich text…");
        var html    = Markdown.ToHtml(MdExporter.NormalizeMarkdown(_currentMd), Pipeline);
        var cfHtml  = BuildCFHtml(html);
        var dataObj = new DataObject();
        dataObj.SetData(DataFormats.Html, cfHtml);
        dataObj.SetData(DataFormats.Text, _currentMd);
        Clipboard.SetDataObject(dataObj, copy: true);

        _statusLabel.ForeColor = Color.ForestGreen;
        _statusLabel.Text      = "✓ Copied as rich text — paste into Outlook or eM Client";
        await Task.Delay(2500);
        _statusLabel.ForeColor = Color.Gray;
        _statusLabel.Text      = _currentPath ?? "Pasted content";
    }

    private async Task CopySelectedAsync(bool rich = true)
    {
        var htmlJson  = await _webView.CoreWebView2.ExecuteScriptAsync(@"(function(){
            var sel=window.getSelection();
            if(!sel||sel.isCollapsed)return null;
            var r=sel.getRangeAt(0);
            var d=document.createElement('div');
            d.appendChild(r.cloneContents());
            return d.innerHTML;
        })()");
        var plainJson = await _webView.CoreWebView2.ExecuteScriptAsync(
            "window.getSelection()?.toString()??''");

        var html  = System.Text.Json.JsonSerializer.Deserialize<string>(htmlJson);
        var plain = System.Text.Json.JsonSerializer.Deserialize<string>(plainJson) ?? "";

        if (string.IsNullOrWhiteSpace(html))
        {
            Status("Select text in the preview first.", error: true);
            return;
        }

        if (rich)
        {
            var cfHtml  = BuildCFHtml(html);
            var dataObj = new DataObject();
            dataObj.SetData(DataFormats.Html, cfHtml);
            dataObj.SetData(DataFormats.Text, plain);
            Clipboard.SetDataObject(dataObj, copy: true);
        }
        else
        {
            Clipboard.SetText(plain);
        }

        _statusLabel.ForeColor = Color.ForestGreen;
        _statusLabel.Text      = rich ? "✓ Selection copied as rich text" : "✓ Selection copied as plain text";
        await Task.Delay(2000);
        _statusLabel.ForeColor = Color.Gray;
        _statusLabel.Text      = _currentPath ?? "Pasted content";
    }

    // Builds the Windows CF_HTML clipboard format string required by Outlook / eM Client.
    private static string BuildCFHtml(string htmlFragment)
    {
        const string headerTemplate =
            "Version:0.9\r\n" +
            "StartHTML:{0:D10}\r\n" +
            "EndHTML:{1:D10}\r\n" +
            "StartFragment:{2:D10}\r\n" +
            "EndFragment:{3:D10}\r\n";

        const string prefix = "<html><body>\r\n<!--StartFragment-->";
        const string suffix = "<!--EndFragment-->\r\n</body></html>";

        var enc       = System.Text.Encoding.UTF8;
        int headerLen = enc.GetByteCount(string.Format(headerTemplate, 0, 0, 0, 0));
        int startHtml     = headerLen;
        int startFragment = startHtml     + enc.GetByteCount(prefix);
        int endFragment   = startFragment + enc.GetByteCount(htmlFragment);
        int endHtml       = endFragment   + enc.GetByteCount(suffix);

        return string.Format(headerTemplate, startHtml, endHtml, startFragment, endFragment)
               + prefix + htmlFragment + suffix;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

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
            ForeColor = Navy
        };
        b.FlatAppearance.BorderColor            = primary ? Navy : Color.White;
        b.FlatAppearance.BorderSize             = primary ? 1 : 0;
        b.FlatAppearance.MouseOverBackColor     = Color.FromArgb(235, 240, 250);
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
