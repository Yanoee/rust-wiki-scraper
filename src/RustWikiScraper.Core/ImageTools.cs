using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RustWikiScraper.Core;

public static class ImageTools
{
    public const int Size = 512;

    public static byte[] To512Png(byte[] source)
    {
        using var input = new MemoryStream(source);
        BitmapSource frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        if (frame.PixelWidth != Size || frame.PixelHeight != Size)
        {
            var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            var resized = Lanczos(pixels, frame.PixelWidth, frame.PixelHeight, Size, Size);
            frame = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Bgra32, null, resized, Size * 4);
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    internal static byte[] Lanczos(byte[] src, int sw, int sh, int dw, int dh)
    {
        var pre = new float[sw * sh * 4];
        for (var i = 0; i < sw * sh; i++)
        {
            var a = src[i * 4 + 3] / 255f;
            pre[i * 4] = src[i * 4] * a;
            pre[i * 4 + 1] = src[i * 4 + 1] * a;
            pre[i * 4 + 2] = src[i * 4 + 2] * a;
            pre[i * 4 + 3] = src[i * 4 + 3];
        }
        var mid = Pass(pre, sw, sh, dw, horizontal: true);
        var dst = Pass(mid, dw, sh, dh, horizontal: false);
        var result = new byte[dw * dh * 4];
        for (var i = 0; i < dw * dh; i++)
        {
            var a = Math.Clamp(dst[i * 4 + 3], 0, 255);
            var k = a > 0.5f ? 255f / a : 0;
            for (var c = 0; c < 3; c++) result[i * 4 + c] = (byte)Math.Clamp(MathF.Round(dst[i * 4 + c] * k), 0, 255);
            result[i * 4 + 3] = (byte)MathF.Round(a);
        }
        return result;
    }

    static float[] Pass(float[] src, int w, int h, int newLen, bool horizontal)
    {
        var oldLen = horizontal ? w : h;
        var scale = (double)oldLen / newLen;
        var support = 3 * Math.Max(scale, 1);
        var filterScale = Math.Max(scale, 1);
        var outW = horizontal ? newLen : w;
        var outH = horizontal ? h : newLen;
        var dst = new float[outW * outH * 4];
        for (var o = 0; o < newLen; o++)
        {
            var center = (o + 0.5) * scale;
            var from = Math.Max(0, (int)Math.Floor(center - support));
            var to = Math.Min(oldLen - 1, (int)Math.Ceiling(center + support));
            var weights = new float[to - from + 1];
            double total = 0;
            for (var j = from; j <= to; j++)
                total += weights[j - from] = (float)Kernel((j + 0.5 - center) / filterScale);
            for (var j = 0; j < weights.Length; j++) weights[j] = (float)(weights[j] / total);

            var lines = horizontal ? h : w;
            for (var line = 0; line < lines; line++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                for (var j = from; j <= to; j++)
                {
                    var s = (horizontal ? line * w + j : j * w + line) * 4;
                    var wt = weights[j - from];
                    b += src[s] * wt; g += src[s + 1] * wt; r += src[s + 2] * wt; a += src[s + 3] * wt;
                }
                var d = (horizontal ? line * outW + o : o * outW + line) * 4;
                dst[d] = b; dst[d + 1] = g; dst[d + 2] = r; dst[d + 3] = a;
            }
        }
        return dst;
    }

    static double Kernel(double x)
    {
        x = Math.Abs(x);
        if (x < 1e-8) return 1;
        if (x >= 3) return 0;
        var px = Math.PI * x;
        return 3 * Math.Sin(px) * Math.Sin(px / 3) / (px * px);
    }
}
