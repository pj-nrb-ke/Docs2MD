namespace Docs2MD;

using System.Reflection;
using System.Runtime.InteropServices;
using MaterialSkin;
using MaterialSkin.Controls;
using Svg;

public sealed class MainForm : Form
{
    // ── P/Invoke for borderless drag ───────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int  SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION       = 0x02;

    // ── Fields ─────────────────────────────────────────────────────────────
    private readonly ListBox             _fileList;
    private readonly TextBox             _outputFolder;
    private readonly CheckBox            _ocrEnabled;
    private readonly CheckBox            _forceOcr;
    private readonly TextBox             _ocrLang;
    private readonly Button              _convertButton;
    private readonly MaterialProgressBar _progress;
    private readonly TextBox             _log;
    private CancellationTokenSource?     _cts;
    private readonly UserSettings        _settings = UserSettings.Load();

    private static readonly Color Navy   = Color.FromArgb(27,  53,  96);
    private static readonly Color Teal   = Color.FromArgb( 0, 168, 150);
    private static readonly Color BgGray = Color.FromArgb(242, 244, 247);

    public MainForm()
    {
        // ── MaterialSkin (child forms + themed controls) ───────────────────
        var skin = MaterialSkinManager.Instance;
        skin.Theme = MaterialSkinManager.Themes.LIGHT;
        skin.ColorScheme = new ColorScheme(
            Color.FromArgb(27,  53,  96),
            Color.FromArgb(15,  32,  64),
            Color.FromArgb(45,  80, 144),
            Color.FromArgb( 0, 168, 150),
            TextShade.WHITE
        );

        // ── Form — borderless ──────────────────────────────────────────────
        FormBorderStyle = FormBorderStyle.None;
        Text            = "Docs2MD";
        MinimumSize     = new Size(875, 800);
        Size            = new Size(1025, 950);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = BgGray;
        AllowDrop       = true;
        DragEnter      += OnDragEnter;
        DragDrop       += OnDragDrop;

        var iconBmp = LoadLogo(64, 43);
        if (iconBmp != null) Icon = Icon.FromHandle(iconBmp.GetHicon());

        // ── Custom title bar ───────────────────────────────────────────────
        Controls.Add(BuildTitleBar());

        // ── Content: 6-row table ───────────────────────────────────────────
        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 6,
            Padding     = new Padding(16, 6, 16, 16),
            BackColor   = Color.Transparent,
            AllowDrop   = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // row 0: viewer buttons
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));  // row 1: file list
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // row 2: file action buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // row 3: options
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // row 4: convert
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));  // row 5: log
        root.DragEnter += OnDragEnter;
        root.DragDrop  += OnDragDrop;
        Controls.Add(root);

        // ── Row 0: Viewer tool buttons ─────────────────────────────────────
        var viewerBar = new FlowLayoutPanel
        {
            AutoSize     = true,
            WrapContents = false,
            Padding      = new Padding(0, 0, 0, 8),
            BackColor    = Color.Transparent
        };
        viewerBar.Controls.AddRange([
            Btn("🖼  Wireframe Viewer",   Style.Outlined, (_, _) => new WireframeViewerForm().Show(this)),
            Btn("📄  MD Viewer / Export", Style.Outlined, (_, _) => new MdViewerForm().Show(this)),
        ]);
        root.Controls.Add(viewerBar, 0, 0);

        // ── Row 1: File list ───────────────────────────────────────────────
        var fileCard = Card(allowDrop: true);
        fileCard.DragEnter += OnDragEnter;
        fileCard.DragDrop  += OnDragDrop;

        var fileLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = Color.Transparent
        };
        fileLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fileLayout.Controls.Add(SectionLabel("FILES TO CONVERT  —  drag & drop PDF / DOCX here"), 0, 0);

        _fileList = new ListBox
        {
            Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended,
            HorizontalScrollbar = true, BorderStyle = BorderStyle.None,
            BackColor = Color.White, ForeColor = Color.FromArgb(33, 33, 33),
            Font = new Font("Segoe UI", 10f), AllowDrop = true, IntegralHeight = false
        };
        _fileList.DragEnter += OnDragEnter;
        _fileList.DragDrop  += OnDragDrop;
        fileLayout.Controls.Add(_fileList, 0, 1);
        fileCard.Controls.Add(fileLayout);
        root.Controls.Add(fileCard, 0, 1);

        // ── Row 2: File action buttons ─────────────────────────────────────
        var fileButtons = new FlowLayoutPanel
        {
            AutoSize  = true, WrapContents = false,
            Padding   = new Padding(0, 7, 0, 7),
            BackColor = Color.Transparent
        };
        fileButtons.Controls.AddRange([
            Btn("+ Add Files",  Style.Outlined, (_, _) => AddFilesDialog()),
            Btn("+ Add Folder", Style.Outlined, (_, _) => AddFolderDialog()),
            Btn("Remove",       Style.Text,     (_, _) => RemoveSelected()),
            Btn("Clear All",    Style.Text,     (_, _) => _fileList.Items.Clear()),
        ]);
        root.Controls.Add(fileButtons, 0, 2);

        // ── Row 3: Options ─────────────────────────────────────────────────
        var optCard   = Card();
        var optLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2,
            BackColor = Color.Transparent
        };

        _ocrEnabled = new CheckBox
        {
            Text = "OCR scanned pages", Checked = true, AutoSize = true,
            Font = new Font("Segoe UI", 10f), ForeColor = Color.FromArgb(33, 33, 33)
        };
        _forceOcr = new CheckBox
        {
            Text = "Force OCR on all pages", AutoSize = true,
            Font = new Font("Segoe UI", 10f), ForeColor = Color.FromArgb(33, 33, 33),
            Margin = new Padding(14, 0, 0, 0)
        };

        var ocrRow = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false,
            Padding = new Padding(0, 2, 0, 4)
        };
        ocrRow.Controls.AddRange([
            _ocrEnabled, _forceOcr,
            new Label { Text = "Language:", AutoSize = true, Font = new Font("Segoe UI", 10f), Margin = new Padding(20, 4, 6, 0) },
            (_ocrLang = new TextBox { Width = 70, Text = "eng", Margin = new Padding(0, 2, 0, 0) })
        ]);

        var outRow = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false,
            Padding = new Padding(0, 8, 0, 2)     // ← top padding creates the gap
        };
        outRow.Controls.AddRange([
            new Label { Text = "Output folder:", AutoSize = true, Font = new Font("Segoe UI", 10f), Margin = new Padding(0, 5, 8, 0) },
            (_outputFolder = new TextBox { Width = 400, PlaceholderText = "(same folder as each input file)", Margin = new Padding(0, 2, 0, 0) }),
            Btn("Browse…", Style.Outlined, (_, _) => BrowseOutputFolder())
        ]);

        optLayout.Controls.Add(ocrRow, 0, 0);
        optLayout.Controls.Add(outRow, 0, 1);
        optCard.Controls.Add(optLayout);
        root.Controls.Add(optCard, 0, 3);

        // ── Row 4: Convert ─────────────────────────────────────────────────
        var convCard   = Card();
        var convLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2,
            BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 4)
        };
        convLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        convLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _convertButton = Btn("▶  CONVERT", Style.Contained, async (_, _) => await ConvertOrCancelAsync());
        _convertButton.Margin = new Padding(0, 0, 16, 0);

        _progress = new MaterialProgressBar
        {
            Dock    = DockStyle.Fill,
            Value   = 0,
            Visible = false,                        // hidden until conversion starts
            Margin  = new Padding(0, 10, 0, 10)
        };

        convLayout.Controls.Add(_convertButton, 0, 0);
        convLayout.Controls.Add(_progress, 1, 0);
        convCard.Controls.Add(convLayout);
        root.Controls.Add(convCard, 0, 4);

        // ── Row 5: Log ─────────────────────────────────────────────────────
        var logCard = Card(allowDrop: true);
        logCard.DragEnter += OnDragEnter;
        logCard.DragDrop  += OnDragDrop;

        var logLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = Color.Transparent
        };
        logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logLayout.Controls.Add(SectionLabel("LOG"), 0, 0);

        _log = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
            BorderStyle = BorderStyle.None,
            BackColor = Color.White, ForeColor = Color.FromArgb(33, 33, 33)
        };
        logLayout.Controls.Add(_log, 0, 1);
        logCard.Controls.Add(logLayout);
        root.Controls.Add(logCard, 0, 5);

        Log.Sink = AppendLog;
    }

    // ── Custom title bar ───────────────────────────────────────────────────

    private Panel BuildTitleBar()
    {
        var bar = new Panel { Height = 52, Dock = DockStyle.Top, BackColor = Navy };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Navy,
            Margin = new Padding(0), Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // logo + title
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // spacer
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // win buttons

        // Logo + title (left side)
        var left = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false,
            BackColor = Navy, Dock = DockStyle.Fill,
            Padding = new Padding(10, 0, 0, 0)
        };
        var logoBmp = LoadLogo(64, 43);
        if (logoBmp != null)
        {
            var logoPb = new PictureBox
            {
                Image = logoBmp, SizeMode = PictureBoxSizeMode.AutoSize,
                BackColor = Navy, Margin = new Padding(0, 4, 6, 4)
            };
            logoPb.MouseDown += TitleBarDrag;
            left.Controls.Add(logoPb);
        }
        var titleLbl = new Label
        {
            Text = "Docs2MD", AutoSize = true,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.White, BackColor = Navy,
            Margin = new Padding(0, 13, 0, 0)
        };
        titleLbl.MouseDown += TitleBarDrag;
        left.Controls.Add(titleLbl);
        left.MouseDown += TitleBarDrag;
        layout.Controls.Add(left, 0, 0);

        // Spacer (for drag)
        var spacer = new Label { Dock = DockStyle.Fill, BackColor = Navy };
        spacer.MouseDown += TitleBarDrag;
        layout.Controls.Add(spacer, 1, 0);

        // Window control buttons (right side)
        var minBtn   = WinBtn("─", hoverRed: false);
        var maxBtn   = WinBtn("□", hoverRed: false);
        var closeBtn = WinBtn("✕", hoverRed: true);

        minBtn.Click   += (_, _) => WindowState = FormWindowState.Minimized;
        maxBtn.Click   += (_, _) => WindowState =
            WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        closeBtn.Click += (_, _) => Close();

        var winBtns = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false,
            BackColor = Navy, Dock = DockStyle.Fill
        };
        winBtns.Controls.AddRange([minBtn, maxBtn, closeBtn]);
        layout.Controls.Add(winBtns, 2, 0);

        bar.Controls.Add(layout);
        bar.MouseDown    += TitleBarDrag;
        bar.DoubleClick  += (_, _) => maxBtn.PerformClick();
        titleLbl.DoubleClick += (_, _) => maxBtn.PerformClick();

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
            Width     = 46,
            Height    = 52,
            FlatStyle = FlatStyle.Flat,
            BackColor = Navy,
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 10f),
            Cursor    = Cursors.Arrow
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = hoverRed
            ? Color.FromArgb(196, 43, 28)
            : Color.FromArgb(50, 80, 130);
        return b;
    }

    // ── Resize support for borderless form ────────────────────────────────

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            int lp = m.LParam.ToInt32();
            int sx = lp & 0xFFFF;        if (sx > 32767) sx -= 65536;
            int sy = (lp >> 16) & 0xFFFF; if (sy > 32767) sy -= 65536;
            var pt = PointToClient(new Point(sx, sy));
            int x = pt.X, y = pt.Y, w = ClientSize.Width, h = ClientSize.Height;
            const int t = 6;

            if      (x < t   && y < t)    { m.Result = (IntPtr)13; return; }
            else if (x >= w-t && y < t)   { m.Result = (IntPtr)14; return; }
            else if (x < t   && y >= h-t) { m.Result = (IntPtr)16; return; }
            else if (x >= w-t && y >= h-t){ m.Result = (IntPtr)17; return; }
            else if (y < t)               { m.Result = (IntPtr)12; return; }
            else if (y >= h-t)            { m.Result = (IntPtr)15; return; }
            else if (x < t)               { m.Result = (IntPtr)10; return; }
            else if (x >= w-t)            { m.Result = (IntPtr)11; return; }
        }
        base.WndProc(ref m);
    }

    // ── Logo loading ───────────────────────────────────────────────────────

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

    // ── UI helpers ─────────────────────────────────────────────────────────

    private static Panel Card(bool allowDrop = false) => new Panel
    {
        Dock      = DockStyle.Fill,
        BackColor = Color.White,
        Padding   = new Padding(14, 10, 14, 10),
        Margin    = new Padding(0, 0, 0, 7),
        AllowDrop = allowDrop
    };

    private static Label SectionLabel(string text) => new Label
    {
        Text      = text,
        Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
        ForeColor = Color.FromArgb(100, 120, 155),
        AutoSize  = true,
        Padding   = new Padding(0, 0, 0, 4)
    };

    private enum Style { Outlined, Text, Contained }

    private static Button Btn(string text, Style style, EventHandler onClick)
    {
        var b = new Button
        {
            Text      = text,
            AutoSize  = true,
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 10f),
            Cursor    = Cursors.Hand,
            Margin    = new Padding(0, 0, 8, 0),
            Padding   = new Padding(12, 6, 12, 6)
        };
        switch (style)
        {
            case Style.Outlined:
                b.BackColor = Color.White;
                b.ForeColor = Navy;
                b.FlatAppearance.BorderColor = Navy;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 240, 250);
                break;
            case Style.Text:
                b.BackColor = Color.White;
                b.ForeColor = Navy;
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(235, 240, 250);
                break;
            case Style.Contained:
                b.BackColor = Teal;
                b.ForeColor = Color.White;
                b.Font      = new Font("Segoe UI", 10.5f, FontStyle.Bold);
                b.Padding   = new Padding(18, 8, 18, 8);
                b.FlatAppearance.BorderColor = Teal;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 145, 130);
                break;
        }
        b.Click += onClick;
        return b;
    }

    // ── File handling ──────────────────────────────────────────────────────

    private static readonly string[] Extensions = [".pdf", ".docx"];

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            AddPaths(paths);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var ext in Extensions)
                    AddPaths(Directory.EnumerateFiles(path, "*" + ext, SearchOption.AllDirectories));
            }
            else if (File.Exists(path)
                     && Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())
                     && !_fileList.Items.Contains(path))
            {
                _fileList.Items.Add(path);
            }
        }
    }

    private void AddFilesDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Documents (*.pdf;*.docx)|*.pdf;*.docx|All files (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = ValidFolderOrEmpty(_settings.LastInputFolder)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddPaths(dialog.FileNames);
            RememberInputFolder(Path.GetDirectoryName(dialog.FileNames.FirstOrDefault()));
        }
    }

    private void AddFolderDialog()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Add all PDF/DOCX files under a folder",
            InitialDirectory = ValidFolderOrEmpty(_settings.LastInputFolder)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddPaths([dialog.SelectedPath]);
            RememberInputFolder(dialog.SelectedPath);
        }
    }

    private void RememberInputFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder)) return;
        _settings.LastInputFolder = folder;
        _settings.Save();
    }

    private static string ValidFolderOrEmpty(string? folder) =>
        !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : "";

    private void RemoveSelected()
    {
        foreach (var item in _fileList.SelectedItems.Cast<object>().ToList())
            _fileList.Items.Remove(item);
    }

    private void BrowseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Folder for the converted .md files",
            InitialDirectory = ValidFolderOrEmpty(_settings.LastOutputFolder)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputFolder.Text = dialog.SelectedPath;
            _settings.LastOutputFolder = dialog.SelectedPath;
            _settings.Save();
        }
    }

    // ── Conversion ─────────────────────────────────────────────────────────

    private async Task ConvertOrCancelAsync()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            return;
        }

        var files = _fileList.Items.Cast<string>().ToList();
        if (files.Count == 0)
        {
            MessageBox.Show(this, "Add at least one PDF or DOCX file first.", "Docs2MD",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string outputFolder = _outputFolder.Text.Trim();
        if (outputFolder.Length > 0)
        {
            try { Directory.CreateDirectory(outputFolder); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot create output folder:\n" + ex.Message, "Docs2MD",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        var options = new ConvertOptions
        {
            OcrEnabled  = _ocrEnabled.Checked,
            ForceOcr    = _forceOcr.Checked,
            OcrLanguage = _ocrLang.Text.Trim().Length > 0 ? _ocrLang.Text.Trim() : "eng"
        };

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _convertButton.Text      = "⏹  CANCEL";
        _convertButton.BackColor = Color.FromArgb(180, 50, 50);
        _convertButton.FlatAppearance.BorderColor = Color.FromArgb(180, 50, 50);
        _progress.Visible = true;
        _progress.Value   = 0;
        _progress.Maximum = files.Count;

        int ok = 0, failed = 0;
        try
        {
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                string target = outputFolder.Length > 0
                    ? Path.Combine(outputFolder, Path.ChangeExtension(Path.GetFileName(file), ".md"))
                    : Path.ChangeExtension(file, ".md");

                AppendLog($"Converting: {file}");
                try
                {
                    string markdown = await Task.Run(() =>
                        Path.GetExtension(file).ToLowerInvariant() == ".docx"
                            ? DocxToMarkdown.Convert(file)
                            : PdfToMarkdown.Convert(file, options), token);

                    await File.WriteAllTextAsync(target, markdown, token);
                    AppendLog($"  OK -> {target}");
                    ok++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppendLog($"  FAILED: {ex.Message}");
                    failed++;
                }
                _progress.Value = Math.Min(_progress.Value + 1, _progress.Maximum);
            }
            AppendLog($"Done. {ok} converted, {failed} failed.");
        }
        catch (OperationCanceledException)
        {
            AppendLog("Cancelled.");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _convertButton.Text      = "▶  CONVERT";
            _convertButton.BackColor = Teal;
            _convertButton.FlatAppearance.BorderColor = Teal;
            _progress.Visible = false;
        }
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), message); return; }
        _log.AppendText(message + Environment.NewLine);
    }
}
