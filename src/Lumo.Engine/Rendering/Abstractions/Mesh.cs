namespace Lumo.Engine.Rendering.Abstractions;

/// <summary>
/// Mesh abstraction for vertex data.
/// </summary>
public sealed class Mesh : IDisposable
{
    public float[] Vertices { get; set; } = [];
    public uint[] Indices { get; set; } = [];
    public float[] Normals { get; set; } = [];
    public float[] TexCoords { get; set; } = [];
    public float[] VertexColors { get; set; } = [];
    public bool HasTexture { get; set; }
    public string Name { get; set; } = string.Empty;
    private bool _isDisposed;

    public int VertexCount => Vertices.Length / 3;
    public int TriangleCount => Indices.Length / 3;

    public Mesh(string name = "")
    {
        Name = name;
    }

    /// <summary>
    /// Create a simple triangle mesh.
    /// </summary>
    public static Mesh CreateTriangle()
    {
        return new Mesh("Triangle")
        {
            Vertices = [
                -0.5f, -0.5f, 0.0f,
                 0.5f, -0.5f, 0.0f,
                 0.0f,  0.5f, 0.0f
            ],
            Indices = [0, 1, 2],
            Normals = [
                0.0f, 0.0f, 1.0f,
                0.0f, 0.0f, 1.0f,
                0.0f, 0.0f, 1.0f
            ]
        };
    }

    /// <summary>
    /// Create a quad (two triangles).
    /// </summary>
    public static Mesh CreateQuad()
    {
        return new Mesh("Quad")
        {
            Vertices = [
                -0.5f, -0.5f, 0.0f,
                 0.5f, -0.5f, 0.0f,
                 0.5f,  0.5f, 0.0f,
                -0.5f,  0.5f, 0.0f
            ],
            Indices = [0, 1, 2, 2, 3, 0],
            Normals = [
                0.0f, 0.0f, 1.0f,
                0.0f, 0.0f, 1.0f,
                0.0f, 0.0f, 1.0f,
                0.0f, 0.0f, 1.0f
            ],
            TexCoords = [
                0.0f, 0.0f,
                1.0f, 0.0f,
                1.0f, 1.0f,
                0.0f, 1.0f
            ]
        };
    }

    /// <summary>
    /// Create a 3D cube mesh.
    /// </summary>
    public static Mesh CreateCube()
    {
        return new Mesh("Cube")
        {
            Vertices = [
                // Front face
                -0.5f, -0.5f,  0.5f,
                 0.5f, -0.5f,  0.5f,
                 0.5f,  0.5f,  0.5f,
                -0.5f,  0.5f,  0.5f,
                // Back face
                -0.5f, -0.5f, -0.5f,
                 0.5f, -0.5f, -0.5f,
                 0.5f,  0.5f, -0.5f,
                -0.5f,  0.5f, -0.5f,
                // Top face
                -0.5f,  0.5f, -0.5f,
                 0.5f,  0.5f, -0.5f,
                 0.5f,  0.5f,  0.5f,
                -0.5f,  0.5f,  0.5f,
                // Bottom face
                -0.5f, -0.5f, -0.5f,
                 0.5f, -0.5f, -0.5f,
                 0.5f, -0.5f,  0.5f,
                -0.5f, -0.5f,  0.5f,
                // Right face
                 0.5f, -0.5f, -0.5f,
                 0.5f, -0.5f,  0.5f,
                 0.5f,  0.5f,  0.5f,
                 0.5f,  0.5f, -0.5f,
                // Left face
                -0.5f, -0.5f, -0.5f,
                -0.5f, -0.5f,  0.5f,
                -0.5f,  0.5f,  0.5f,
                -0.5f,  0.5f, -0.5f,
            ],
            Indices = [
                 0,  1,  2,  2,  3,  0,  // Front
                 4,  5,  6,  6,  7,  4,  // Back
                 8,  9, 10, 10, 11,  8,  // Top
                12, 13, 14, 14, 15, 12,  // Bottom
                16, 17, 18, 18, 19, 16,  // Right
                20, 21, 22, 22, 23, 20   // Left
            ],
            Normals = [
                 0,  0,  1,  0,  0,  1,  0,  0,  1,  0,  0,  1,  // Front
                 0,  0, -1,  0,  0, -1,  0,  0, -1,  0,  0, -1,  // Back
                 0,  1,  0,  0,  1,  0,  0,  1,  0,  0,  1,  0,  // Top
                 0, -1,  0,  0, -1,  0,  0, -1,  0,  0, -1,  0,  // Bottom
                 1,  0,  0,  1,  0,  0,  1,  0,  0,  1,  0,  0,  // Right
                -1,  0,  0, -1,  0,  0, -1,  0,  0, -1,  0,  0   // Left
            ]
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
