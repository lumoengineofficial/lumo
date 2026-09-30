using SkiaSharp;

namespace Lumo.Tools;

/// <summary>
/// Removes dark background from the Lumo logo, making it transparent.
/// </summary>
public static class LogoProcessor
{
    /// <summary>
    /// Process the logo: remove dark navy background using color-distance based approach.
    /// </summary>
    public static void RemoveBackground(string inputPath, string outputPath)
    {
        using var original = SKBitmap.Decode(inputPath);
        using var output = new SKBitmap(original.Width, original.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        // Sample the background color from corners (dark navy ~#0d1b2a)
        SKColor bg1 = original.GetPixel(0, 0);
        SKColor bg2 = original.GetPixel(original.Width - 1, 0);
        SKColor bg3 = original.GetPixel(0, original.Height - 1);
        SKColor bg4 = original.GetPixel(original.Width - 1, original.Height - 1);

        // Average background color
        byte bgR = (byte)((bg1.Red + bg2.Red + bg3.Red + bg4.Red) / 4);
        byte bgG = (byte)((bg1.Green + bg2.Green + bg3.Green + bg4.Green) / 4);
        byte bgB = (byte)((bg1.Blue + bg2.Blue + bg3.Blue + bg4.Blue) / 4);

        Console.WriteLine($"Detected background color: RGB({bgR}, {bgG}, {bgB})");

        float maxDistance = 90f;  // max color distance to consider as background
        float fadeWidth = 30f;    // soft edge transition width

        for (int y = 0; y < original.Height; y++)
        {
            for (int x = 0; x < original.Width; x++)
            {
                SKColor src = original.GetPixel(x, y);

                // Euclidean distance from background color
                float dr = src.Red - bgR;
                float dg = src.Green - bgG;
                float db = src.Blue - bgB;
                float distance = MathF.Sqrt(dr * dr + dg * dg + db * db);

                byte alpha;
                if (distance < maxDistance - fadeWidth)
                {
                    // Definitely background -> fully transparent
                    alpha = 0;
                }
                else if (distance < maxDistance)
                {
                    // Soft edge transition
                    float t = (distance - (maxDistance - fadeWidth)) / fadeWidth;
                    alpha = (byte)(src.Alpha * Math.Clamp(t, 0f, 1f));
                }
                else
                {
                    // Foreground -> keep original alpha
                    alpha = src.Alpha;
                }

                output.SetPixel(x, y, new SKColor(src.Red, src.Green, src.Blue, alpha));
            }
        }

        using var image = SKImage.FromBitmap(output);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);

        Console.WriteLine($"Processed: {outputPath} ({original.Width}x{original.Height})");
    }

    /// <summary>
    /// Create a resized version of the logo.
    /// </summary>
    public static void Resize(string inputPath, string outputPath, int size)
    {
        using var original = SKBitmap.Decode(inputPath);
        using var resized = original.Resize(new SKImageInfo(size, size), SKFilterQuality.High);
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);

        Console.WriteLine($"Resized: {outputPath} ({size}x{size})");
    }

    /// <summary>
    /// Create a window icon: the logo on a rounded dark tile (white mark stays
    /// visible on both light and dark surfaces).
    /// </summary>
    public static void CreateIcon(string inputPath, string iconPath, int size = 64)
    {
        using var original = SKBitmap.Decode(inputPath);
        byte[] png = EncodeRounded(original, size);
        File.WriteAllBytes(iconPath, png);
        Console.WriteLine($"Icon created: {iconPath} ({size}x{size})");
    }

    /// <summary>
    /// Create a multi-size .ico (16..256) for the executable file icon.
    /// </summary>
    public static void CreateIco(string inputPath, string icoPath)
    {
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        using var original = SKBitmap.Decode(inputPath);

        var frames = new List<byte[]>(sizes.Length);
        foreach (int s in sizes)
            frames.Add(EncodeRounded(original, s));

        using var fs = File.Create(icoPath);
        using var w = new BinaryWriter(fs);
        w.Write((ushort)0);                       // reserved
        w.Write((ushort)1);                       // type: icon
        w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));   // width
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));   // height
            w.Write((byte)0);                       // palette
            w.Write((byte)0);                       // reserved
            w.Write((ushort)1);                     // planes
            w.Write((ushort)32);                    // bit count
            w.Write(frames[i].Length);
            w.Write(offset);
            offset += frames[i].Length;
        }
        foreach (byte[] frame in frames)
            w.Write(frame);

        Console.WriteLine($"Icon created: {icoPath} ({string.Join(", ", sizes.Select(s => s + "px"))})");
    }

    /// <summary>Render the source bitmap scaled into a rounded-corner tile with a transparent outside.</summary>
    private static byte[] EncodeRounded(SKBitmap source, int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float r = size * 0.18f;
        using var clip = new SKPath();
        clip.AddRoundRect(new SKRect(0, 0, size, size), r, r);
        canvas.ClipPath(clip);

        using (var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
            canvas.DrawBitmap(source, new SKRect(0, 0, size, size), paint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
