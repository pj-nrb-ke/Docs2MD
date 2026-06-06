using System.Text.RegularExpressions;

namespace Docs2MD;

/// <summary>Final tidy pass applied to all converted Markdown.</summary>
public static partial class MarkdownCleaner
{
    public static string Tidy(string markdown)
    {
        // Normalise line endings
        string text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');

        // Strip trailing whitespace per line
        text = TrailingWhitespace().Replace(text, "");

        // Collapse 3+ blank lines to one blank line
        text = ExcessBlankLines().Replace(text, "\n\n");

        // De-hyphenate words broken across lines: "exam-\nple" -> "example"
        text = BrokenHyphenation().Replace(text, "$1$2");

        return text.Trim() + "\n";
    }

    [GeneratedRegex(@"[ \t]+(?=\n)")]
    private static partial Regex TrailingWhitespace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessBlankLines();

    [GeneratedRegex(@"(\p{Ll})-\n(\p{Ll})")]
    private static partial Regex BrokenHyphenation();
}
