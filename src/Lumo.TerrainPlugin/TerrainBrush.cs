namespace Lumo.TerrainPlugin;

public enum BrushOp
{
    Raise,
    Lower,
    Smooth,
    Flatten,
}

/// <summary>Sculpt operations over a heightmap; only marks the touched chunks dirty
/// so the caller rebuilds a few thousand triangles per stroke, not the whole map.</summary>
public static class TerrainBrush
{
    /// <summary>Applies one brush dab centered at world (wx, wz). Heights clamp to [0, HeightScale].</summary>
    public static void Apply(Heightmap map, BrushOp op, float wx, float wz, float radius, float strength,
        float flattenTarget, int cellsPerChunk, ISet<(int X, int Z)> dirty)
    {
        if (radius <= 0f) return;

        float gxW = (wx / map.WorldSize + 0.5f) * (map.Width - 1);
        float gzW = (wz / map.WorldSize + 0.5f) * (map.Height - 1);
        float radiusG = radius / map.WorldSize * (map.Width - 1);
        if (radiusG < 0.5f) radiusG = 0.5f;

        int i0 = Math.Max(0, (int)MathF.Floor(gxW - radiusG));
        int i1 = Math.Min(map.Width - 1, (int)MathF.Ceiling(gxW + radiusG));
        int j0 = Math.Max(0, (int)MathF.Floor(gzW - radiusG));
        int j1 = Math.Min(map.Height - 1, (int)MathF.Ceiling(gzW + radiusG));
        if (i1 < i0 || j1 < j0) return;

        for (int j = j0; j <= j1; j++)
        {
            for (int i = i0; i <= i1; i++)
            {
                float dx = i - gxW;
                float dz = j - gzW;
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d > radiusG) continue;

                float t = 1f - d / radiusG;
                float fall = t * t * (3f - 2f * t);
                int idx = j * map.Width + i;
                float h = map.Data[idx];

                switch (op)
                {
                    case BrushOp.Raise:
                        h += strength * fall;
                        break;
                    case BrushOp.Lower:
                        h -= strength * fall;
                        break;
                    case BrushOp.Smooth:
                    {
                        float avg = Average3x3(map, i, j);
                        h += (avg - h) * Math.Min(1f, fall * 0.65f);
                        break;
                    }
                    case BrushOp.Flatten:
                        h += (flattenTarget - h) * Math.Min(1f, fall * 0.55f);
                        break;
                }

                map.Data[idx] = Math.Clamp(h, 0f, map.HeightScale);
            }
        }

        int cellsX = (map.Width - 1 + cellsPerChunk - 1) / cellsPerChunk;
        int cellsZ = (map.Height - 1 + cellsPerChunk - 1) / cellsPerChunk;
        for (int cz = j0 / cellsPerChunk; cz <= j1 / cellsPerChunk && cz < cellsZ; cz++)
        {
            for (int cx = i0 / cellsPerChunk; cx <= i1 / cellsPerChunk && cx < cellsX; cx++)
                dirty.Add((cx, cz));
        }
    }

    private static float Average3x3(Heightmap map, int x, int z)
    {
        float sum = 0f;
        int n = 0;
        for (int j = z - 1; j <= z + 1; j++)
        {
            if (j < 0 || j >= map.Height) continue;
            for (int i = x - 1; i <= x + 1; i++)
            {
                if (i < 0 || i >= map.Width) continue;
                sum += map.Data[j * map.Width + i];
                n++;
            }
        }
        return n > 0 ? sum / n : 0f;
    }
}
