using Docnet.Core;
using Docnet.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;
// WinForms global usings pull in System.Drawing — disambiguate explicitly
using ISImage = SixLabors.ImageSharp.Image;
using ISColor = SixLabors.ImageSharp.Color;

namespace Docs2MD;

/// <summary>
/// OCR fallback for scanned PDF pages:
/// Docnet (PDFium) rasterises the page to BGRA, ImageSharp re-encodes to PNG,
/// Tesseract recognises the text. Requires tessdata\eng.traineddata next to
/// the exe (run get-tessdata.ps1 once).
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
        byte[] png = RenderPageToPng(pdfPath, pageIndex);
        using var pix = Pix.LoadFromMemory(png);
        using var result = _engine.Process(pix);
        return result.GetText();
    }

    private byte[] RenderPageToPng(string pdfPath, int pageIndex)
    {
        using var docReader = DocLib.Instance.GetDocReader(
            pdfPath,
            new PageDimensions(scalingFactor: _options.OcrScale));
        using var pageReader = docReader.GetPageReader(pageIndex);

        int width = pageReader.GetPageWidth();
        int height = pageReader.GetPageHeight();
        byte[] bgra = pageReader.GetImage(); // BGRA, transparent where unpainted

        using var image = ISImage.LoadPixelData<Bgra32>(bgra, width, height);
        // Flatten transparency onto white so Tesseract sees black-on-white
        image.Mutate(ctx => ctx.BackgroundColor(ISColor.White));

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    public void Dispose() => _engine.Dispose();
}
