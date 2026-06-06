using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Docs2MD;

/// <summary>
/// PDF → Markdown using PdfPig. Words are grouped into lines by baseline,
/// lines into paragraphs by vertical gap. Headings are inferred from font
/// size relative to the document's body-text median; code is detected via
/// monospace fonts and source-code patterns and emitted as fenced blocks.
/// Print headers/footers (timestamps, URL + page n/m) are dropped.
/// Pages with no usable text layer fall back to OCR (see OcrEngine).
/// </summary>
public static partial class PdfToMarkdown
{
    public static string Convert(string path, ConvertOptions options)
    {
        var sb = new StringBuilder();
        using var ocr = options.OcrEnabled ? OcrEngine.TryCreate(options) : null;
        using var document = PdfDocument.Open(path);

        double bodySize = MedianFontSize(document);
        int pageCount = document.NumberOfPages;

        for (int p = 1; p <= pageCount; p++)
        {
            var page = document.GetPage(p);
            var words = page.GetWords().ToList();
            int charCount = words.Sum(w => w.Text.Length);

            bool needsOcr = options.ForceOcr || charCount < options.MinCharsPerPage;
            if (needsOcr && ocr is not null)
            {
                string ocrText = ocr.RecognizePage(path, p - 1);
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    AppendPage(sb, ocrText.Trim(), p, pageCount, ocrUsed: true);
                    continue;
                }
            }

            if (charCount == 0)
            {
                if (needsOcr && ocr is null && options.OcrEnabled)
                    Log.Warn($"  [pdf] page {p}: no text layer and OCR unavailable — page skipped");
                continue;
            }

            string pageMarkdown = RenderPage(words, bodySize);
            if (pageMarkdown.Length > 0)
                AppendPage(sb, pageMarkdown, p, pageCount, ocrUsed: false);
        }

        // Merge code blocks that were split by a page boundary:
        // "```  ---  ```csharp" between pages means the code continues.
        string assembled = SplitCodeFence().Replace(sb.ToString(), "");

        return MarkdownCleaner.Tidy(assembled);
    }

    private static void AppendPage(StringBuilder sb, string content, int page, int total, bool ocrUsed)
    {
        if (sb.Length > 0) sb.AppendLine().AppendLine("---").AppendLine();
        if (ocrUsed) sb.AppendLine($"<!-- page {page}/{total} (OCR) -->").AppendLine();
        sb.AppendLine(content);
    }

    // ---------- text-layer rendering ----------

    private sealed record Line(double Baseline, double FontSize, string Text, bool IsMonospace);

    private static string RenderPage(List<Word> words, double bodySize)
    {
        var lines = GroupIntoLines(words);
        var sb = new StringBuilder();
        double? prevBaseline = null;
        double prevFontSize = bodySize;
        bool inCodeBlock = false;

        foreach (var line in lines)
        {
            string text = line.Text.Trim();
            if (text.Length == 0 || IsPrintArtifact(text)) continue;

            bool isCode = line.IsMonospace || LooksLikeCode(text);

            if (inCodeBlock && !isCode)
            {
                sb.AppendLine("```").AppendLine();
                inCodeBlock = false;
            }

            if (isCode)
            {
                if (!inCodeBlock)
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.AppendLine("```csharp");
                    inCodeBlock = true;
                }
                sb.AppendLine(text);
                prevBaseline = line.Baseline;
                prevFontSize = line.FontSize;
                continue;
            }

            // Paragraph break when the vertical gap clearly exceeds line spacing
            bool paragraphBreak = prevBaseline is double pb &&
                                  (pb - line.Baseline) > Math.Max(line.FontSize, prevFontSize) * 1.8;
            if (sb.Length > 0 && paragraphBreak)
                sb.AppendLine();

            string prefix = HeadingPrefix(line.FontSize, bodySize, text);
            if (prefix.Length > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(prefix).Append(' ').AppendLine(text);
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine(NormalizeBullet(text));
            }

            prevBaseline = line.Baseline;
            prevFontSize = line.FontSize;
        }

        if (inCodeBlock) sb.AppendLine("```");
        return sb.ToString().TrimEnd();
    }

    private static List<Line> GroupIntoLines(List<Word> words)
    {
        var lines = new List<Line>();
        // Sort top-to-bottom (PDF y grows upward), then left-to-right
        foreach (var group in words
                     .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 4.0)) // ~4pt baseline tolerance
                     .OrderByDescending(g => g.Key))
        {
            var ordered = group.OrderBy(w => w.BoundingBox.Left).ToList();
            var letters = ordered.SelectMany(w => w.Letters).ToList();
            double baseline = ordered.Average(w => w.BoundingBox.Bottom);
            double size = letters.Select(l => l.PointSize).DefaultIfEmpty(10).Average();
            int monoCount = letters.Count(l => IsMonospaceFont(l.FontName));
            bool mono = letters.Count > 0 && monoCount > letters.Count / 2;
            lines.Add(new Line(baseline, size, string.Join(' ', ordered.Select(w => w.Text)), mono));
        }
        return lines;
    }

    private static bool IsMonospaceFont(string? fontName) =>
        fontName is not null &&
        (fontName.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
         fontName.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
         fontName.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
         fontName.Contains("Menlo", StringComparison.OrdinalIgnoreCase));

    private static double MedianFontSize(PdfDocument document)
    {
        var sizes = new List<double>();
        int sampled = 0;
        for (int p = 1; p <= document.NumberOfPages && sampled < 4000; p++)
        {
            foreach (var letter in document.GetPage(p).Letters)
            {
                sizes.Add(letter.PointSize);
                if (++sampled >= 4000) break;
            }
        }
        if (sizes.Count == 0) return 10;
        sizes.Sort();
        return sizes[sizes.Count / 2];
    }

    private static string HeadingPrefix(double fontSize, double bodySize, string text)
    {
        // Headings: clearly larger font, short, few words, no sentence punctuation.
        // Lines ending in ':' are lead-ins to code/examples, not headings.
        if (bodySize <= 0 || text.Length > 80) return "";
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12) return "";
        if (text.EndsWith('.') || text.EndsWith(',') || text.EndsWith(';') || text.EndsWith(':')) return "";
        // URLs and lowercase-starting fragments are body text, never headings
        if (text.Contains("://")) return "";
        if (!char.IsUpper(text[0]) && !char.IsDigit(text[0])) return "";

        double ratio = fontSize / bodySize;
        return ratio switch
        {
            >= 1.9 => "#",
            >= 1.55 => "##",
            >= 1.3 => "###",
            _ => ""
        };
    }

    /// <summary>Source-code heuristics for lines not flagged by font.</summary>
    private static bool LooksLikeCode(string text)
    {
        if (text is "{" or "}" or "};") return true;
        if (text.StartsWith("//", StringComparison.Ordinal)) return true;
        if (text.EndsWith(';') && (text.Contains('=') || text.Contains('(') || text.Contains('.'))) return true;
        if (CodeKeyword().IsMatch(text)) return true;
        return false;
    }

    private static bool IsPrintArtifact(string text)
    {
        // Browser print header: "21/07/2024, 22:07 Some Title - Portal"
        if (PrintHeader().IsMatch(text)) return true;
        // Footer: "https://... 1/3" or a bare URL line
        if (PrintFooter().IsMatch(text)) return true;
        return false;
    }

    private static string NormalizeBullet(string text)
    {
        foreach (var bullet in new[] { "•", "‣", "●", "▪", "·", "– ", "— " })
        {
            if (text.StartsWith(bullet, StringComparison.Ordinal))
                return "- " + text[bullet.Length..].TrimStart();
        }
        return text;
    }

    [GeneratedRegex(@"^\d{1,2}/\d{1,2}/\d{2,4},?\s+\d{1,2}:\d{2}\b")]
    private static partial Regex PrintHeader();

    [GeneratedRegex(@"^https?://\S+(\s+\d+/\d+)?$")]
    private static partial Regex PrintFooter();

    [GeneratedRegex(@"^(var|string|int|bool|double|foreach|if|else|using|new|return|public|private|static|class)\b.*[;{)]\s*$")]
    private static partial Regex CodeKeyword();

    [GeneratedRegex(@"```\r?\n\s*---\s*\r?\n\s*```csharp\r?\n")]
    private static partial Regex SplitCodeFence();
}
