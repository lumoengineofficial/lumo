using System.Numerics;
using Lumo.Engine.Assets;

namespace Lumo.Engine.Rendering.Software;

/// <summary>
/// Rasterized textured quad in screen space.
/// Pixels are tightly packed premultiplied BGRA8, top-down rows.
/// </summary>
public sealed class QuadRaster
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Pixels { get; init; } = [];
}

/// <summary>
/// Software rasterizer that maps a texture onto a screen-space quad
/// (two triangles, nearest sampling, color tint).
/// </summary>
public static class TexturedQuad
{
    private const int MaxSize = 4096;

    /// <summary>
    /// Rasterizes <paramref name="tex"/> onto the quad tl→tr→br→bl.
    /// Returns null for degenerate or oversized quads; callers should fall
    /// back to solid-color rendering in that case.
    /// </summary>
    public static QuadRaster? Rasterize(
        Texture2D tex,
        Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl,
        Vector3 tint)
    {
        float minX = MathF.Min(MathF.Min(tl.X, tr.X), MathF.Min(br.X, bl.X));
        float minY = MathF.Min(MathF.Min(tl.Y, tr.Y), MathF.Min(br.Y, bl.Y));
        float maxX = MathF.Max(MathF.Max(tl.X, tr.X), MathF.Max(br.X, bl.X));
        float maxY = MathF.Max(MathF.Max(tl.Y, tr.Y), MathF.Max(br.Y, bl.Y));

        int x0 = (int)MathF.Floor(minX);
        int y0 = (int)MathF.Floor(minY);
        int w = (int)MathF.Ceiling(maxX) - x0;
        int h = (int)MathF.Ceiling(maxY) - y0;
        if (w <= 0 || h <= 0 || w > MaxSize || h > MaxSize) return null;

        var pixels = new byte[w * h * 4];

        // Triangle 1: tl(0,0) tr(1,0) br(1,1); Triangle 2: tl(0,0) br(1,1) bl(0,1).
        RasterTriangle(tex, tl, tr, br, new(0, 0), new(1, 0), new(1, 1), x0, y0, w, h, tint, pixels);
        RasterTriangle(tex, tl, br, bl, new(0, 0), new(1, 1), new(0, 1), x0, y0, w, h, tint, pixels);

        return new QuadRaster { X = x0, Y = y0, Width = w, Height = h, Pixels = pixels };
    }

    private static void RasterTriangle(
        Texture2D tex,
        Vector2 p0, Vector2 p1, Vector2 p2,
        Vector2 uv0, Vector2 uv1, Vector2 uv2,
        int x0, int y0, int w, int h,
        Vector3 tint, byte[] dst)
    {
        float area = Edge(p0, p1, p2);
        if (MathF.Abs(area) < 1e-6f) return;
        float invArea = 1f / area;

        int px0 = Math.Max(x0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
        int py0 = Math.Max(y0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
        int px1 = Math.Min(x0 + w, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
        int py1 = Math.Min(y0 + h, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));

        float tr = Math.Clamp(tint.X, 0f, 1f);
        float tg = Math.Clamp(tint.Y, 0f, 1f);
        float tb = Math.Clamp(tint.Z, 0f, 1f);

        for (int py = py0; py < py1; py++)
        {
            float sy = py + 0.5f;
            int rowBase = (py - y0) * w;
            for (int px = px0; px < px1; px++)
            {
                float sx = px + 0.5f;

                float w0 = Edge(p1, p2, new Vector2(sx, sy)) * invArea;
                float w1 = Edge(p2, p0, new Vector2(sx, sy)) * invArea;
                float w2 = 1f - w0 - w1;
                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                float u = w0 * uv0.X + w1 * uv1.X + w2 * uv2.X;
                float v = w0 * uv0.Y + w1 * uv1.Y + w2 * uv2.Y;

                int ix = (int)(u * tex.Width);
                if (ix < 0) ix = 0; else if (ix >= tex.Width) ix = tex.Width - 1;
                int iy = (int)(v * tex.Height);
                if (iy < 0) iy = 0; else if (iy >= tex.Height) iy = tex.Height - 1;

                int si = (iy * tex.Width + ix) * 4;
                byte a = tex.Pixels[si + 3];
                if (a == 0) continue;

                byte r = (byte)Math.Clamp((int)(tex.Pixels[si] * tr), 0, 255);
                byte g = (byte)Math.Clamp((int)(tex.Pixels[si + 1] * tg), 0, 255);
                byte b = (byte)Math.Clamp((int)(tex.Pixels[si + 2] * tb), 0, 255);

                int di = (rowBase + (px - x0)) * 4;
                if (a == 255)
                {
                    dst[di] = b;
                    dst[di + 1] = g;
                    dst[di + 2] = r;
                    dst[di + 3] = 255;
                }
                else
                {
                    dst[di] = (byte)(b * a / 255);
                    dst[di + 1] = (byte)(g * a / 255);
                    dst[di + 2] = (byte)(r * a / 255);
                    dst[di + 3] = a;
                }
            }
        }
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 c)
        => (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);
}
