namespace Docs2MD;

using System.Reflection;
using System.Runtime.InteropServices;
using MaterialSkin;
using MaterialSkin.Controls;
using Svg;

public sealed class MainForm : Form
{
    // ── P/Invoke ───────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern int  SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
    [DllImport("shell32.dll")] private static extern void DragAcceptFiles(IntPtr hWnd, bool accept);
    [DllImport("shell32.dll")] private static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, System.Text.StringBuilder? lpszFile, uint cch);
    [DllImport("shell32.dll")] private static extern void DragFinish(IntPtr hDrop);
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION       = 0x02;
    private const int WM_DROPFILES     = 0x0233;

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
        // ── MaterialSkin ───────────────────────────────────────────────────
        var skin = MaterialSkinManager.Instance;
        skin.Theme       = MaterialSkinManager.Themes.LIGHT;
        skin.ColorScheme = new ColorScheme(
            Color.FromArgb(27, 53, 96), Color.FromArgb(15, 32, 64),
            Color.FromArgb(45, 80, 144), Color.FromArgb(0, 168, 150),
            TextShade.WHITE);

        // ── Form ───────────────────────────────────────────────────────────
        FormBorderStyle = FormBorderStyle.None;
        Text            = "Docs2MD";
        MinimumSize     = new Size(780, 660);
        Size            = new Size(960, 820);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = BgGray;
        AllowDrop       = true;
        DragEnter      += OnDragEnter;
        DragDrop       += OnDragDrop;

        var iconBmp = LoadLogo(64, 43);
        if (iconBmp != null) Icon = Icon.FromHandle(iconBmp.GetHicon());

        // ── Outer shell: rows for title bar / launcher / content ───────────
        // Using an explicit TLP avoids all DockStyle stacking ambiguity.
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = Padding.Empty, Margin = Padding.Empty, BackColor = BgGray
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));    // title bar
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));    // launcher bar
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // content
        Controls.Add(outer);

        outer.Controls.Add(BuildTitleBar(),    0, 0);
        outer.Controls.Add(BuildLauncherBar(), 0, 1);

        // ── Content: 4 rows — ALL Absolute or Percent, zero AutoSize rows.
        // AutoSize rows + Dock=Fill children = 0-height collapse; avoid entirely.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(14, 18, 14, 14), BackColor = Color.Transparent,
            AllowDrop = true
        };
        // Options row height: label(22) + OCR(52) + outputFolder(62) + card padding(16) + card margin(8) = 160
        // Convert row height: button(~34) + top/bottom padding(12+12) = 58 (no card, no margin)
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));   // file card   — grows
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160)); // options card — fixed
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));  // convert row  — fixed
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));   // log card    — grows
        root.DragEnter += OnDragEnter;
        root.DragDrop  += OnDragDrop;
        outer.Controls.Add(root, 0, 2);

        // ── Row 0: File card ───────────────────────────────────────────────
        var fileCard = Card(allowDrop: true);
        fileCard.DragEnter += OnDragEnter;
        fileCard.DragDrop  += OnDragDrop;

        var fileInner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent
        };
        fileInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));    // header
        fileInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));     // separator
        fileInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // list

        // Header: section label left, action buttons right
        var fileHeaderTlp = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent
        };
        fileHeaderTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileHeaderTlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fileHeaderTlp.Controls.Add(new Label
        {
            Text = "FILES TO CONVERT  —  drag & drop PDF / DOCX here",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 120, 155),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true, UseMnemonic = false
        }, 0, 0);

        var fileActions = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false,
            BackColor = Color.White, Margin = Padding.Empty, Padding = Padding.Empty
        };
        fileActions.Controls.AddRange([
            SmallBtn("+ Files",  Style.Outlined, (_, _) => AddFilesDialog()),
            SmallBtn("+ Folder", Style.Outlined, (_, _) => AddFolderDialog()),
            SmallBtn("Remove",   Style.Text,     (_, _) => RemoveSelected()),
            SmallBtn("Clear",    Style.Text,     (_, _) => _fileList.Items.Clear()),
        ]);
        fileHeaderTlp.Controls.Add(fileActions, 1, 0);
        fileInner.Controls.Add(fileHeaderTlp, 0, 0);
        fileInner.Controls.Add(
            new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(226, 230, 240) }, 0, 1);

        _fileList = new ListBox
        {
            Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended,
            HorizontalScrollbar = true, BorderStyle = BorderStyle.None,
            BackColor = Color.White, ForeColor = Color.FromArgb(33, 33, 33),
            Font = new Font("Segoe UI", 10f), AllowDrop = true, IntegralHeight = false
        };
        _fileList.DragEnter += OnDragEnter;
        _fileList.DragDrop  += OnDragDrop;
        fileInner.Controls.Add(_fileList, 0, 2);
        fileCard.Controls.Add(fileInner);
        root.Controls.Add(fileCard, 0, 0);

        // ── Row 1: Options card (120px row → 104px usable after card margin 8px + padding 8+8) ──
        var optCard  = Card();
        var optInner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent
        };
        // All rows Absolute — no AutoSize inside options, ever.
        optInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));  // "OPTIONS" label
        optInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));  // OCR row
        optInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));  // output folder row

        optInner.Controls.Add(SectionLabel("OPTIONS"), 0, 0);

        _ocrEnabled = new CheckBox
        {
            Text = "OCR scanned pages", Checked = true, AutoSize = true,
            Font = new Font("Segoe UI", 10f), ForeColor = Color.FromArgb(33, 33, 33)
        };
        _forceOcr = new CheckBox
        {
            Text = "Force OCR on all pages", AutoSize = true,
            Font = new Font("Segoe UI", 10f), ForeColor = Color.FromArgb(33, 33, 33),
            Margin = new Padding(16, 0, 0, 0)
        };
        var ocrRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.White
        };
        ocrRow.Controls.AddRange([
            _ocrEnabled, _forceOcr,
            new Label { Text = "Language:", AutoSize = true, Font = new Font("Segoe UI", 10f),
                        Margin = new Padding(24, 7, 6, 0) },
            (_ocrLang = new TextBox { Width = 70, Text = "eng", Margin = new Padding(0, 5, 0, 0) })
        ]);
        optInner.Controls.Add(ocrRow, 0, 1);

        // Output folder: TLP with 3 columns so Margin on Browse creates a real gap (docking ignores Margin).
        var outRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = Color.White, Padding = new Padding(0, 10, 0, 10), Margin = Padding.Empty
        };
        outRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112)); // "Output folder:" label
        outRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // TextBox stretches
        outRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Browse button

        var outLbl = new Label
        {
            Text = "Output folder:", Font = new Font("Segoe UI", 10f),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        };
        var browseOut = Btn("Browse…", Style.Outlined, (_, _) => BrowseOutputFolder());
        browseOut.Margin = new Padding(12, 0, 0, 0); // 12px gap between TextBox and Browse
        _outputFolder = new TextBox
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4),
            PlaceholderText = "(same folder as each input file)"
        };
        outRow.Controls.Add(outLbl,         0, 0);
        outRow.Controls.Add(_outputFolder,  1, 0);
        outRow.Controls.Add(browseOut,      2, 0);
        optInner.Controls.Add(outRow, 0, 2);

        optCard.Controls.Add(optInner);
        root.Controls.Add(optCard, 0, 1);

        // ── Row 2: Convert (58px, no card) ────────────────────────────────
        var convRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2,
            BackColor = Color.Transparent, Padding = new Padding(0, 10, 0, 10)
        };
        convRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        convRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _convertButton = Btn("▶  CONVERT", Style.Contained, async (_, _) => await ConvertOrCancelAsync());
        _convertButton.Margin = new Padding(0, 0, 16, 0);

        _progress = new MaterialProgressBar
        {
            Dock = DockStyle.Fill, Value = 0, Visible = false,
            Margin = new Padding(0, 10, 0, 10)
        };
        convRow.Controls.Add(_convertButton, 0, 0);
        convRow.Controls.Add(_progress, 1, 0);
        root.Controls.Add(convRow, 0, 2);

        // ── Row 3: Log card ────────────────────────────────────────────────
        var logCard = Card(allowDrop: true);
        logCard.Margin = new Padding(0, 10, 0, 0); // gap between CONVERT row and LOG card
        logCard.DragEnter += OnDragEnter;
        logCard.DragDrop  += OnDragDrop;

        var logInner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent
        };
        logInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));  // "LOG" label
        logInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));   // separator
        logInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // text area

        logInner.Controls.Add(SectionLabel("LOG"), 0, 0);
        logInner.Controls.Add(
            new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(226, 230, 240),
                        Margin = new Padding(0, 0, 0, 4) }, 0, 1);

        _log = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
            BorderStyle = BorderStyle.None,
            BackColor = Color.White, ForeColor = Color.FromArgb(33, 33, 33)
        };
        logInner.Controls.Add(_log, 0, 2);
        logCard.Controls.Add(logInner);
        root.Controls.Add(logCard, 0, 3);

        Log.Sink = AppendLog;
    }

    // ── Custom title bar ───────────────────────────────────────────────────

    private Panel BuildTitleBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = Navy };

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
            Padding = new Padding(10, 12, 0, 0)
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
            AutoSize = true, WrapContents = false, BackColor = Navy
        };
        winBtns.Controls.AddRange([minBtn, maxBtn, closeBtn]);

        // Wrap so buttons stay vertically centred regardless of title bar height
        var winBtnsWrap = new Panel { Dock = DockStyle.Fill, BackColor = Navy };
        winBtnsWrap.Controls.Add(winBtns);
        winBtnsWrap.Resize += (_, _) =>
            winBtns.Location = new Point(
                winBtnsWrap.Width  - winBtns.Width,
                (winBtnsWrap.Height - winBtns.Height) / 2);
        layout.Controls.Add(winBtnsWrap, 2, 0);

        bar.Controls.Add(layout);
        bar.MouseDown    += TitleBarDrag;
        bar.DoubleClick  += (_, _) => maxBtn.PerformClick();
        titleLbl.DoubleClick += (_, _) => maxBtn.PerformClick();

        return bar;
    }

    private Panel BuildLauncherBar()
    {
        var bar = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.White,
            Padding   = new Padding(16, 8, 16, 14)
        };
        bar.Controls.Add(new Panel
        {
            Height    = 1,
            Dock      = DockStyle.Bottom,
            BackColor = Color.FromArgb(218, 224, 236)
        });

        var flow = new FlowLayoutPanel
        {
            AutoSize     = true,
            WrapContents = false,
            BackColor    = Color.White,
            Dock         = DockStyle.Left
        };
        flow.Controls.AddRange([
            Btn("🖼  Wireframe Viewer",   Style.Outlined, (_, _) => new WireframeViewerForm().Show(this)),
            Btn("📄  MD Viewer / Export", Style.Outlined, (_, _) => new MdViewerForm().Show(this)),
        ]);
        bar.Controls.Add(flow);
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
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = hoverRed
            ? Color.FromArgb(196, 43, 28)
            : Color.FromArgb(50, 80, 130);
        return b;
    }

    // ── Resize support for borderless form ────────────────────────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DragAcceptFiles(Handle, true);  // Enable WM_DROPFILES as fallback for OLE drag-drop
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DROPFILES)
        {
            uint count = DragQueryFileW(m.WParam, 0xFFFFFFFF, null, 0);
            var paths = new List<string>((int)count);
            var sb = new System.Text.StringBuilder(260);
            for (uint i = 0; i < count; i++)
            {
                DragQueryFileW(m.WParam, i, sb, (uint)sb.Capacity);
                paths.Add(sb.ToString());
            }
            DragFinish(m.WParam);
            AddPaths(paths);
            return;
        }

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
        Padding   = new Padding(14, 8, 14, 8),
        Margin    = new Padding(0, 0, 0, 8),
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

    private static Button SmallBtn(string text, Style style, EventHandler onClick)
    {
        var b = Btn(text, style, onClick);
        b.Font    = new Font("Segoe UI", 9f);
        b.Padding = new Padding(8, 4, 8, 4);
        return b;
    }

    private static Button Btn(string text, Style style, EventHandler onClick)
    {
        var b = new Button
        {
            Text                    = text,
            AutoSize                = true,
            FlatStyle               = FlatStyle.Flat,
            Font                    = new Font("Segoe UI", 10f),
            Cursor                  = Cursors.Hand,
            Margin                  = new Padding(0, 0, 8, 0),
            Padding                 = new Padding(12, 6, 12, 6),
            UseVisualStyleBackColor = false
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
        if (dialog.ShowDialog() == DialogResult.OK)
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
        if (dialog.ShowDialog() == DialogResult.OK)
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
        if (dialog.ShowDialog() == DialogResult.OK)
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
