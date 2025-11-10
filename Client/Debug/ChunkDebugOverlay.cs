#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using UltraSim.ECS.Chunk;
using UltraSim.ECS.Components;
using Client.ECS.Rendering;

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
        [Export] public bool HighlightZoneWindows { get; set; } = true;
        [Export] public Color CoreWindowColor { get; set; } = new Color(0.2f, 0.95f, 0.3f, 1.0f);
        [Export] public Color NearWindowColor { get; set; } = new Color(1.0f, 0.6f, 0.2f, 1.0f);
        [Export(PropertyHint.Range, "0.5,4.0,0.1")] public float EdgeFalloffExponent { get; set; } = 1.75f;

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
                    EmitWireframe(wireTool, bounds, WireColor);
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
                    EmitPlane(planeTool, bounds, PlaneColor);
                    added = true;
                }

                if (added)
                {
                    planeTool.SetMaterial(_planeMaterial);
                    planeTool.Commit(_mesh);
                    hasGeometry = true;
                }
            }

            if (HighlightZoneWindows && HybridRenderSharedState.Instance.IsConfigured && _chunkManager != null)
            {
                var sharedState = HybridRenderSharedState.Instance;
                var coreTool = new SurfaceTool();
                coreTool.Begin(Mesh.PrimitiveType.Lines);
                bool drewCore = false;
                var coreSet = new HashSet<ChunkLocation>();
                foreach (var slot in sharedState.CoreWindow.ActiveSlots)
                {
                    coreSet.Add(slot.GlobalLocation);
                    var bounds = _chunkManager.ChunkToWorldBounds(slot.GlobalLocation);
                    EmitWireframe(coreTool, bounds, CoreWindowColor);
                    drewCore = true;
                }
                if (drewCore)
                {
                    coreTool.SetMaterial(_wireMaterial);
                    coreTool.Commit(_mesh);
                    hasGeometry = true;
                }

                var nearTool = new SurfaceTool();
                nearTool.Begin(Mesh.PrimitiveType.Lines);
                bool drewNear = false;
                foreach (var slot in sharedState.NearWindow.ActiveSlots)
                {
                    if (coreSet.Contains(slot.GlobalLocation))
                        continue;
                    var bounds = _chunkManager.ChunkToWorldBounds(slot.GlobalLocation);
                    EmitWireframe(nearTool, bounds, NearWindowColor);
                    drewNear = true;
                }
                if (drewNear)
                {
                    nearTool.SetMaterial(_wireMaterial);
                    nearTool.Commit(_mesh);
                    hasGeometry = true;
                }
            }

            _meshInstance.Visible = hasGeometry;
        }

        private void EmitWireframe(SurfaceTool tool, ChunkBounds bounds, Color color)
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

            EmitLine(tool, c000, c001, color, bounds);
            EmitLine(tool, c000, c010, color, bounds);
            EmitLine(tool, c001, c011, color, bounds);
            EmitLine(tool, c010, c011, color, bounds);

            EmitLine(tool, c100, c101, color, bounds);
            EmitLine(tool, c100, c110, color, bounds);
            EmitLine(tool, c101, c111, color, bounds);
            EmitLine(tool, c110, c111, color, bounds);

            EmitLine(tool, c000, c100, color, bounds);
            EmitLine(tool, c001, c101, color, bounds);
            EmitLine(tool, c010, c110, color, bounds);
            EmitLine(tool, c011, c111, color, bounds);
        }

        private void EmitPlane(SurfaceTool tool, ChunkBounds bounds, Color color)
        {
            Vector3 v0 = new(bounds.MinX, bounds.MinY, bounds.MinZ);
            Vector3 v1 = new(bounds.MaxX, bounds.MinY, bounds.MinZ);
            Vector3 v2 = new(bounds.MaxX, bounds.MinY, bounds.MaxZ);
            Vector3 v3 = new(bounds.MinX, bounds.MinY, bounds.MaxZ);

            EmitQuad(tool, v0, v1, v2, v3, color, bounds);
        }

        private void EmitLine(SurfaceTool tool, Vector3 a, Vector3 b, Color color, ChunkBounds bounds)
        {
            tool.SetColor(GetEdgeWeightedColor(color, a, bounds));
            tool.AddVertex(a);
            tool.SetColor(GetEdgeWeightedColor(color, b, bounds));
            tool.AddVertex(b);
        }

        private void EmitQuad(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, ChunkBounds bounds)
        {
            tool.SetColor(GetEdgeWeightedColor(color, a, bounds));
            tool.AddVertex(a);
            tool.SetColor(GetEdgeWeightedColor(color, b, bounds));
            tool.AddVertex(b);
            tool.SetColor(GetEdgeWeightedColor(color, c, bounds));
            tool.AddVertex(c);

            tool.SetColor(GetEdgeWeightedColor(color, a, bounds));
            tool.AddVertex(a);
            tool.SetColor(GetEdgeWeightedColor(color, c, bounds));
            tool.AddVertex(c);
            tool.SetColor(GetEdgeWeightedColor(color, d, bounds));
            tool.AddVertex(d);
        }

        private Color GetEdgeWeightedColor(Color baseColor, Vector3 position, ChunkBounds bounds)
        {
            float weight = ComputeEdgeWeight(position, bounds);
            float alpha = baseColor.A * weight;
            return new Color(baseColor.R, baseColor.G, baseColor.B, alpha);
        }

        private float ComputeEdgeWeight(Vector3 position, ChunkBounds bounds)
        {
            float halfX = Math.Max((bounds.MaxX - bounds.MinX) * 0.5f, 0.0001f);
            float halfY = Math.Max((bounds.MaxY - bounds.MinY) * 0.5f, 0.0001f);
            float halfZ = Math.Max((bounds.MaxZ - bounds.MinZ) * 0.5f, 0.0001f);

            float centerX = (bounds.MinX + bounds.MaxX) * 0.5f;
            float centerY = (bounds.MinY + bounds.MaxY) * 0.5f;
            float centerZ = (bounds.MinZ + bounds.MaxZ) * 0.5f;

            float dx = Math.Abs(position.X - centerX) / halfX;
            float dy = Math.Abs(position.Y - centerY) / halfY;
            float dz = Math.Abs(position.Z - centerZ) / halfZ;

            float normalized = Mathf.Clamp(Math.Max(dx, Math.Max(dy, dz)), 0f, 1f);
            return Mathf.Pow(normalized, EdgeFalloffExponent);
        }
    }
}
