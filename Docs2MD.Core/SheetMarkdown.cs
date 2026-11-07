using System.Text;

namespace Docs2MD;

/// <summary>
/// Shared renderer: turns worksheets (each a list of string rows) into one
/// "## SheetName" heading + GitHub pipe table per sheet, first row as header,
/// pipes escaped and newlines flattened. Used by both the XLSX and XLS paths so
/// their output is identical.
/// </summary>
internal static class SheetMarkdown
{
    public static string Write(IEnumerable<(string Name, IReadOnlyList<IReadOnlyList<string>> Rows)> sheets)
    {
        var sb = new StringBuilder();
        foreach (var (name, rows) in sheets)
        {
            if (rows.Count == 0) continue;
            int cols = rows.Max(r => r.Count);
            if (cols == 0) continue;

            if (sb.Length > 0) sb.AppendLine();
            sb.Append("## ").AppendLine(name).AppendLine();

            AppendRow(sb, rows[0], cols);                       // header
            sb.Append('|');
            for (int c = 0; c < cols; c++) sb.Append(" --- |");
            sb.AppendLine();
            for (int r = 1; r < rows.Count; r++)
                AppendRow(sb, rows[r], cols);

            sb.AppendLine();
        }
        return MarkdownCleaner.Tidy(sb.ToString());
    }

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string> row, int cols)
    {
        sb.Append('|');
        for (int c = 0; c < cols; c++)
            sb.Append(' ').Append(Escape(c < row.Count ? row[c] : "")).Append(" |");
        sb.AppendLine();
    }

    // Escape pipes and flatten newlines so a cell can't break the table row.
    private static string Escape(string s) =>
        s.Replace("|", "\\|")
         .Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ')
         .Trim();
}
