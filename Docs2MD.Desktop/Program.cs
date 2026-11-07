namespace Docs2MD;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Launched with file arguments (the Explorer "Convert to MD here" verb passes
        // the clicked file) → convert headlessly and exit. No args → open the window.
        if (args.Length > 0)
            RunHeadless(args);
        else
            Application.Run(new MainForm());
    }

    // Converts each file and writes <name>.md beside it. Success is silent (the .md
    // just appears next to the source); any failures are collected into one message box.
    private static void RunHeadless(string[] files)
    {
        var options = new ConvertOptions();
        var errors  = new List<string>();

        foreach (var file in files)
        {
            try
            {
                string markdown = DocumentToMarkdown.Convert(file, options);
                File.WriteAllText(Path.ChangeExtension(file, ".md"), markdown);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
            MessageBox.Show(
                string.Join(Environment.NewLine, errors),
                "Docs2MD — conversion errors",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
