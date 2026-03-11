using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.ImageFilters;
using SkiaSharp;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CryptoJournal.Wpf.UI;

public static class ChartColors
{
    public static ulong Sha256(string s)
    {
        // Instantiate a cryptographic SHA256 hashing algorithm
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));

        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    // Translate HSL color space to SKColor (normalized 0..1 range)
    public static SKColor FromHsl(float h, float s, float l, byte a = 255)
    {
        h = h % 360f;
        if (h < 0) h += 360f;

        float c = (1 - MathF.Abs(2 * l - 1)) * s;
        float x = c * (1 - MathF.Abs((h / 60f) % 2 - 1));
        float m = l - c / 2;
        float r1, b1, g1;
        if (h < 60) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        byte r = (byte)Math.Clamp((r1 + m) * 255f, 0, 255);
        byte g = (byte)Math.Clamp((g1 + m) * 255f, 0, 255);
        byte b = (byte)Math.Clamp((b1 + m) * 255f, 0, 255);

        return new SKColor(r, g, b, a);
    }

    public static SKColor StableColorForSymbol(string symbol)
    {
        symbol = (symbol ?? "").Trim().ToUpperInvariant();
        if (symbol.Length == 0) return new SKColor(120, 120, 120);

        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(symbol));

        // 16 bit -> hue (0..360)
        ushort h16 = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2));
        float hue = (h16 / 65535f) * 360f;

        // 8 bit -> saturation in proper range
        byte s8 = bytes[2];
        float sat = 0.60f + (s8 / 255f) * 0.35f; // 0.60..0.95

        // 8 bit -> lightness in a fairly light range
        byte l8 = bytes[3];
        float lig = 0.50f + (l8 / 255f) * 0.20f; // 0.50..0.70

        var color = FromHsl(hue, sat, lig);

        // end up in a dark spot (rare, but it might happen due to conversion/rounding) - just lighten it
        if (RelativeLuma(color) < 0.40f)
            color = EnsureLight(color, minLuma: 0.40f);

        return color;
    }

    public static SKColor PnlColor(decimal value)
        => value == 0 ? new SKColor(160, 200, 160) : (value > 0 ? new SKColor(46, 204, 113) : new SKColor(231, 76, 60)); // Maps positive variants to shades of green and negative to red

    public static SolidColorPaint CreatePnlLabelPaint()
    {
        return new SolidColorPaint(new SKColor(245, 245, 245)) // almost white
        {
            SKTypeface  = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold),
            // shadow: dx, dy, sigmaX, sigmaY, color
            ImageFilter = new DropShadow(0, 2, 4, 4, new SKColor(0, 0, 0, 200)),
            ZIndex      = 9999,
        };
    }

    public static string FormatCompact(double v)
    {
        var abs = Math.Abs(v);
        if (abs >= 1_000_000_000_000) return (v / 1_000_000_000_000d).ToString("0.##") + "T";
        if (abs >= 1_000_000_000)     return (v / 1_000_000_000d).ToString("0.##") + "B";
        if (abs >= 1_000_000)         return (v / 1_000_000d).ToString("0.##") + "M";
        if (abs >= 1_000)             return (v / 1_000d).ToString("0.##") + "k";
        return v.ToString("0.00$");
    }

    private static float RelativeLuma(SKColor c)
    {
        // sRGB relative luminance (approx)
        float r = c.Red   / 255f;
        float g = c.Green / 255f;
        float b = c.Blue  / 255f;
        return 0.2126f * r + 0.7152f * g + 0.0722f * b;
    }

    private static SKColor EnsureLight(SKColor c, float minLuma)
    {
        var l = RelativeLuma(c);
        if (l >= minLuma) return c;

        // Linearly interpolate the color value towards pure white
        float t = (minLuma - l) / MathF.Max(0.0001f, 1f - l);
        byte  r = (byte)Math.Clamp(c.Red   + (255 - c.Red)   * t, 0, 255);
        byte  g = (byte)Math.Clamp(c.Green + (255 - c.Green) * t, 0, 255);
        byte  b = (byte)Math.Clamp(c.Blue  + (255 - c.Blue)  * t, 0, 255);
        return new SKColor(r, g, b, c.Alpha);
    }
}