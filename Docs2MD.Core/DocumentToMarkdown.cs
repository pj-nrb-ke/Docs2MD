namespace Docs2MD;

/// <summary>
/// Single entry point that routes a file to the right converter by extension.
/// Shared by the desktop app, the right-click CLI mode, and the web service so
/// the extension→converter mapping lives in exactly one place.
/// </summary>
public static class DocumentToMarkdown
{
    /// <summary>Extensions the converters accept (used for drag-drop filtering and shell registration).</summary>
    public static readonly string[] SupportedExtensions = [".pdf", ".docx", ".xlsx", ".xls"];

    public static string Convert(string path, ConvertOptions options) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".docx" => DocxToMarkdown.Convert(path),
            ".xlsx" => XlsxToMarkdown.Convert(path),
            ".xls"  => XlsToMarkdown.Convert(path),
            ".pdf"  => PdfToMarkdown.Convert(path, options),
            ".md"   => File.ReadAllText(path),
            var ext => throw new NotSupportedException($"Unsupported file type: {ext}")
        };
}
