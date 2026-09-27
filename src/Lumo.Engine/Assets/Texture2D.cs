using System.Runtime.InteropServices;
using SkiaSharp;

namespace Lumo.Engine.Assets;

/// <summary>
/// CPU texture backed by raw RGBA32 pixels (straight alpha, top-down rows).
/// PNG/JPG files are decoded once per path and cached.
/// </summary>
public sealed class Texture2D
{
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Texture2D> Cache = new();

    public int Width { get; }
    public int Height { get; }

    /// <summary>Tightly packed RGBA32, length = Width * Height * 4.</summary>
    public byte[] Pixels { get; }

    /// <summary>Wraps an existing tightly packed RGBA32 pixel buffer
    /// (length must be Width * Height * 4).</summary>
    public Texture2D(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("Pixel buffer length must be Width * Height * 4.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Loads and caches a texture, or returns null when the file
    /// is missing or cannot be decoded.</summary>
    public static Texture2D? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        lock (CacheLock)
        {
            if (Cache.TryGetValue(path, out var hit)) return hit;
        }

        SKBitmap? decoded = null;
        SKBitmap? rgba = null;
        try
        {
            decoded = SKBitmap.Decode(path);
            if (decoded == null) return null;

            rgba = decoded.Copy(SKColorType.Rgba8888);
            if (rgba == null) return null;

            int w = rgba.Width;
            int h = rgba.Height;
            var pixels = new byte[w * h * 4];
            Marshal.Copy(rgba.GetPixels(), pixels, 0, pixels.Length);
            if (rgba.AlphaType == SKAlphaType.Premul)
                Unpremultiply(pixels);

            var tex = new Texture2D(w, h, pixels);
            lock (CacheLock)
            {
                Cache[path] = tex;
            }
            return tex;
        }
        catch
        {
            return null;
        }
        finally
        {
            rgba?.Dispose();
            decoded?.Dispose();
        }
    }

    public static void ClearCache()
    {
        lock (CacheLock)
        {
            Cache.Clear();
        }
    }

    private static void Unpremultiply(byte[] pixels)
    {
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a == 0)
            {
                pixels[i] = 0;
                pixels[i + 1] = 0;
                pixels[i + 2] = 0;
            }
            else if (a != 255)
            {
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
            }
        }
    }
}
