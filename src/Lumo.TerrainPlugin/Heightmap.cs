namespace Lumo.TerrainPlugin;

/// <summary>Heightmap grid in world units (Y up). Row-major, x = width axis, z = height axis.</summary>
public sealed class Heightmap
{
    public const int FormatVersion = 1;
    private static readonly byte[] Magic = "LUMOHMAP"u8.ToArray();

    public int Width { get; }
    public int Height { get; }
    public float WorldSize { get; }
    public float HeightScale { get; }
    public float[] Data { get; }

    /// <summary>World units between two samples.</summary>
    public float CellWorld => WorldSize / Math.Max(1, Width - 1);

    public Heightmap(int width, int height, float worldSize, float heightScale, float[]? data = null)
    {
        if (width < 2 || height < 2) throw new ArgumentOutOfRangeException(nameof(width), "heightmap needs at least 2x2 samples");
        Width = width;
        Height = height;
        WorldSize = worldSize;
        HeightScale = heightScale;
        Data = data ?? new float[width * height];
        if (Data.Length != width * height)
            throw new ArgumentException($"expected {width * height} samples, got {Data.Length}", nameof(data));
    }

    /// <summary>Clamped indexed access (world-unit height).</summary>
    public float Get(int x, int z)
        => Data[Math.Clamp(z, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)];

    public bool InBounds(float wx, float wz)
    {
        float half = WorldSize * 0.5f;
        return wx >= -half && wx <= half && wz >= -half && wz <= half;
    }

    /// <summary>Bilinear sample at world x/z; 0 outside the map.</summary>
    public float SampleWorld(float wx, float wz)
    {
        if (!InBounds(wx, wz)) return 0f;
        float gx = (wx / WorldSize + 0.5f) * (Width - 1);
        float gz = (wz / WorldSize + 0.5f) * (Height - 1);
        int x0 = Math.Clamp((int)MathF.Floor(gx), 0, Width - 1);
        int z0 = Math.Clamp((int)MathF.Floor(gz), 0, Height - 1);
        int x1 = Math.Min(x0 + 1, Width - 1);
        int z1 = Math.Min(z0 + 1, Height - 1);
        float fx = gx - x0;
        float fz = gz - z0;
        float a = Get(x0, z0) + (Get(x1, z0) - Get(x0, z0)) * fx;
        float b = Get(x0, z1) + (Get(x1, z1) - Get(x0, z1)) * fx;
        return a + (b - a) * fz;
    }

    /// <summary>World x/z of a sample index (index 0 → -WorldSize/2).</summary>
    public float GridToWorld(int index, int count) => (index / (float)(count - 1) - 0.5f) * WorldSize;

    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(Magic);
        bw.Write(FormatVersion);
        bw.Write(Width);
        bw.Write(Height);
        bw.Write(WorldSize);
        bw.Write(HeightScale);
        foreach (float v in Data) bw.Write(v);
    }

    public static Heightmap? Load(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (!br.ReadBytes(8).AsSpan().SequenceEqual(Magic)) return null;
            if (br.ReadInt32() != FormatVersion) return null;
            int w = br.ReadInt32();
            int h = br.ReadInt32();
            if (w < 2 || h < 2 || w > 4096 || h > 4096) return null;
            float worldSize = br.ReadSingle();
            float heightScale = br.ReadSingle();
            if (worldSize <= 0f || heightScale <= 0f) return null;
            var data = new float[w * h];
            for (int i = 0; i < data.Length; i++) data[i] = br.ReadSingle();
            return new Heightmap(w, h, worldSize, heightScale, data);
        }
        catch
        {
            return null;
        }
    }
}
