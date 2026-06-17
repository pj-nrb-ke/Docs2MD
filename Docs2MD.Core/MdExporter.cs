namespace Docs2MD;

using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

// Aliases so Markdig table types don't clash with OpenXML Wordprocessing types.
using MdTable     = Markdig.Extensions.Tables.Table;
using MdTableRow  = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;

/// <summary>
/// Converts Markdown to Word (.docx) or Excel (.xlsx).
///
/// Word:  walks the Markdig AST and builds an OpenXML WordprocessingDocument
///        using built-in heading/body styles. Handles headings, paragraphs,
///        bold, italic, inline code, bullet and ordered lists, tables, code
///        blocks, and horizontal rules.
///
/// Excel: extracts every markdown table into its own worksheet (headers bolded,
///        columns auto-sized, first row frozen). Falls back to a text dump when
///        no tables exist.
/// </summary>
public static class MdExporter
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // ════════════════════════════════════════════════════════════════════
    //  Word export
    // ════════════════════════════════════════════════════════════════════

    public static void ToWord(string markdown, string outputPath)
    {
        var mdDoc = Markdown.Parse(NormalizeMarkdown(markdown), Pipeline);

        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = wordDoc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            foreach (var block in mdDoc)
                AppendBlock(body, block, orderedCounters: new Stack<int>());

            // Word requires the body to end with a paragraph
            body.AppendChild(new Paragraph());
            main.Document.Save();
        }

        File.WriteAllBytes(outputPath, ms.ToArray());
    }

    // ── block dispatch ───────────────────────────────────────────────────

    private static void AppendBlock(Body body, Block block, Stack<int> orderedCounters)
    {
        switch (block)
        {
            case HeadingBlock h:
                body.AppendChild(HeadingParagraph(h));
                break;

            case ParagraphBlock p:
                body.AppendChild(InlineParagraph(p.Inline, "Normal"));
                break;

            case ListBlock list:
                AppendList(body, list, depth: 0);
                break;

            case MdTable table:
                body.AppendChild(BuildWordTable(table));
                break;

            case FencedCodeBlock code:
            case CodeBlock code2:
                AppendCodeBlock(body, block);
                break;

            case ThematicBreakBlock:
                body.AppendChild(HorizontalRule());
                break;

            case ContainerBlock container:
                foreach (var child in container)
                    AppendBlock(body, child, orderedCounters);
                break;
        }
    }

    // ── headings ─────────────────────────────────────────────────────────

    private static Paragraph HeadingParagraph(HeadingBlock h)
    {
        var style = $"Heading{Math.Clamp(h.Level, 1, 6)}";
        var para  = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = style }));

        if (h.Inline != null)
            foreach (var run in InlineRuns(h.Inline))
                para.AppendChild(run);

        return para;
    }

    // ── paragraphs with inline formatting ────────────────────────────────

    private static Paragraph InlineParagraph(ContainerInline? inlines, string style = "Normal")
    {
        var para = new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = style }));
        if (inlines != null)
            foreach (var run in InlineRuns(inlines))
                para.AppendChild(run);
        return para;
    }

    private static IEnumerable<OpenXmlElement> InlineRuns(ContainerInline inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    yield return PlainRun(lit.Content.ToString());
                    break;

                case EmphasisInline em:
                    bool isBold   = em.DelimiterChar == '*' && em.DelimiterCount == 2;
                    bool isItalic = (em.DelimiterChar == '*' || em.DelimiterChar == '_') && em.DelimiterCount == 1;
                    var  rp       = new RunProperties();
                    if (isBold)   rp.AppendChild(new Bold());
                    if (isItalic) rp.AppendChild(new Italic());
                    foreach (var child in InlineRuns(em))
                    {
                        if (child is Run r) { r.PrependChild(rp.CloneNode(true)); yield return r; }
                        else yield return child;
                    }
                    break;

                case CodeInline code:
                    var codeRun = new Run(
                        new RunProperties(new RunFonts { Ascii = "Courier New", HighAnsi = "Courier New" },
                                          new FontSize { Val = "18" }),
                        new Text(code.Content.ToString()) { Space = SpaceProcessingModeValues.Preserve });
                    yield return codeRun;
                    break;

                case LineBreakInline:
                    yield return new Run(new Break());
                    break;

                case ContainerInline container:
                    foreach (var r in InlineRuns(container)) yield return r;
                    break;
            }
        }
    }

    private static Run PlainRun(string text) =>
        new(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

    // ── lists ────────────────────────────────────────────────────────────

    private static void AppendList(Body body, ListBlock list, int depth)
    {
        int counter = list.IsOrdered ? 1 : 0;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            foreach (var child in item)
            {
                if (child is ParagraphBlock para)
                {
                    var prefix = list.IsOrdered ? $"{counter}. " : "• ";
                    var p      = InlineParagraph(para.Inline, "ListParagraph");

                    // Indent by depth
                    p.ParagraphProperties!.AppendChild(new Indentation
                    {
                        Left    = StringValue.FromString(((depth + 1) * 720).ToString()),
                        Hanging = "360"
                    });

                    // Prepend prefix as first run
                    p.PrependChild(PlainRun(prefix));
                    body.AppendChild(p);
                }
                else if (child is ListBlock nested)
                {
                    AppendList(body, nested, depth + 1);
                }
            }
            counter++;
        }
    }

    // ── code blocks ──────────────────────────────────────────────────────

    private static void AppendCodeBlock(Body body, Block block)
    {
        var lines = block switch
        {
            FencedCodeBlock f => f.Lines.ToString().TrimEnd(),
            CodeBlock c       => c.Lines.ToString().TrimEnd(),
            _                 => ""
        };

        foreach (var line in lines.Split('\n'))
        {
            var p = new Paragraph(
                new ParagraphProperties(
                    new ParagraphStyleId { Val = "Normal" },
                    new Indentation { Left = "720" },
                    new SpacingBetweenLines { Before = "0", After = "0" }),
                new Run(
                    new RunProperties(
                        new RunFonts { Ascii = "Courier New", HighAnsi = "Courier New" },
                        new FontSize { Val = "18" },
                        new Shading { Val = ShadingPatternValues.Clear, Fill = "F3F3F3" }),
                    new Text(line) { Space = SpaceProcessingModeValues.Preserve }));
            body.AppendChild(p);
        }
    }

    // ── tables ───────────────────────────────────────────────────────────

    private static Table BuildWordTable(MdTable mdTable)
    {
        var tbl = new Table(
            new TableProperties(
                new TableStyle { Val = "TableGrid" },
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                new TableBorders(
                    new TopBorder    { Val = BorderValues.Single, Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Size = 4 },
                    new LeftBorder   { Val = BorderValues.Single, Size = 4 },
                    new RightBorder  { Val = BorderValues.Single, Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4 })));

        foreach (var mdRow in mdTable.OfType<MdTableRow>())
        {
            var tr = new TableRow();
            foreach (var mdCell in mdRow.OfType<MdTableCell>())
            {
                var tc = new TableCell();
                var p  = new Paragraph();

                if (mdRow.IsHeader)
                {
                    tc.AppendChild(new TableCellProperties(
                        new Shading { Val = ShadingPatternValues.Clear, Fill = "E8EEF6" }));
                }

                var textRun = new Run(new Text(ExtractText(mdCell)) { Space = SpaceProcessingModeValues.Preserve });
                if (mdRow.IsHeader) textRun.PrependChild(new RunProperties(new Bold()));

                p.AppendChild(textRun);
                tc.AppendChild(p);
                tr.AppendChild(tc);
            }
            tbl.AppendChild(tr);
        }

        return tbl;
    }

    // ── horizontal rule ──────────────────────────────────────────────────

    private static Paragraph HorizontalRule() =>
        new(new ParagraphProperties(
            new ParagraphBorders(
                new BottomBorder { Val = BorderValues.Single, Size = 6, Color = "AAAAAA" })));

    // ════════════════════════════════════════════════════════════════════
    //  Excel export
    // ════════════════════════════════════════════════════════════════════

    public static void ToExcel(string markdown, string outputPath)
    {
        var doc    = Markdown.Parse(NormalizeMarkdown(markdown), Pipeline);
        var tables = doc.Descendants<MdTable>().ToList();

        using var wb = new XLWorkbook();

        if (tables.Count > 0)
        {
            int n = 1;
            foreach (var table in tables)
                WriteTableSheet(wb, table, $"Table {n++}");
        }
        else
        {
            // No markdown tables — write raw text, one line per row
            var ws  = wb.Worksheets.Add("Content");
            int row = 1;
            foreach (var line in markdown.Split('\n'))
                ws.Cell(row++, 1).Value = line.TrimEnd();
            ws.Column(1).Width = 80;
        }

        wb.SaveAs(outputPath);
    }

    private static void WriteTableSheet(XLWorkbook wb, MdTable table, string name)
    {
        var ws  = wb.Worksheets.Add(name);
        int row = 1;

        foreach (var tableRow in table.OfType<MdTableRow>())
        {
            int col = 1;
            foreach (var cell in tableRow.OfType<MdTableCell>())
            {
                var xlCell = ws.Cell(row, col);
                xlCell.Value = ExtractText(cell);

                if (tableRow.IsHeader)
                {
                    xlCell.Style.Font.Bold            = true;
                    xlCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#d6e4f0");
                    xlCell.Style.Border.BottomBorder  = XLBorderStyleValues.Medium;
                    xlCell.Style.Border.BottomBorderColor = XLColor.FromHtml("#4a72a8");
                }
                else
                {
                    xlCell.Style.Border.BottomBorder      = XLBorderStyleValues.Thin;
                    xlCell.Style.Border.BottomBorderColor = XLColor.FromHtml("#cccccc");
                }
                col++;
            }
            row++;
        }

        ws.Columns().AdjustToContents();
        if (row > 1) ws.SheetView.FreezeRows(1);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Shared helpers
    // ════════════════════════════════════════════════════════════════════

    private static string ExtractText(ContainerBlock block)
    {
        var sb = new StringBuilder();
        foreach (var inline in block.Descendants<LiteralInline>())
            sb.Append(inline.Content.ToString());
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Inserts a blank line before list items that directly follow a paragraph
    /// so Markdig reliably parses inline bold/italic inside list items.
    /// </summary>
    public static string NormalizeMarkdown(string md) =>
        System.Text.RegularExpressions.Regex.Replace(
            md, @"([^\n])\n([ \t]*[-*+] )", "$1\n\n$2",
            System.Text.RegularExpressions.RegexOptions.Multiline);
}
