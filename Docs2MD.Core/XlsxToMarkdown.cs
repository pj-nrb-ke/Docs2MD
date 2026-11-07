using ClosedXML.Excel;

namespace Docs2MD;

/// <summary>
/// XLSX → Markdown. Each worksheet becomes a "## SheetName" heading followed by
/// a GitHub pipe table built from the sheet's used range; the first used row is
/// the header. Cell text is taken as displayed (number/date formats applied).
/// </summary>
public static class XlsxToMarkdown
{
    public static string Convert(string path)
    {
        using var wb = new XLWorkbook(path);
        var sheets = new List<(string, IReadOnlyList<IReadOnlyList<string>>)>();

        foreach (var ws in wb.Worksheets)
        {
            var range = ws.RangeUsed();
            if (range is null) continue;   // empty sheet

            int cols = range.ColumnCount();
            var rows = new List<IReadOnlyList<string>>();
            foreach (var row in range.Rows())
            {
                var cells = new string[cols];
                for (int c = 1; c <= cols; c++)
                    cells[c - 1] = row.Cell(c).GetFormattedString();
                rows.Add(cells);
            }
            sheets.Add((ws.Name, rows));
        }

        return SheetMarkdown.Write(sheets);
    }
}
