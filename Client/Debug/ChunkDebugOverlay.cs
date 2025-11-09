#nullable enable

using System.Collections.Generic;
using Godot;
using UltraSim.ECS.Chunk;
using UltraSim.ECS.Components;

namespace Client.Debug
{
    /// <summary>
    /// Draws wireframe boxes (and optional translucent quads) for every active chunk.
    /// Helps visualize chunk boundaries directly inside the Godot scene view.
    /// </summary>
    [GlobalClass]
    public partial class ChunkDebugOverlay : Node3D
    {
        [ExportCategory("Visibility")]
        [Export] public bool OverlayEnabled { get; set; } = true;
        [Export] public bool ShowWireframes { get; set; } = true;
        [Export] public bool ShowPlanes { get; set; } = true;
        [Export(PropertyHint.Range, "0.05,5.0,0.05")] public float RefreshIntervalSeconds { get; set; } = 0.5f;
        [Export] public bool AutoRefresh { get; set; } = true;

        [ExportCategory("Colors")]
        [Export] public Color WireColor { get; set; } = new Color(0.1f, 0.8f, 1.0f, 0.9f);
        [Export] public Color PlaneColor { get; set; } = new Color(0.0f, 0.5f, 1.0f, 0.15f);

        private readonly ArrayMesh _mesh = new();
        private MeshInstance3D _meshInstance = null!;
        private StandardMaterial3D _wireMaterial = null!;
        private StandardMaterial3D _planeMaterial = null!;

        private ChunkManager? _chunkManager;
        private double _refreshTimer;

        public override void _Ready()
        {
            _meshInstance = new MeshInstance3D
            {
                Name = "ChunkDebugMesh",
                Mesh = _mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false
            };

            _wireMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };

            _planeMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                NoDepthTest = false
            };

            AddChild(_meshInstance);
            _refreshTimer = RefreshIntervalSeconds;
        }

        public override void _Process(double delta)
        {
            if (!OverlayEnabled || _chunkManager == null)
            {
                if (_meshInstance != null)
                    _meshInstance.Visible = false;
                return;
            }

            if (!AutoRefresh)
                return;

            _refreshTimer -= delta;
            if (_refreshTimer <= 0.0)
            {
                RefreshNow();
                _refreshTimer = RefreshIntervalSeconds;
            }
        }

        public void SetChunkManager(ChunkManager? manager)
        {
            _chunkManager = manager;
            if (IsInsideTree())
            {
                RefreshNow();
            }
        }

        public void RefreshNow()
        {
            _refreshTimer = RefreshIntervalSeconds;
            RebuildGeometry();
        }

        private void RebuildGeometry()
        {
            _mesh.ClearSurfaces();

            if (!OverlayEnabled || _chunkManager == null)
            {
                _meshInstance.Visible = false;
                return;
            }

            bool hasGeometry = false;
            var chunks = _chunkManager.EnumerateChunks();

            if (ShowWireframes)
            {
                var wireTool = new SurfaceTool();
                wireTool.Begin(Mesh.PrimitiveType.Lines);
                foreach (var (location, _) in chunks)
                {
                    var bounds = _chunkManager.ChunkToWorldBounds(location);
                    EmitWireframe(wireTool, bounds);
                    hasGeometry = true;
                }

                if (hasGeometry)
                {
                    wireTool.SetMaterial(_wireMaterial);
                    wireTool.Commit(_mesh);
                }
            }

            if (ShowPlanes)
            {
                bool added = false;
                var planeTool = new SurfaceTool();
                planeTool.Begin(Mesh.PrimitiveType.Triangles);
                foreach (var (location, _) in chunks)
                {
                    var bounds = _chunkManager.ChunkToWorldBounds(location);
                    EmitPlane(planeTool, bounds);
                    added = true;
                }

                if (added)
                {
                    planeTool.SetMaterial(_planeMaterial);
                    planeTool.Commit(_mesh);
                    hasGeometry = true;
                }
            }

            _meshInstance.Visible = hasGeometry;
        }

        private void EmitWireframe(SurfaceTool tool, ChunkBounds bounds)
        {
            Vector3 min = new(bounds.MinX, bounds.MinY, bounds.MinZ);
            Vector3 max = new(bounds.MaxX, bounds.MaxY, bounds.MaxZ);

            Vector3 c000 = min;
            Vector3 c001 = new(max.X, min.Y, min.Z);
            Vector3 c010 = new(min.X, max.Y, min.Z);
            Vector3 c011 = new(max.X, max.Y, min.Z);
            Vector3 c100 = new(min.X, min.Y, max.Z);
            Vector3 c101 = new(max.X, min.Y, max.Z);
            Vector3 c110 = new(min.X, max.Y, max.Z);
            Vector3 c111 = max;

            EmitLine(tool, c000, c001);
            EmitLine(tool, c000, c010);
            EmitLine(tool, c001, c011);
            EmitLine(tool, c010, c011);

            EmitLine(tool, c100, c101);
            EmitLine(tool, c100, c110);
            EmitLine(tool, c101, c111);
            EmitLine(tool, c110, c111);

            EmitLine(tool, c000, c100);
            EmitLine(tool, c001, c101);
            EmitLine(tool, c010, c110);
            EmitLine(tool, c011, c111);
        }

        private void EmitPlane(SurfaceTool tool, ChunkBounds bounds)
        {
            Vector3 v0 = new(bounds.MinX, bounds.MinY, bounds.MinZ);
            Vector3 v1 = new(bounds.MaxX, bounds.MinY, bounds.MinZ);
            Vector3 v2 = new(bounds.MaxX, bounds.MinY, bounds.MaxZ);
            Vector3 v3 = new(bounds.MinX, bounds.MinY, bounds.MaxZ);

            EmitQuad(tool, v0, v1, v2, v3);
        }

        private void EmitLine(SurfaceTool tool, Vector3 a, Vector3 b)
        {
            tool.SetColor(WireColor);
            tool.AddVertex(a);
            tool.SetColor(WireColor);
            tool.AddVertex(b);
        }

        private void EmitQuad(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            tool.SetColor(PlaneColor);
            tool.AddVertex(a);
            tool.SetColor(PlaneColor);
            tool.AddVertex(b);
            tool.SetColor(PlaneColor);
            tool.AddVertex(c);

            tool.SetColor(PlaneColor);
            tool.AddVertex(a);
            tool.SetColor(PlaneColor);
            tool.AddVertex(c);
            tool.SetColor(PlaneColor);
            tool.AddVertex(d);
        }
    }
}
