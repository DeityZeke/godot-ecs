#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Attach to an empty Node3D inside a test scene. When the scene runs, this script
/// incrementally spawns MeshInstance3D nodes (100 per batch), lets the scene settle
/// for a short delay, prints timing stats, and repeats until FPS drops below the
/// configured threshold.  Uses simple movement so Godot has to update transforms.
/// </summary>
[GlobalClass]
public partial class MeshInstanceStress : Node3D
{
    [Export(PropertyHint.Range, "10,1000,10")] public int BatchSize { get; set; } = 100;
    [Export(PropertyHint.Range, "0.1,5.0,0.1")] public float BatchDelaySeconds { get; set; } = 2.0f;
    [Export(PropertyHint.Range, "5,120,1")] public float TargetFps { get; set; } = 20.0f;
    [Export] public float MovementAmplitude { get; set; } = 1.5f;
    [Export] public float MovementSpeed { get; set; } = 0.75f;

    private readonly List<MeshInstance3D> _instances = new();
    private readonly List<Vector3> _movementOffsets = new();
    private SphereMesh? _sharedMesh;
    private bool _spawning;

    public override void _Ready()
    {
        this.CallDeferred("Initialize");
    }

    public void Initialize()
    {
        _sharedMesh = new SphereMesh
        {
            RadialSegments = 8,
            Rings = 4,
            Radius = 0.5f
        };

        _ = RunStressLoop();
    }

    public override void _Process(double delta)
    {
        if (_instances.Count == 0)
            return;

        float t = (float)Time.GetTicksMsec() / 1000.0f;

        for (int i = 0; i < _instances.Count; i++)
        {
            var inst = _instances[i];
            if (inst == null || !IsInstanceValid(inst))
                continue;

            var basePos = _movementOffsets[i];
            float wobble = Mathf.Sin(t * MovementSpeed + i * 0.1f) * MovementAmplitude;
            inst.Position = basePos + new Vector3(0, wobble, 0);
        }
    }

    private async Task RunStressLoop()
    {
        if (_spawning)
            return;

        _spawning = true;
        GD.Print("[MeshInstanceStress] Starting stress test...");

        while (true)
        {
            SpawnBatch(BatchSize);

            if (BatchDelaySeconds > 0)
            {
                await ToSignal(GetTree().CreateTimer(BatchDelaySeconds), SceneTreeTimer.SignalName.Timeout);
            }

            float fps = (float)Engine.GetFramesPerSecond();
            GD.Print($"[MeshInstanceStress] Count={_instances.Count:N0}, FPS={fps:F1}, Memory={Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024 * 1024):F1}MB");

            if (fps <= TargetFps)
            {
                GD.PushWarning($"[MeshInstanceStress] Target FPS {TargetFps} hit. Final count: {_instances.Count:N0}");
                break;
            }
        }

        GD.Print("[MeshInstanceStress] Stress test complete.");
        _spawning = false;
    }

    private void SpawnBatch(int count)
    {
        if (_sharedMesh == null)
            return;

        var random = new Random();
        for (int i = 0; i < count; i++)
        {
            var initialPos = new Vector3(
                random.NextSingle() * 50f - 25f,
                random.NextSingle() * 5f + 2.0f,
                random.NextSingle() * 50f - 25f);

            var meshInstance = new MeshInstance3D
            {
                Mesh = _sharedMesh,
                Position = initialPos,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };

            _movementOffsets.Add(initialPos);
            _instances.Add(meshInstance);
            AddChild(meshInstance);
        }

        GD.Print($"[MeshInstanceStress] Spawned {count} mesh instances (Total {_instances.Count:N0})");
    }
}
