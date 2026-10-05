namespace Lumo.TerrainPlugin;

/// <summary>Seeded multi-octave value-noise terrain synthesis (deterministic, allocation-light).</summary>
public static class TerrainGenerator
{
    public static Heightmap Generate(int resolution, float worldSize, float heightScale, int seed, int octaves = 5, float frequency = 3f)
    {
        var map = new Heightmap(resolution, resolution, worldSize, heightScale);

        float amplitudeSum = 0f;
        float amp = 1f;
        for (int o = 0; o < octaves; o++) { amplitudeSum += amp; amp *= 0.5f; }

        float inv = 1f / Math.Max(1, resolution - 1);
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float nx = x * inv * frequency;
                float nz = z * inv * frequency;
                float sum = 0f;
                float a = 1f;
                float f = 1f;
                for (int o = 0; o < octaves; o++)
                {
                    sum += ValueNoise(nx * f, nz * f, seed + o * 1013) * a;
                    a *= 0.5f;
                    f *= 2f;
                }
                float v = sum / amplitudeSum;
                v = MathF.Pow(v, 1.35f);
                map.Data[z * resolution + x] = v * heightScale;
            }
        }
        return map;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Hash01(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 0x9E3779B1u ^ (uint)z * 0x85EBCA77u ^ (uint)seed * 0xC2B2AE3Du;
            h ^= h >> 15;
            h *= 0x2545F491u;
            h ^= h >> 13;
            h *= 0x27D4EB2Fu;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static float ValueNoise(float x, float z, int seed)
    {
        int x0 = (int)MathF.Floor(x);
        int z0 = (int)MathF.Floor(z);
        float tx = Fade(x - x0);
        float tz = Fade(z - z0);
        float v00 = Hash01(x0, z0, seed);
        float v10 = Hash01(x0 + 1, z0, seed);
        float v01 = Hash01(x0, z0 + 1, seed);
        float v11 = Hash01(x0 + 1, z0 + 1, seed);
        float a = v00 + (v10 - v00) * tx;
        float b = v01 + (v11 - v01) * tx;
        return a + (b - a) * tz;
    }
}
