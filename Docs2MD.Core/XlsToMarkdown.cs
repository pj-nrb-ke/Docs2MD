using System.Text;
using ExcelDataReader;

namespace Docs2MD;

/// <summary>
/// Legacy XLS (binary BIFF) → Markdown via ExcelDataReader, emitting the same
/// per-sheet table format as XlsxToMarkdown. ExcelDataReader exposes raw cell
/// values rather than display-formatted strings.
/// ponytail: XLS cells render via ToString (no number-format strings); add a
/// formatter only if XLS date/number fidelity turns out to matter.
/// </summary>
public static class XlsToMarkdown
{
    static XlsToMarkdown() =>
        // XLS uses legacy code pages; register the provider so ExcelDataReader can decode text.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Convert(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var sheets = new List<(string, IReadOnlyList<IReadOnlyList<string>>)>();
        do
        {
            var rows = new List<IReadOnlyList<string>>();
            while (reader.Read())
            {
                var cells = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                    cells[i] = reader.GetValue(i)?.ToString() ?? "";
                rows.Add(cells);
            }
            if (rows.Count > 0) sheets.Add((reader.Name, rows));
        } while (reader.NextResult());

        return SheetMarkdown.Write(sheets);
    }
}
