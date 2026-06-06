namespace Docs2MD;

public sealed class ConvertOptions
{
    /// <summary>Enable OCR fallback for PDF pages without a usable text layer.</summary>
    public bool OcrEnabled { get; set; } = true;

    /// <summary>OCR every page, even when it has embedded text.</summary>
    public bool ForceOcr { get; set; }

    /// <summary>Tesseract language code(s), e.g. "eng" or "eng+deu".</summary>
    public string OcrLanguage { get; set; } = "eng";

    /// <summary>A page with fewer extracted characters than this is treated as scanned.</summary>
    public int MinCharsPerPage { get; set; } = 16;

    /// <summary>Rasterisation scale for OCR (1.0 = 72 dpi). 4.17 ≈ 300 dpi.</summary>
    public double OcrScale { get; set; } = 4.17;
}
