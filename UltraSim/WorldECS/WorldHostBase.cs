#nullable enable

using Godot;
using Client.ECS.ControlPanel;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.SIMD;
using UltraSim.ECS.Systems;

namespace UltraSim.WorldECS
{
    /// <summary>
    /// Base ECS bootstrapper - derive for server/client specific hosts.
    /// </summary>
    public abstract partial class WorldHostBase : Node3D, IHost
    {
        [Export] public bool EnableDebugStats = true;
        [Export] public float AutoSaveInterval = 60.0f;

        private World _world = null!;
        private ECSControlPanel _controlPanel = null!;
        private double _accum;
        private double _fpsAccum;
        private int _fpsFrames;
        private int _frameCount = 0;
        private int _worldEndFrameCounter = 0;
        private bool _controlPanelCreated = false;

        protected HostEnvironment HostInfo { get; private set; } = null!;

        protected virtual string RuntimeProfile => "Hybrid";
        protected virtual bool EnableControlPanelUI => true;
        protected virtual bool EnableInputShortcuts => true;
        protected virtual string HostPrefix => $"[{GetType().Name}]";
        protected World ActiveWorld => _world;

        #region IHost Implementation

        public RuntimeContext Runtime { get; private set; } = null!;

        public object GetRootHandle() => GetTree().Root;

        public UltraSim.IO.IIOProfile? GetIOProfile() => null;

        public void Log(LogEntry entry)
        {
            switch (entry.Severity)
            {
                case LogSeverity.Error:
                    GD.PushError(entry.ToString());
                    break;
                case LogSeverity.Warning:
                    GD.PushWarning(entry.ToString());
                    break;
                default:
                    GD.Print(entry.ToString());
                    break;
            }
        }

        public World GetWorld() => _world;

        #endregion

        public abstract EnvironmentType Environment { get; }

        #region Godot Lifecycle

        public override void _Ready()
        {
            HostInfo = BuildHostEnvironment();

            // Create RuntimeContext with environment capture
            Runtime = new RuntimeContext(HostInfo, RuntimeProfile);

            UltraSim.Logging.Host = this;

            // Initialize SIMD manager with detected hardware capabilities
            SimdManager.Initialize(HostInfo.SimdSupport);

            GD.Print("========================================");
            GD.Print("      ECS WORLD INITIALIZATION         ");
            GD.Print("========================================");

            _world = new World(this);
            OnWorldCreated(_world);

            // Subscribe to world events via EventSink
            EventSink.WorldInitialized += HandleWorldInitialized;

            // Register systems
            RegisterSystems();

            // Enable auto-save
            _world.EnableAutoSave(AutoSaveInterval);

            // Initialize world (processes system queues)
            _world.Initialize();

            GD.Print("========================================");
            GD.Print("         ECS WORLD READY                ");
            if (EnableControlPanelUI)
                GD.Print("  Press F12 to open Control Panel      ");
            GD.Print("========================================\n");
        }

        public override void _Process(double delta)
        {
            UltraSim.Logging.DrainToHost();

            BeforeWorldTick(delta);

            var start = Time.GetTicksUsec();
            _world.Tick(delta);
            var end = Time.GetTicksUsec();

            AfterWorldTick(delta);
            HandleWorldFrameProgress();

            double frameMs = (end - start) / 1000.0;
            _world.LastTickTimeMs = frameMs; // Update for UI display
            _fpsAccum += frameMs;
            _fpsFrames++;
            _frameCount++;

            if (_accum >= 1.0)
            {
                if (EnableDebugStats)
                {
                    double avg = _fpsAccum / _fpsFrames;
                    GD.Print($"[ECS] Frame: {avg:F3} ms (avg over {_fpsFrames} frames)");
                }
                _accum = 0;
                _fpsAccum = 0;
                _fpsFrames = 0;
            }
            else
            {
                _accum += delta;
            }
        }

        public override void _ExitTree()
        {
            EventSink.WorldInitialized -= HandleWorldInitialized;
            base._ExitTree();
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                if (EnableControlPanelUI && keyEvent.Keycode == Key.F12 && _controlPanel != null)
                {
                    _controlPanel.Toggle();
                    GetViewport().SetInputAsHandled();
                }
                else if (EnableInputShortcuts && keyEvent.Keycode == Key.F11)
                {
                    _world.Save($"manual_{System.DateTime.Now:yyyyMMdd_HHmmss}.sav");
                }
                else if (EnableInputShortcuts && keyEvent.Keycode == Key.Key9)
                {
                    _world.QuickSave();
                }
                else if (EnableInputShortcuts && keyEvent.Keycode == Key.Key0)
                {
                    _world.QuickLoad();
                }
            }
        }

        #endregion

        #region Helper Methods

        protected virtual void OnWorldCreated(World world) { }
        protected virtual HostEnvironment BuildHostEnvironment() => HostEnvironment.Capture();
        protected virtual void BeforeWorldTick(double delta) { }
        protected virtual void AfterWorldTick(double delta) { }
        protected virtual void OnWorldFrameProgress(int frameIndex) { }
        protected abstract void RegisterSystems();

        protected virtual void CreateControlPanel()
        {
            var frame = new CanvasLayer();
            GetTree().Root.CallDeferred(MethodName.AddChild, frame);

            _controlPanel = new ECSControlPanel();
            _controlPanel.Initialize(_world);
            frame.CallDeferred(MethodName.AddChild, _controlPanel);
        }

        private void HandleWorldInitialized(World world)
        {
            if (!ReferenceEquals(world, _world))
                return;

            GD.Print($"{HostPrefix} World initialized.");
        }

        private void HandleWorldFrameProgress()
        {
            _worldEndFrameCounter++;

            if (EnableControlPanelUI && !_controlPanelCreated && _worldEndFrameCounter >= 1)
            {
                CreateControlPanel();
                _controlPanelCreated = true;
            }

            OnWorldFrameProgress(_worldEndFrameCounter);
        }

        #endregion
    }
}
