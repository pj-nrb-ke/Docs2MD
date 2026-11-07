using Docnet.Core;
using Docnet.Core.Models;
using Tesseract;

namespace Docs2MD;

/// <summary>
/// OCR fallback for scanned PDF pages:
/// Docnet (PDFium) rasterises the page to BGRA, BmpWriter flattens it onto white
/// and encodes a BMP, Tesseract recognises the text. Requires tessdata\eng.traineddata
/// next to the exe (run get-tessdata.ps1 once).
/// </summary>
public sealed class OcrEngine : IDisposable
{
    private readonly TesseractEngine _engine;
    private readonly ConvertOptions _options;

    private OcrEngine(TesseractEngine engine, ConvertOptions options)
    {
        _engine = engine;
        _options = options;
    }

    /// <summary>Returns null (with a warning) when tessdata or native libs are missing.</summary>
    public static OcrEngine? TryCreate(ConvertOptions options)
    {
        string tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
        try
        {
            var engine = new TesseractEngine(tessdata, options.OcrLanguage, EngineMode.Default);
            return new OcrEngine(engine, options);
        }
        catch (Exception ex)
        {
            Log.Warn(
                $"  [ocr] Tesseract unavailable ({ex.Message}). " +
                $"Run get-tessdata.ps1 and rebuild. Scanned pages will be skipped.");
            return null;
        }
    }

    /// <summary>OCR a single page (0-based index) of the given PDF.</summary>
    public string RecognizePage(string pdfPath, int pageIndex)
    {
        byte[] bmp = RenderPageToBmp(pdfPath, pageIndex);
        using var pix = Pix.LoadFromMemory(bmp);
        using var result = _engine.Process(pix);
        return result.GetText();
    }

    private byte[] RenderPageToBmp(string pdfPath, int pageIndex)
    {
        using var docReader = DocLib.Instance.GetDocReader(
            pdfPath,
            new PageDimensions(scalingFactor: _options.OcrScale));
        using var pageReader = docReader.GetPageReader(pageIndex);

        int width = pageReader.GetPageWidth();
        int height = pageReader.GetPageHeight();
        byte[] bgra = pageReader.GetImage(); // BGRA, transparent where unpainted

        return BmpWriter.FromBgraOnWhite(bgra, width, height);
    }

    public void Dispose() => _engine.Dispose();
}
