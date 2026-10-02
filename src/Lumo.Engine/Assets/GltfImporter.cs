using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Lumo.Engine.Rendering.Abstractions;

namespace Lumo.Engine.Assets;

/// <summary>
/// Parses glTF 2.0 (.gltf with external or embedded buffers) into a single
/// merged Mesh. Very large models are reduced with vertex-cluster decimation
/// so the software viewport stays interactive.
/// </summary>
public static class GltfImporter
{
    /// <summary>Triangle budget after import; larger meshes are clustered down.</summary>
    public const int DefaultMaxTriangles = 25_000;

    public static Mesh Load(string path, int maxTriangles = DefaultMaxTriangles)
        => Load(path, out _, maxTriangles);

    /// <summary>Loads a .gltf file; <paramref name="originalTriangles"/> reports the
    /// pre-decimation triangle count so callers can log how much was reduced.</summary>
    public static Mesh Load(string path, out int originalTriangles, int maxTriangles = DefaultMaxTriangles)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("glTF file not found", path);

        string json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(path))!;

        byte[][] buffers = ReadBuffers(root, baseDir);
        JsonElement[] views = root.TryGetProperty("bufferViews", out var vEl) ? vEl.EnumerateArray().ToArray() : [];
        JsonElement[] accessors = root.TryGetProperty("accessors", out var aEl) ? aEl.EnumerateArray().ToArray() : [];
        JsonElement[] meshes = root.TryGetProperty("meshes", out var mEl) ? mEl.EnumerateArray().ToArray() : [];
        JsonElement[] nodes = root.TryGetProperty("nodes", out var nEl) ? nEl.EnumerateArray().ToArray() : [];
        BaseColorTex?[] materials = ReadMaterials(root, baseDir, buffers, views);

        var acc = new MeshAccumulator(Path.GetFileNameWithoutExtension(path));
        var visited = new HashSet<int>();

        if (root.TryGetProperty("scenes", out var scenesEl))
            foreach (var scene in scenesEl.EnumerateArray())
                if (scene.TryGetProperty("nodes", out var rootsEl))
                    foreach (var r in rootsEl.EnumerateArray())
                        WalkNode(r.GetInt32(), Matrix4x4.Identity, nodes, meshes, buffers, views, accessors, acc, visited, materials);

        // Nodes outside any scene still get imported with identity transform.
        for (int i = 0; i < nodes.Length; i++)
            if (!visited.Contains(i))
                WalkNode(i, Matrix4x4.Identity, nodes, meshes, buffers, views, accessors, acc, visited, materials);

        if (acc.Positions.Count == 0 || acc.Indices.Count == 0)
            throw new InvalidDataException($"glTF contains no geometry: {path}");

        var mesh = acc.ToMesh();
        originalTriangles = mesh.TriangleCount;
        if (mesh.TriangleCount > maxTriangles)
            mesh = Decimate(mesh, maxTriangles);
        return mesh;
    }

    /// <summary>Relative URIs (buffers, images) a .gltf references — used to copy
    /// the whole package (model + .bin + textures) into Assets/.</summary>
    public static IReadOnlyList<string> GetReferencedUris(string gltfPath)
    {
        var result = new List<string>();
        using var doc = JsonDocument.Parse(File.ReadAllText(gltfPath));
        var root = doc.RootElement;

        void Collect(string property)
        {
            if (!root.TryGetProperty(property, out var arr)) return;
            foreach (var item in arr.EnumerateArray())
            {
                if (!item.TryGetProperty("uri", out var u)) continue;
                string uri = u.GetString() ?? "";
                if (uri.Length == 0 || uri.StartsWith("data:", StringComparison.Ordinal)) continue;
                if (!result.Contains(uri, StringComparer.OrdinalIgnoreCase))
                    result.Add(uri);
            }
        }

        Collect("buffers");
        Collect("images");
        return result;
    }

    /// <summary>
    /// Registers every *.gltf under <paramref name="directory"/> (recursive) into
    /// the MeshLibrary so saved scenes keep their models after a restart.
    /// Broken files are skipped; returns the number of meshes registered.
    /// </summary>
    public static int RegisterDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return 0;

        int count = 0;
        foreach (string file in Directory.GetFiles(directory, "*.gltf", SearchOption.AllDirectories))
        {
            try
            {
                MeshLibrary.Register(Load(file));
                count++;
            }
            catch
            {
                // A broken file must not hide the other models.
            }
        }
        return count;
    }

    // ---------- base-color materials ----------

    private sealed class BaseColorTex
    {
        public Texture2D? Texture;
        public int TexCoord;
        public Vector2 Offset;
        public Vector2 Scale = Vector2.One;
        public float Rotation;
        public bool ClampU;
        public bool ClampV;
    }

    private static BaseColorTex?[] ReadMaterials(JsonElement root, string baseDir, byte[][] buffers, JsonElement[] views)
    {
        if (!root.TryGetProperty("materials", out var matsEl)) return [];

        JsonElement[] textures = root.TryGetProperty("textures", out var tEl) ? tEl.EnumerateArray().ToArray() : [];
        JsonElement[] images = root.TryGetProperty("images", out var iEl) ? iEl.EnumerateArray().ToArray() : [];
        JsonElement[] samplers = root.TryGetProperty("samplers", out var sEl) ? sEl.EnumerateArray().ToArray() : [];

        var result = new BaseColorTex?[matsEl.GetArrayLength()];
        int mi = 0;
        foreach (var mat in matsEl.EnumerateArray())
        {
            if (mat.TryGetProperty("pbrMetallicRoughness", out var pbr) &&
                pbr.TryGetProperty("baseColorTexture", out var bct) &&
                bct.TryGetProperty("index", out var texIdxEl))
            {
                int texIdx = texIdxEl.GetInt32();
                var info = new BaseColorTex
                {
                    TexCoord = bct.TryGetProperty("texCoord", out var tc) ? tc.GetInt32() : 0,
                };

                if (bct.TryGetProperty("extensions", out var ext) &&
                    ext.TryGetProperty("KHR_texture_transform", out var tr))
                {
                    if (tr.TryGetProperty("offset", out var off))
                    {
                        var o = ReadFloatArray(off, 2);
                        info.Offset = new Vector2(o[0], o[1]);
                    }
                    if (tr.TryGetProperty("scale", out var sc))
                    {
                        var s2 = ReadFloatArray(sc, 2);
                        info.Scale = new Vector2(s2[0], s2[1]);
                    }
                    if (tr.TryGetProperty("rotation", out var rot))
                        info.Rotation = rot.GetSingle();
                }

                if ((uint)texIdx < (uint)textures.Length)
                {
                    var tex = textures[texIdx];
                    if (tex.TryGetProperty("source", out var src) && src.GetInt32() < images.Length)
                        info.Texture = LoadImage(images[src.GetInt32()], baseDir, buffers, views);
                    if (tex.TryGetProperty("sampler", out var smp) && smp.GetInt32() < samplers.Length)
                    {
                        var sampler = samplers[smp.GetInt32()];
                        info.ClampU = sampler.TryGetProperty("wrapS", out var wu) && wu.GetInt32() == 33071;
                        info.ClampV = sampler.TryGetProperty("wrapT", out var wv) && wv.GetInt32() == 33071;
                    }
                }

                if (info.Texture != null) result[mi] = info;
            }
            mi++;
        }
        return result;
    }

    private static Texture2D? LoadImage(JsonElement image, string baseDir, byte[][] buffers, JsonElement[] views)
    {
        try
        {
            if (image.TryGetProperty("uri", out var u))
            {
                string uri = u.GetString() ?? "";
                if (uri.StartsWith("data:", StringComparison.Ordinal))
                {
                    int comma = uri.IndexOf(',');
                    if (comma < 0) return null;
                    string head = uri[..comma];
                    string data = uri[(comma + 1)..];
                    return Texture2D.Decode(head.Contains("base64", StringComparison.OrdinalIgnoreCase)
                        ? Convert.FromBase64String(data)
                        : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data)));
                }
                return Texture2D.Load(Path.GetFullPath(Path.Combine(baseDir, Uri.UnescapeDataString(uri))));
            }

            if (image.TryGetProperty("bufferView", out var bv) &&
                (uint)bv.GetInt32() < (uint)views.Length)
            {
                var view = views[bv.GetInt32()];
                int bufIdx = view.GetProperty("buffer").GetInt32();
                int off = view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
                int len = view.GetProperty("byteLength").GetInt32();
                if ((uint)bufIdx >= (uint)buffers.Length || off < 0 || len <= 0 || off + len > buffers[bufIdx].Length)
                    return null;
                var bytes = new byte[len];
                Buffer.BlockCopy(buffers[bufIdx], off, bytes, 0, len);
                return Texture2D.Decode(bytes);
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static float[] TransformUv(float[] uv, BaseColorTex bc)
    {
        if (bc.Offset == Vector2.Zero && bc.Scale == Vector2.One && bc.Rotation == 0f)
            return uv;

        var result = new float[uv.Length];
        float cos = MathF.Cos(bc.Rotation);
        float sin = MathF.Sin(bc.Rotation);
        for (int i = 0; i + 1 < uv.Length; i += 2)
        {
            float x = uv[i] * bc.Scale.X;
            float y = uv[i + 1] * bc.Scale.Y;
            result[i] = bc.Offset.X + x * cos - y * sin;
            result[i + 1] = bc.Offset.Y + x * sin + y * cos;
        }
        return result;
    }

    // ---------- scene graph ----------

    private static void WalkNode(int index, Matrix4x4 parent, JsonElement[] nodes, JsonElement[] meshes,
        byte[][] buffers, JsonElement[] views, JsonElement[] accessors, MeshAccumulator acc, HashSet<int> visited,
        BaseColorTex?[] materials)
    {
        if ((uint)index >= (uint)nodes.Length || !visited.Add(index)) return;

        var node = nodes[index];
        Matrix4x4 world = ReadLocalMatrix(node) * parent;

        if (node.TryGetProperty("mesh", out var meshEl) && meshEl.GetInt32() < meshes.Length)
            AppendMesh(meshes[meshEl.GetInt32()], world, buffers, views, accessors, acc, materials);

        if (node.TryGetProperty("children", out var children))
            foreach (var c in children.EnumerateArray())
                WalkNode(c.GetInt32(), world, nodes, meshes, buffers, views, accessors, acc, visited, materials);
    }

    private static Matrix4x4 ReadLocalMatrix(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var mEl))
        {
            float[] e = ReadFloatArray(mEl, 16);
            // glTF stores column-major; System.Numerics is row-major with row-vector convention.
            return new Matrix4x4(
                e[0], e[4], e[8],  e[12],
                e[1], e[5], e[9],  e[13],
                e[2], e[6], e[10], e[14],
                e[3], e[7], e[11], e[15]);
        }

        Vector3 s = node.TryGetProperty("scale", out var sEl) ? ReadVec3(sEl) : Vector3.One;
        Quaternion r = node.TryGetProperty("rotation", out var rEl) ? ReadQuat(rEl) : Quaternion.Identity;
        Vector3 t = node.TryGetProperty("translation", out var tEl) ? ReadVec3(tEl) : Vector3.Zero;
        return Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
    }

    private static Vector3 ReadVec3(JsonElement el)
    {
        float[] v = ReadFloatArray(el, 3);
        return new Vector3(v[0], v[1], v[2]);
    }

    private static Quaternion ReadQuat(JsonElement el)
    {
        float[] v = ReadFloatArray(el, 4);
        return new Quaternion(v[0], v[1], v[2], v[3]); // x, y, z, w
    }

    private static float[] ReadFloatArray(JsonElement el, int expected)
    {
        var list = new float[expected];
        int i = 0;
        foreach (var x in el.EnumerateArray())
        {
            if (i >= expected) break;
            list[i++] = x.GetSingle();
        }
        return list;
    }

    private static void AppendMesh(JsonElement mesh, Matrix4x4 world, byte[][] buffers,
        JsonElement[] views, JsonElement[] accessors, MeshAccumulator acc, BaseColorTex?[] materials)
    {
        if (!mesh.TryGetProperty("primitives", out var prims)) return;

        foreach (var prim in prims.EnumerateArray())
        {
            // Only triangle lists (default mode 4) are supported.
            if (prim.TryGetProperty("mode", out var modeEl) && modeEl.GetInt32() != 4) continue;
            if (!prim.TryGetProperty("attributes", out var attrs)) continue;
            if (!attrs.TryGetProperty("POSITION", out var pAcc)) continue;

            float[] pos = ReadNumbers(pAcc.GetInt32(), accessors, views, buffers);
            float[]? norm = attrs.TryGetProperty("NORMAL", out var nAcc)
                ? ReadNumbers(nAcc.GetInt32(), accessors, views, buffers) : null;
            float[]? uv = attrs.TryGetProperty("TEXCOORD_0", out var tAcc)
                ? ReadNumbers(tAcc.GetInt32(), accessors, views, buffers) : null;

            int matIdx = prim.TryGetProperty("material", out var mEl) ? mEl.GetInt32() : -1;
            BaseColorTex? bc = (uint)matIdx < (uint)materials.Length ? materials[matIdx] : null;
            Texture2D? tex = bc?.Texture;
            float[]? sampleUv = null;
            if (tex != null)
            {
                float[]? src = bc!.TexCoord == 1 && attrs.TryGetProperty("TEXCOORD_1", out var t1Acc)
                    ? ReadNumbers(t1Acc.GetInt32(), accessors, views, buffers)
                    : uv;
                if (src != null && src.Length == pos.Length / 3 * 2)
                    sampleUv = TransformUv(src, bc);
            }

            uint[] indices;
            if (prim.TryGetProperty("indices", out var iAcc))
            {
                indices = ReadIndices(iAcc.GetInt32(), accessors, views, buffers);
            }
            else
            {
                int n = pos.Length / 3;
                indices = new uint[n];
                for (int i = 0; i < n; i++) indices[i] = (uint)i;
            }

            acc.AddPrimitive(pos, norm, uv, indices, world, tex, sampleUv, bc);
        }
    }

    // ---------- accessor decoding ----------

    private static byte[][] ReadBuffers(JsonElement root, string baseDir)
    {
        if (!root.TryGetProperty("buffers", out var bufs)) return [];

        var result = new List<byte[]>();
        foreach (var b in bufs.EnumerateArray())
        {
            if (!b.TryGetProperty("uri", out var uriEl))
                throw new NotSupportedException("glTF buffers without a URI (GLB binary chunk) are not supported.");

            string uri = uriEl.GetString() ?? "";
            if (uri.StartsWith("data:", StringComparison.Ordinal))
            {
                int comma = uri.IndexOf(',');
                if (comma < 0) throw new InvalidDataException("Malformed data: URI in glTF buffer.");
                string head = uri[..comma];
                string data = uri[(comma + 1)..];
                result.Add(head.Contains("base64", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(data)
                    : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(data)));
            }
            else
            {
                string full = Path.GetFullPath(Path.Combine(baseDir, Uri.UnescapeDataString(uri)));
                result.Add(File.ReadAllBytes(full));
            }
        }
        return [.. result];
    }

    private static float[] ReadNumbers(int index, JsonElement[] accessors, JsonElement[] views, byte[][] buffers)
    {
        if ((uint)index >= (uint)accessors.Length) return [];
        var acc = accessors[index];

        int count = acc.GetProperty("count").GetInt32();
        int compType = acc.GetProperty("componentType").GetInt32();
        int comps = ComponentCount(acc.GetProperty("type").GetString());
        if (comps == 0 || count <= 0) return [];

        var result = new float[count * comps];
        int compSize = ComponentSize(compType);
        bool normalized = acc.TryGetProperty("normalized", out var nEl) && nEl.GetBoolean();

        if (!acc.TryGetProperty("bufferView", out var bvEl))
            return result; // no view = zero-filled accessor

        ResolveView(bvEl.GetInt32(), acc, views, buffers, comps, compSize,
            out byte[] buf, out int start, out int stride);

        if (compType == 5126 && stride == comps * 4)
        {
            Buffer.BlockCopy(buf, start, result, 0, result.Length * 4);
            return result;
        }

        for (int i = 0; i < count; i++)
        {
            int o = start + i * stride;
            for (int c = 0; c < comps; c++, o += compSize)
                result[i * comps + c] = ReadComponent(buf, o, compType, normalized);
        }
        return result;
    }

    private static uint[] ReadIndices(int index, JsonElement[] accessors, JsonElement[] views, byte[][] buffers)
    {
        if ((uint)index >= (uint)accessors.Length) return [];
        var acc = accessors[index];

        int count = acc.GetProperty("count").GetInt32();
        int compType = acc.GetProperty("componentType").GetInt32();
        if (compType is not (5121 or 5123 or 5125))
            throw new InvalidDataException($"Unsupported index componentType {compType}.");
        if (!acc.TryGetProperty("bufferView", out var bvEl))
            throw new InvalidDataException("Index accessor has no bufferView.");

        int comps = 1;
        int compSize = ComponentSize(compType);
        ResolveView(bvEl.GetInt32(), acc, views, buffers, comps, compSize,
            out byte[] buf, out int start, out int stride);

        var result = new uint[count];
        for (int i = 0; i < count; i++)
        {
            int o = start + i * stride;
            result[i] = compType switch
            {
                5121 => buf[o],
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(o)),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(o)),
            };
        }
        return result;
    }

    private static void ResolveView(int viewIndex, JsonElement acc, JsonElement[] views, byte[][] buffers,
        int comps, int compSize, out byte[] buf, out int start, out int stride)
    {
        if ((uint)viewIndex >= (uint)views.Length)
            throw new InvalidDataException($"bufferView {viewIndex} out of range.");
        var view = views[viewIndex];

        int bufferIndex = view.GetProperty("buffer").GetInt32();
        if ((uint)bufferIndex >= (uint)buffers.Length)
            throw new InvalidDataException($"buffer {bufferIndex} out of range.");
        buf = buffers[bufferIndex];

        int viewOffset = view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0;
        int accOffset = acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0;
        stride = view.TryGetProperty("byteStride", out var st) ? st.GetInt32() : compSize * comps;
        start = viewOffset + accOffset;

        int count = acc.GetProperty("count").GetInt32();
        long last = (long)start + (long)(count - 1) * stride + (long)comps * compSize;
        if (start < 0 || stride <= 0 || last > buf.Length)
            throw new InvalidDataException("Accessor data lies outside its buffer.");
    }

    private static float ReadComponent(byte[] buf, int offset, int compType, bool normalized) => compType switch
    {
        5126 => BitConverter.ToSingle(buf, offset),
        5123 => Scale(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(offset)), normalized, ushort.MaxValue),
        5125 => Scale(BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(offset)), normalized, uint.MaxValue),
        5121 => Scale(buf[offset], normalized, byte.MaxValue),
        5122 => Scale(BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(offset)), normalized),
        5120 => Scale((sbyte)buf[offset], normalized),
        _ => throw new InvalidDataException($"Unsupported componentType {compType}."),
    };

    private static float Scale(uint v, bool normalized, uint max)
        => normalized ? v / (float)max : v;

    private static float Scale(ushort v, bool normalized, uint max)
        => normalized ? v / (float)max : v;

    private static float Scale(byte v, bool normalized, uint max)
        => normalized ? v / (float)max : v;

    private static float Scale(int v, bool normalized)
        => normalized ? MathF.Max(v / 32767f, -1f) : v;

    private static float Scale(sbyte v, bool normalized)
        => normalized ? MathF.Max(v / 127f, -1f) : v;

    private static int ComponentCount(string? type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        "MAT2" => 4,
        "MAT3" => 9,
        "MAT4" => 16,
        _ => 0,
    };

    private static int ComponentSize(int compType) => compType switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5124 or 5125 or 5126 => 4,
        _ => throw new InvalidDataException($"Unsupported componentType {compType}."),
    };

    // ---------- vertex-cluster decimation ----------

    private static Mesh Decimate(Mesh src, int maxTriangles)
    {
        var verts = src.Vertices;
        if (verts.Length < 9 || src.Indices.Length < 3) return src;

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i + 2 < verts.Length; i += 3)
        {
            if (verts[i] < minX) minX = verts[i];
            if (verts[i] > maxX) maxX = verts[i];
            if (verts[i + 1] < minY) minY = verts[i + 1];
            if (verts[i + 1] > maxY) maxY = verts[i + 1];
            if (verts[i + 2] < minZ) minZ = verts[i + 2];
            if (verts[i + 2] > maxZ) maxZ = verts[i + 2];
        }

        float extent = MathF.Max(maxX - minX, MathF.Max(maxY - minY, maxZ - minZ));
        if (extent <= 0 || float.IsNaN(extent)) return src;

        Mesh? best = null, last = null;
        for (int cells = 16; cells <= 1024; cells *= 2)
        {
            Mesh? m = Cluster(src, extent / cells);
            if (m == null) continue;
            last = m;
            if (m.TriangleCount <= maxTriangles) best = m;
            else break;
        }
        return best ?? last ?? src;
    }

    private static Mesh? Cluster(Mesh src, float cellSize)
    {
        if (!(cellSize > 0) || float.IsNaN(cellSize)) return null;

        var verts = src.Vertices;
        var idx = src.Indices;
        int vertexCount = verts.Length / 3;
        if (vertexCount == 0 || idx.Length < 3) return null;

        bool hasNormals = src.Normals.Length == verts.Length;
        bool hasUv = src.TexCoords.Length == vertexCount * 2;
        bool hasColors = src.VertexColors.Length == vertexCount * 4;

        var map = new Dictionary<long, int>(Math.Min(vertexCount, 1 << 16));
        var newPos = new List<float>(Math.Min(vertexCount, 1 << 14) * 3);
        var newNorm = hasNormals ? new List<float>(Math.Min(vertexCount, 1 << 14) * 3) : null;
        var newUv = hasUv ? new List<float>(Math.Min(vertexCount, 1 << 14) * 2) : null;
        var newCol = hasColors ? new List<float>(Math.Min(vertexCount, 1 << 14) * 4) : null;
        var newIdx = new List<uint>(Math.Min(idx.Length, 96_000));

        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            uint a = Map(idx[t]);
            uint b = Map(idx[t + 1]);
            uint c = Map(idx[t + 2]);
            if (a == b || b == c || a == c) continue;
            newIdx.Add(a);
            newIdx.Add(b);
            newIdx.Add(c);
        }
        if (newIdx.Count == 0) return null;

        return new Mesh(src.Name)
        {
            Vertices = [.. newPos],
            Indices = [.. newIdx],
            Normals = newNorm != null ? [.. newNorm] : [],
            TexCoords = newUv != null ? [.. newUv] : [],
            VertexColors = newCol != null ? [.. newCol] : [],
            HasTexture = src.HasTexture,
        };

        uint Map(uint vi)
        {
            if ((uint)vi >= (uint)vertexCount)
                throw new InvalidDataException($"Index {vi} out of range ({vertexCount} vertices).");

            int i3 = (int)vi * 3;
            float x = verts[i3], y = verts[i3 + 1], z = verts[i3 + 2];
            long key = (((long)MathF.Floor(x / cellSize) + 0x100000) << 42)
                     | (((long)MathF.Floor(y / cellSize) + 0x100000) << 21)
                     | ((long)MathF.Floor(z / cellSize) + 0x100000);
            if (map.TryGetValue(key, out int id)) return (uint)id;

            id = newPos.Count / 3;
            newPos.Add(x);
            newPos.Add(y);
            newPos.Add(z);
            if (newNorm != null)
            {
                newNorm.Add(src.Normals[i3]);
                newNorm.Add(src.Normals[i3 + 1]);
                newNorm.Add(src.Normals[i3 + 2]);
            }
            if (newUv != null)
            {
                newUv.Add(src.TexCoords[vi * 2]);
                newUv.Add(src.TexCoords[vi * 2 + 1]);
            }
            if (newCol != null)
            {
                int c4 = (int)vi * 4;
                newCol.Add(src.VertexColors[c4]);
                newCol.Add(src.VertexColors[c4 + 1]);
                newCol.Add(src.VertexColors[c4 + 2]);
                newCol.Add(src.VertexColors[c4 + 3]);
            }
            map[key] = id;
            return (uint)id;
        }
    }

    // ---------- accumulator ----------

    private sealed class MeshAccumulator(string name)
    {
        public List<float> Positions { get; } = new(4096);
        public List<float> Normals { get; } = new(4096);
        public List<float> TexCoords { get; } = new(4096);
        public List<float> Colors { get; } = new(4096);
        public List<uint> Indices { get; } = new(4096);
        public bool HasTexture { get; private set; }
        private bool _normalsComplete = true;
        private bool _uvComplete = true;

        public void AddPrimitive(float[] pos, float[]? norm, float[]? uv, uint[] idx, Matrix4x4 world,
            Texture2D? tex = null, float[]? sampleUv = null, BaseColorTex? bc = null)
        {
            bool identity = world.IsIdentity;
            uint offset = (uint)(Positions.Count / 3);

            if (identity) Positions.AddRange(pos);
            else
            {
                for (int i = 0; i + 2 < pos.Length; i += 3)
                {
                    var p = Vector3.Transform(new Vector3(pos[i], pos[i + 1], pos[i + 2]), world);
                    Positions.Add(p.X);
                    Positions.Add(p.Y);
                    Positions.Add(p.Z);
                }
            }

            if (_normalsComplete)
            {
                if (norm == null || norm.Length != pos.Length) _normalsComplete = false;
                else if (identity) Normals.AddRange(norm);
                else
                {
                    for (int i = 0; i + 2 < norm.Length; i += 3)
                    {
                        var n = Vector3.Normalize(Vector3.TransformNormal(
                            new Vector3(norm[i], norm[i + 1], norm[i + 2]), world));
                        Normals.Add(n.X);
                        Normals.Add(n.Y);
                        Normals.Add(n.Z);
                    }
                }
            }

            if (_uvComplete)
            {
                if (uv == null || uv.Length != pos.Length / 3 * 2) _uvComplete = false;
                else TexCoords.AddRange(uv);
            }

            int vertexCount = pos.Length / 3;
            for (int i = 0; i < vertexCount; i++)
            {
                float r = 1f, g = 1f, b = 1f, a = 1f;
                if (tex != null && sampleUv != null && i * 2 + 1 < sampleUv.Length)
                {
                    var c = SampleTexture(tex, sampleUv[i * 2], sampleUv[i * 2 + 1], bc);
                    r = c.X; g = c.Y; b = c.Z; a = c.W;
                }
                Colors.Add(r);
                Colors.Add(g);
                Colors.Add(b);
                Colors.Add(a);
            }
            if (tex != null) HasTexture = true;

            for (int i = 0; i < idx.Length; i++)
                Indices.Add(offset + idx[i]);
        }

        private static Vector4 SampleTexture(Texture2D t, float u, float v, BaseColorTex? bc)
        {
            if (bc is { ClampU: false }) u -= MathF.Floor(u);
            else u = Math.Clamp(u, 0f, 1f);
            if (bc is { ClampV: false }) v -= MathF.Floor(v);
            else v = Math.Clamp(v, 0f, 1f);

            float x = u * (t.Width - 1);
            float y = v * (t.Height - 1);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Math.Min(x0 + 1, t.Width - 1);
            int y1 = Math.Min(y0 + 1, t.Height - 1);
            float fx = x - x0, fy = y - y0;

            var px = t.Pixels;
            int i00 = (y0 * t.Width + x0) * 4, i10 = (y0 * t.Width + x1) * 4;
            int i01 = (y1 * t.Width + x0) * 4, i11 = (y1 * t.Width + x1) * 4;
            var result = new Vector4();
            for (int ch = 0; ch < 4; ch++)
            {
                float top = px[i00 + ch] + (px[i10 + ch] - px[i00 + ch]) * fx;
                float bot = px[i01 + ch] + (px[i11 + ch] - px[i01 + ch]) * fx;
                float val = (top + (bot - top) * fy) / 255f;
                if (ch == 0) result.X = val;
                else if (ch == 1) result.Y = val;
                else if (ch == 2) result.Z = val;
                else result.W = val;
            }
            return result;
        }

        public Mesh ToMesh()
        {
            var mesh = new Mesh(name)
            {
                Vertices = [.. Positions],
                Indices = [.. Indices],
            };
            if (_normalsComplete && Normals.Count == Positions.Count)
                mesh.Normals = [.. Normals];
            if (_uvComplete && TexCoords.Count == Positions.Count / 3 * 2)
                mesh.TexCoords = [.. TexCoords];
            if (HasTexture && Colors.Count == Positions.Count / 3 * 4)
                mesh.VertexColors = [.. Colors];
            mesh.HasTexture = HasTexture;
            return mesh;
        }
    }
}
