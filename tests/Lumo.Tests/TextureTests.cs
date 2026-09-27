using System.Numerics;
using Lumo.Engine.Assets;
using Lumo.Engine.Rendering.Software;
using SkiaSharp;

namespace Lumo.Tests;

/// <summary>
/// PNG decode (Texture2D) and screen-space textured-quad rasterization.
/// </summary>
public class TextureTests
{
    private static string WritePng(SKBitmap bitmap)
    {
        string path = Path.Combine(Path.GetTempPath(), $"lumo_tex_{Guid.NewGuid():N}.png");
        using var img = SKImage.FromBitmap(bitmap);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
        return path;
    }

    [Fact]
    public void LoadPng_DecodesWidthHeightAndPixels()
    {
        string path;
        using (var bmp = new SKBitmap(4, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul))
        {
            bmp.SetPixel(0, 0, new SKColor(255, 0, 0, 255));
            bmp.SetPixel(1, 0, new SKColor(0, 255, 0, 255));
            bmp.SetPixel(2, 0, new SKColor(0, 0, 255, 255));
            bmp.SetPixel(3, 0, new SKColor(255, 255, 255, 255));
            bmp.SetPixel(0, 1, new SKColor(0, 0, 0, 0));
            bmp.SetPixel(1, 1, new SKColor(10, 20, 30, 255));
            bmp.SetPixel(2, 1, new SKColor(0, 0, 0, 128));
            bmp.SetPixel(3, 1, new SKColor(1, 2, 3, 255));
            path = WritePng(bmp);
        }

        try
        {
            var tex = Texture2D.Load(path);
            Assert.NotNull(tex);
            Assert.Equal(4, tex!.Width);
            Assert.Equal(2, tex.Height);
            Assert.Equal(4 * 2 * 4, tex.Pixels.Length);

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, tex.Pixels[0..4]);
            Assert.Equal(new byte[] { 0, 255, 0, 255 }, tex.Pixels[4..8]);
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, tex.Pixels[8..12]);
            Assert.Equal(0, tex.Pixels[16 + 3]);
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, tex.Pixels[20..24]);
            Assert.Equal(128, tex.Pixels[24 + 3]);
        }
        finally
        {
            File.Delete(path);
            Texture2D.ClearCache();
        }
    }

    [Fact]
    public void LoadPng_MissingFileReturnsNull()
    {
        Assert.Null(Texture2D.Load(Path.Combine(Path.GetTempPath(), "definitely_missing_lumo.png")));
    }

    private static Texture2D MakeCheckerTexture()
    {
        // 2×2: TL red, TR green, BL blue, BR white.
        var pixels = new byte[]
        {
            255, 0, 0, 255,    0, 255, 0, 255,
            0, 0, 255, 255,    255, 255, 255, 255,
        };
        return new Texture2D(2, 2, pixels);
    }

    [Fact]
    public void Rasterize_AxisAlignedQuadSamplesTextureCorners()
    {
        var tex = MakeCheckerTexture();
        var quad = TexturedQuad.Rasterize(tex,
            new Vector2(10, 20), new Vector2(42, 20),
            new Vector2(42, 52), new Vector2(10, 52),
            Vector3.One);

        Assert.NotNull(quad);
        Assert.Equal(10, quad!.X);
        Assert.Equal(20, quad.Y);
        Assert.Equal(32, quad.Width);
        Assert.Equal(32, quad.Height);

        byte[] At(int px, int py)
        {
            int i = ((py - quad.Y) * quad.Width + (px - quad.X)) * 4;
            return quad.Pixels[i..(i + 4)];
        }

        // BGRA premultiplied: TL red = (0,0,255,255), TR green = (0,255,0,255).
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, At(12, 22));
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, At(39, 22));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, At(12, 49));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, At(39, 49));
    }

    [Fact]
    public void Rasterize_AppliesTintAndTransparency()
    {
        var tex = MakeCheckerTexture();
        var quad = TexturedQuad.Rasterize(tex,
            new Vector2(0, 0), new Vector2(32, 0),
            new Vector2(32, 32), new Vector2(0, 32),
            new Vector3(0.5f, 1f, 1f));

        Assert.NotNull(quad);
        // TL texel red 255 × tint 0.5 → 127 (truncated), premultiplied BGRA.
        Assert.Equal(127, quad!.Pixels[2]);
        Assert.Equal(255, quad.Pixels[3]);
        // TR texel (right edge, row 0) green × tint 1 → unchanged.
        int trIdx = 31 * 4;
        Assert.Equal(255, quad.Pixels[trIdx + 1]);
    }

    [Fact]
    public void Rasterize_DegenerateQuadReturnsNull()
    {
        var tex = MakeCheckerTexture();
        var same = new Vector2(5, 5);
        Assert.Null(TexturedQuad.Rasterize(tex, same, same, same, same, Vector3.One));
    }
}
