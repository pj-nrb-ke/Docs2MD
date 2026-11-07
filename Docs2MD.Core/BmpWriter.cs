namespace Docs2MD;

/// <summary>
/// Minimal in-memory BMP encoder for OCR input. Composites top-down BGRA pixels
/// (as produced by PDFium/Docnet, transparent where unpainted) onto a white
/// background and writes a 24-bit bottom-up BMP that Leptonica/Tesseract reads.
///
/// Replaces SixLabors.ImageSharp: our only use was "flatten onto white + encode",
/// and ImageSharp 3.2+ requires a paid licence while every free 3.1.x release
/// carries security advisories. BMP is uncompressed, so this stays tiny.
/// </summary>
public static class BmpWriter
{
    public static byte[] FromBgraOnWhite(byte[] bgra, int width, int height)
    {
        int rowSize  = (width * 3 + 3) & ~3;   // each row padded to a 4-byte boundary
        int dataSize = rowSize * height;
        const int headerSize = 54;             // 14-byte file header + 40-byte info header
        var bmp = new byte[headerSize + dataSize];

        // BITMAPFILEHEADER
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        WriteI32(bmp, 2, bmp.Length);          // file size
        WriteI32(bmp, 10, headerSize);         // pixel data offset
        // BITMAPINFOHEADER
        WriteI32(bmp, 14, 40);                 // header size
        WriteI32(bmp, 18, width);
        WriteI32(bmp, 22, height);
        bmp[26] = 1;                           // planes
        bmp[28] = 24;                          // bits per pixel
        WriteI32(bmp, 34, dataSize);

        for (int row = 0; row < height; row++)
        {
            int imgY = height - 1 - row;       // BMP rows run bottom-to-top
            int src  = imgY * width * 4;
            int dst  = headerSize + row * rowSize;
            for (int x = 0; x < width; x++)
            {
                int s   = src + x * 4;
                int a   = bgra[s + 3];
                int inv = 255 - a;             // composite src over white: src*a + white*(255-a)
                bmp[dst]     = (byte)((bgra[s]     * a + 255 * inv) / 255); // B
                bmp[dst + 1] = (byte)((bgra[s + 1] * a + 255 * inv) / 255); // G
                bmp[dst + 2] = (byte)((bgra[s + 2] * a + 255 * inv) / 255); // R
                dst += 3;
            }
        }
        return bmp;
    }

    private static void WriteI32(byte[] b, int o, int v)
    {
        b[o]     = (byte)v;
        b[o + 1] = (byte)(v >> 8);
        b[o + 2] = (byte)(v >> 16);
        b[o + 3] = (byte)(v >> 24);
    }
}
