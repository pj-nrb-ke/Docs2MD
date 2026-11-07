using ClosedXML.Excel;
using System.Text;

namespace Docs2MD;

/// <summary>
/// XLSX → Markdown. Each worksheet becomes a "## SheetName" heading followed by
/// a GitHub pipe table built from the sheet's used range; the first used row is
/// the header. Cell text is taken as displayed (number/date formats applied),
/// with pipes escaped and newlines flattened so one cell stays one table cell.
/// </summary>
public static class XlsxToMarkdown
{
    public static string Convert(string path)
    {
        using var wb = new XLWorkbook(path);
        var sb = new StringBuilder();

        foreach (var ws in wb.Worksheets)
        {
            var range = ws.RangeUsed();
            if (range is null) continue;   // empty sheet

            int cols = range.ColumnCount();
            var rows = range.Rows().ToList();

            if (sb.Length > 0) sb.AppendLine();
            sb.Append("## ").AppendLine(ws.Name).AppendLine();

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

    private static void AppendRow(StringBuilder sb, IXLRangeRow row, int cols)
    {
        sb.Append('|');
        for (int c = 1; c <= cols; c++)
            sb.Append(' ').Append(Cell(row.Cell(c))).Append(" |");
        sb.AppendLine();
    }

    // Escape pipes and flatten newlines so a cell can't break the table row.
    private static string Cell(IXLCell cell) =>
        cell.GetFormattedString()
            .Replace("|", "\\|")
            .Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ')
            .Trim();
}
