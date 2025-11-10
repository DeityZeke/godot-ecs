#nullable enable

using Godot;
using System.Collections.Generic;

namespace Client.Terrain
{
    /// <summary>
    /// Container for generated terrain mesh data.
    /// Holds vertices, normals, UVs, and indices ready for GPU upload.
    /// </summary>
    public sealed class TerrainMesh
    {
        public List<Vector3> Vertices { get; } = new();
        public List<Vector3> Normals { get; } = new();
        public List<Vector2> UVs { get; } = new();
        public List<int> Indices { get; } = new();
        public List<Color> Colors { get; } = new(); // Optional: per-vertex tinting

        public int VertexCount => Vertices.Count;
        public int TriangleCount => Indices.Count / 3;

        /// <summary>
        /// Clears all mesh data for reuse.
        /// </summary>
        public void Clear()
        {
            Vertices.Clear();
            Normals.Clear();
            UVs.Clear();
            Indices.Clear();
            Colors.Clear();
        }

        /// <summary>
        /// Converts mesh data to Godot ArrayMesh for rendering.
        /// </summary>
        public ArrayMesh ToArrayMesh()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);

            // Convert to Godot arrays
            arrays[(int)Mesh.ArrayType.Vertex] = Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = Normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = UVs.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = Indices.ToArray();

            if (Colors.Count > 0)
            {
                arrays[(int)Mesh.ArrayType.Color] = Colors.ToArray();
            }

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            return mesh;
        }

        /// <summary>
        /// Estimates memory usage in bytes.
        /// </summary>
        public int EstimateMemoryBytes()
        {
            // Vector3 = 12 bytes, Vector2 = 8 bytes, int = 4 bytes, Color = 16 bytes
            return (Vertices.Count * 12) +
                   (Normals.Count * 12) +
                   (UVs.Count * 8) +
                   (Indices.Count * 4) +
                   (Colors.Count * 16);
        }

        /// <summary>
        /// Adds a quad to the mesh (two triangles).
        /// </summary>
        public void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3)
        {
            int baseIndex = Vertices.Count;

            // Add vertices
            Vertices.Add(v0);
            Vertices.Add(v1);
            Vertices.Add(v2);
            Vertices.Add(v3);

            // Add normals (same for all 4 vertices)
            Normals.Add(normal);
            Normals.Add(normal);
            Normals.Add(normal);
            Normals.Add(normal);

            // Add UVs
            UVs.Add(uv0);
            UVs.Add(uv1);
            UVs.Add(uv2);
            UVs.Add(uv3);

            // Add indices (two triangles: 0-1-2, 0-2-3)
            Indices.Add(baseIndex + 0);
            Indices.Add(baseIndex + 1);
            Indices.Add(baseIndex + 2);

            Indices.Add(baseIndex + 0);
            Indices.Add(baseIndex + 2);
            Indices.Add(baseIndex + 3);
        }
    }
}
