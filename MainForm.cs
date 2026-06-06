namespace Docs2MD;

public sealed class MainForm : Form
{
    private readonly ListBox _fileList;
    private readonly TextBox _outputFolder;
    private readonly CheckBox _ocrEnabled;
    private readonly CheckBox _forceOcr;
    private readonly TextBox _ocrLang;
    private readonly Button _convertButton;
    private readonly ProgressBar _progress;
    private readonly TextBox _log;
    private CancellationTokenSource? _cts;
    private readonly UserSettings _settings = UserSettings.Load();

    public MainForm()
    {
        Text = "Docs2MD — PDF / DOCX to Markdown";
        MinimumSize = new Size(640, 560);
        Size = new Size(720, 620);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));   // file list
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // file buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // options
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // convert + progress
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));   // log
        Controls.Add(root);

        // --- Row 0: file list -------------------------------------------------
        var listGroup = new GroupBox { Text = "Files to convert (drag && drop PDF / DOCX here)", Dock = DockStyle.Fill };
        _fileList = new ListBox
        {
            Dock = DockStyle.Fill,
            SelectionMode = SelectionMode.MultiExtended,
            HorizontalScrollbar = true,
            AllowDrop = true,
            IntegralHeight = false
        };
        _fileList.DragEnter += OnDragEnter;
        _fileList.DragDrop += OnDragDrop;
        listGroup.Controls.Add(_fileList);
        root.Controls.Add(listGroup, 0, 0);

        // --- Row 1: file buttons ---------------------------------------------
        var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var addFiles = MakeButton("Add files…", (_, _) => AddFilesDialog());
        var addFolder = MakeButton("Add folder…", (_, _) => AddFolderDialog());
        var removeSel = MakeButton("Remove selected", (_, _) => RemoveSelected());
        var clearAll = MakeButton("Clear", (_, _) => _fileList.Items.Clear());
        var sep = new Label { Text = "│", AutoSize = true, Margin = new Padding(8, 7, 8, 3), ForeColor = Color.Silver };
        var wireframeViewer = MakeButton("🖼 Wireframe Viewer…", (_, _) => new WireframeViewerForm().Show(this));
        fileButtons.Controls.AddRange([addFiles, addFolder, removeSel, clearAll, sep, wireframeViewer]);
        root.Controls.Add(fileButtons, 0, 1);

        // --- Row 2: options (two fixed rows so nothing is cut off at any width) --
        var optionsGroup = new GroupBox { Text = "Options", Dock = DockStyle.Fill, AutoSize = true };
        var optionsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(4)
        };

        // Options row 1: output folder
        var folderRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        folderRow.Controls.Add(new Label { Text = "Output folder:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        _outputFolder = new TextBox { Width = 320, PlaceholderText = "(same folder as each input file)" };
        folderRow.Controls.Add(_outputFolder);
        folderRow.Controls.Add(MakeButton("Browse…", (_, _) => BrowseOutputFolder()));
        optionsTable.Controls.Add(folderRow, 0, 0);

        // Options row 2: OCR settings
        var ocrRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _ocrEnabled = new CheckBox { Text = "OCR scanned pages", Checked = true, AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        _forceOcr = new CheckBox { Text = "Force OCR on all PDF pages", AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
        ocrRow.Controls.Add(_ocrEnabled);
        ocrRow.Controls.Add(_forceOcr);
        ocrRow.Controls.Add(new Label { Text = "OCR language:", AutoSize = true, Margin = new Padding(12, 8, 3, 3) });
        _ocrLang = new TextBox { Width = 80, Text = "eng" };
        ocrRow.Controls.Add(_ocrLang);
        optionsTable.Controls.Add(ocrRow, 0, 1);

        optionsGroup.Controls.Add(optionsTable);
        root.Controls.Add(optionsGroup, 0, 2);

        // --- Row 3: convert + progress -----------------------------------------
        var actionRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _convertButton = new Button { Text = "Convert", AutoSize = true, Padding = new Padding(16, 4, 16, 4) };
        _convertButton.Click += async (_, _) => await ConvertOrCancelAsync();
        _progress = new ProgressBar { Dock = DockStyle.Fill, Margin = new Padding(8, 6, 0, 6) };
        actionRow.Controls.Add(_convertButton, 0, 0);
        actionRow.Controls.Add(_progress, 1, 0);
        root.Controls.Add(actionRow, 0, 3);

        // --- Row 4: log ---------------------------------------------------------
        var logGroup = new GroupBox { Text = "Log", Dock = DockStyle.Fill };
        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 8.5f)
        };
        logGroup.Controls.Add(_log);
        root.Controls.Add(logGroup, 0, 4);

        Log.Sink = AppendLog;
    }

    // ---------- file handling ----------

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

    // ---------- conversion ----------

    private async Task ConvertOrCancelAsync()
    {
        if (_cts is not null) // running -> cancel
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
            OcrEnabled = _ocrEnabled.Checked,
            ForceOcr = _forceOcr.Checked,
            OcrLanguage = _ocrLang.Text.Trim().Length > 0 ? _ocrLang.Text.Trim() : "eng"
        };

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _convertButton.Text = "Cancel";
        _progress.Value = 0;
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
            _convertButton.Text = "Convert";
        }
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += onClick;
        return button;
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), message);
            return;
        }
        _log.AppendText(message + Environment.NewLine);
    }
}
