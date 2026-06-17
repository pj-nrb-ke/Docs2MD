namespace Docs2MD.Web.Services;

/// <summary>
/// Wraps the Core conversion pipeline for use from Blazor components.
/// Accepts a stream, writes to a temp file, converts, and returns Markdown.
/// </summary>
public sealed class ConversionService
{
    private readonly ILogger<ConversionService> _logger;
    private readonly long _maxBytes;

    public ConversionService(ILogger<ConversionService> logger, IConfiguration config)
    {
        _logger = logger;
        _maxBytes = (config.GetValue<int>("Conversion:MaxFileSizeMb", 50)) * 1024L * 1024L;
    }

    public long MaxBytes => _maxBytes;

    /// <summary>
    /// Converts the uploaded file stream to Markdown.
    /// Progress messages are emitted via the <paramref name="progress"/> callback.
    /// Returns (markdown, null) on success or (null, errorMessage) on failure.
    /// </summary>
    public async Task<(string? Markdown, string? Error)> ConvertAsync(
        Stream content,
        string fileName,
        ConvertOptions options,
        Action<string> progress,
        CancellationToken ct = default)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        string tmp = Path.Combine(Path.GetTempPath(), $"d2md_{Guid.NewGuid():N}{ext}");
        try
        {
            progress($"Uploading {fileName}…");
            await using (var fs = File.Create(tmp))
                await content.CopyToAsync(fs, ct);

            progress("Converting…");
            Log.Sink = progress;

            string markdown = ext switch
            {
                ".pdf"  => await Task.Run(() => PdfToMarkdown.Convert(tmp, options), ct),
                ".docx" => await Task.Run(() => DocxToMarkdown.Convert(tmp), ct),
                ".md"   => await File.ReadAllTextAsync(tmp, ct),
                _       => throw new NotSupportedException($"Unsupported file type: {ext}")
            };

            progress("Done.");
            _logger.LogInformation("Converted {File} ({Bytes} bytes)", fileName, content.Length);
            return (markdown, null);
        }
        catch (OperationCanceledException)
        {
            return (null, "Conversion cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Conversion failed for {File}", fileName);
            return (null, ex.Message);
        }
        finally
        {
            Log.Sink = _ => { };
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
