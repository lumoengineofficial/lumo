using System.Numerics;
using Lumo.Engine.Assets;
using Lumo.Plugins;

namespace Lumo.TerrainPlugin;

/// <summary>Terrain state and operations shared by editor commands, viewport
/// sculpting and runtime graph nodes. Meshes register into MeshLibrary under
/// "Terrain_c&lt;x&gt;_&lt;z&gt;" names; scene entities reference them by name.</summary>
public static class TerrainSystem
{
    public const string EntityPrefix = "Terrain";
    public const int DefaultResolution = 129;
    public const int CellsPerChunk = 32;
    public const float DefaultWorldSize = 64f;
    public const float DefaultHeightScale = 14f;
    public const float BrushStrength = 0.45f;
    public const float MinBrushRadius = 1.5f;
    public const float MaxBrushRadius = 24f;

    private static readonly HashSet<(int X, int Z)> Dirty = [];

    public static Heightmap? Map { get; private set; }

    /// <summary>Active sculpt brush id ("raise"/"lower"/"smooth"/"flatten") or null.</summary>
    public static string? ActiveBrush { get; private set; }

    public static float BrushRadius { get; private set; } = 6f;

    private static bool _strokeActive;
    private static float _flattenTarget;

    public static int ChunkCountX => Map == null ? 0 : (Map.Width - 1 + CellsPerChunk - 1) / CellsPerChunk;
    public static int ChunkCountZ => Map == null ? 0 : (Map.Height - 1 + CellsPerChunk - 1) / CellsPerChunk;

    public static string MeshName(int cx, int cz) => $"Terrain_c{cx}_{cz}";

    public static string HeightmapPath(string projectPath)
        => Path.Combine(projectPath, "Assets", "Terrain", "terrain.hmap");

    // ------------------------------------------------------- commands

    public static void Generate(IHostBridge bridge, int? seed = null)
    {
        int s = seed ?? Random.Shared.Next();
        Map = TerrainGenerator.Generate(DefaultResolution, DefaultWorldSize, DefaultHeightScale, s);
        RebuildChunks();
        SyncEntities(bridge, replace: true);
        Save(bridge);
        bridge.Log($"Terrain generated: {Map.Width}×{Map.Height}, seed {s}, {ChunkCountX * ChunkCountZ} chunk(s).");
    }

    public static async Task ImportAsync(IHostBridge bridge)
    {
        string? path = await bridge.PickOpenFileAsync("Import Heightmap",
            ("Heightmap Images", ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.tga", "*.webp"]),
            ("All Files", ["*.*"]));
        if (path == null)
        {
            bridge.Log("Heightmap import cancelled.");
            return;
        }

        var tex = Texture2D.Load(path);
        if (tex == null)
        {
            bridge.Log($"Could not decode '{Path.GetFileName(path)}'.");
            return;
        }

        const int maxRes = 257;
        float scale = Math.Min(1f, maxRes / (float)Math.Max(tex.Width, tex.Height));
        int w = Math.Max(16, (int)MathF.Round(tex.Width * scale));
        int h = Math.Max(16, (int)MathF.Round(tex.Height * scale));
        var map = new Heightmap(w, h, DefaultWorldSize, DefaultHeightScale);

        for (int j = 0; j < h; j++)
        {
            int sy = j * (tex.Height - 1) / Math.Max(1, h - 1);
            for (int i = 0; i < w; i++)
            {
                int sx = i * (tex.Width - 1) / Math.Max(1, w - 1);
                int p = (sy * tex.Width + sx) * 4;
                float lum = (tex.Pixels[p] + tex.Pixels[p + 1] + tex.Pixels[p + 2]) / (3f * 255f);
                map.Data[j * w + i] = lum * DefaultHeightScale;
            }
        }

        Map = map;
        RebuildChunks();
        SyncEntities(bridge, replace: true);
        Save(bridge);
        bridge.Log($"Heightmap imported: {Path.GetFileName(path)} → {w}×{h} terrain.");
    }

    public static void Save(IHostBridge bridge)
    {
        if (Map == null || string.IsNullOrEmpty(bridge.ProjectPath)) return;
        try
        {
            string path = HeightmapPath(bridge.ProjectPath);
            Map.Save(path);
            bridge.Log($"Terrain heightmap saved → Assets/Terrain/terrain.hmap");
        }
        catch (Exception ex)
        {
            bridge.Log($"Terrain save failed: {ex.Message}");
        }
    }

    /// <summary>Creates terrain entities when the heightmap exists but the scene lost them.</summary>
    public static void SyncEntities(IHostBridge bridge, bool replace)
    {
        if (Map == null || string.IsNullOrEmpty(bridge.ProjectPath)) return;
        int cxN = ChunkCountX;
        int czN = ChunkCountZ;

        if (!replace)
        {
            bool missing = false;
            for (int cz = 0; cz < czN && !missing; cz++)
                for (int cx = 0; cx < cxN; cx++)
                    if (!bridge.EntityExists(MeshName(cx, cz))) { missing = true; break; }
            if (!missing) return;
            bridge.PushUndo();
        }
        else
        {
            bridge.PushUndo();
            bridge.DeleteEntitiesByPrefix(EntityPrefix);
        }

        bool created = false;
        for (int cz = 0; cz < czN; cz++)
        {
            for (int cx = 0; cx < cxN; cx++)
            {
                bridge.CreateOrUpdateMeshEntity(MeshName(cx, cz), MeshName(cx, cz));
                created = true;
            }
        }

        if (created)
        {
            bridge.SaveProject();
            bridge.RefreshUi();
        }
    }

    /// <summary>Loads the saved heightmap (called by the plugin on startup) and
    /// re-registers chunk meshes. Entity sync happens via the sync command.</summary>
    public static void TryRestore(string projectPath, Action<string> log)
    {
        string path = HeightmapPath(projectPath);
        if (!File.Exists(path)) return;
        var map = Heightmap.Load(path);
        if (map == null)
        {
            log("Terrain heightmap unreadable; skipping restore.");
            return;
        }
        Map = map;
        RebuildChunks();
        log($"Terrain restored: {map.Width}×{map.Height}, {ChunkCountX * ChunkCountZ} chunk(s).");
    }

    // ------------------------------------------------------- brush state

    public static void SetBrush(string? brush)
        => ActiveBrush = brush is "raise" or "lower" or "smooth" or "flatten" ? brush : null;

    public static void AdjustRadius(float delta)
        => BrushRadius = Math.Clamp(BrushRadius + delta, MinBrushRadius, MaxBrushRadius);

    public static bool HandlePointer(ViewportPointerArgs args, IHostBridge? bridge)
    {
        if (Map == null || ActiveBrush == null) return false;

        switch (args.Kind)
        {
            case ViewportPointerKind.Down:
            {
                if (!args.LeftPressed || !args.HasRay) return false;
                if (!Raycast(args.RayOrigin, args.RayDir, out var hit)) return false;
                _strokeActive = true;
                _flattenTarget = hit.Y;
                bridge?.PushUndo();
                ApplyCurrentBrush(hit.X, hit.Z);
                RebuildDirty();
                return true;
            }
            case ViewportPointerKind.Move:
            {
                if (!_strokeActive) return false;
                if (args.HasRay && Raycast(args.RayOrigin, args.RayDir, out var hit))
                {
                    ApplyCurrentBrush(hit.X, hit.Z);
                    RebuildDirty();
                }
                return true;
            }
            case ViewportPointerKind.Up:
            {
                if (!_strokeActive) return false;
                _strokeActive = false;
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>Runtime/graph entry: one brush dab at a world position + mesh update.</summary>
    public static void ApplyBrushAt(BrushOp op, float wx, float wz, float radius, float strength)
    {
        if (Map == null) return;
        TerrainBrush.Apply(Map, op, wx, wz, radius, strength, Map.SampleWorld(wx, wz), CellsPerChunk, Dirty);
        RebuildDirty();
    }

    private static void ApplyCurrentBrush(float wx, float wz)
    {
        if (Map == null || ActiveBrush == null) return;
        var op = ActiveBrush switch
        {
            "lower" => BrushOp.Lower,
            "smooth" => BrushOp.Smooth,
            "flatten" => BrushOp.Flatten,
            _ => BrushOp.Raise,
        };
        TerrainBrush.Apply(Map, op, wx, wz, BrushRadius, BrushStrength, _flattenTarget, CellsPerChunk, Dirty);
    }

    // ------------------------------------------------------- meshes

    /// <summary>Rebuilds all chunk meshes and registers them in MeshLibrary.</summary>
    public static void RebuildChunks()
    {
        if (Map == null) return;
        Dirty.Clear();
        for (int cz = 0; cz < ChunkCountZ; cz++)
            for (int cx = 0; cx < ChunkCountX; cx++)
                Dirty.Add((cx, cz));
        RebuildDirty();
    }

    /// <summary>Rebuilds and re-registers only the dirty chunks (fast sculpt path).</summary>
    public static void RebuildDirty()
    {
        if (Map == null || Dirty.Count == 0) return;
        foreach (var (cx, cz) in Dirty)
            MeshLibrary.Register(TerrainMesher.BuildChunk(Map, cx, cz, CellsPerChunk, MeshName(cx, cz)));
        Dirty.Clear();
    }

    public static float SampleHeight(float wx, float wz) => Map?.SampleWorld(wx, wz) ?? 0f;

    /// <summary>Ray vs heightfield: march then bisect. True when the ray enters the terrain.</summary>
    public static bool Raycast(Vector3 origin, Vector3 dir, out Vector3 hit)
    {
        hit = default;
        if (Map == null) return false;

        float step = Math.Max(0.3f, Map.CellWorld * 0.5f);
        const float maxT = 500f;
        float prevDiff = float.NaN;
        float prevT = 0f;

        for (float t = 0.5f; t <= maxT; t += step)
        {
            var p = origin + dir * t;
            if (!Map.InBounds(p.X, p.Z))
            {
                prevDiff = float.NaN;
                prevT = t;
                continue;
            }

            float diff = p.Y - Map.SampleWorld(p.X, p.Z);
            if (diff <= 0f && !float.IsNaN(prevDiff) && prevDiff > 0f)
            {
                float lo = prevT;
                float hi = t;
                for (int k = 0; k < 8; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    var pm = origin + dir * mid;
                    if (pm.Y - Map.SampleWorld(pm.X, pm.Z) > 0f) lo = mid;
                    else hi = mid;
                }
                hit = origin + dir * ((lo + hi) * 0.5f);
                return Map.InBounds(hit.X, hit.Z);
            }

            prevDiff = diff;
            prevT = t;
        }
        return false;
    }

    /// <summary>Test/reset hook: clears terrain state and active brush.</summary>
    public static void Reset()
    {
        Map = null;
        ActiveBrush = null;
        BrushRadius = 6f;
        _strokeActive = false;
        Dirty.Clear();
    }
}
