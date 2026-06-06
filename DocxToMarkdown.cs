using Mammoth;
using ReverseMarkdown;

namespace Docs2MD;

/// <summary>
/// DOCX → Markdown via two reliable steps:
/// Mammoth (docx → semantic HTML) then ReverseMarkdown (HTML → Markdown).
/// Headings, lists, tables, bold/italic and hyperlinks survive the round trip.
/// </summary>
public static class DocxToMarkdown
{
    public static string Convert(string path)
    {
        var converter = new DocumentConverter();
        var result = converter.ConvertToHtml(path);

        foreach (var warning in result.Warnings)
            Log.Warn($"  [docx warning] {warning}");

        var config = new Config
        {
            GithubFlavored = true,           // fenced code blocks, pipe tables
            UnknownTags = Config.UnknownTagsOption.Bypass,
            RemoveComments = true,
            SmartHrefHandling = true
        };

        string markdown = new Converter(config).Convert(result.Value);
        return MarkdownCleaner.Tidy(markdown);
    }
}
