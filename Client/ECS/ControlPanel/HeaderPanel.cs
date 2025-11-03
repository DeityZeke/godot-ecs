#nullable enable

using System;
using Godot;
using UltraSim.UI;

namespace UltraSim.ECS
{
    /// <summary>
    /// Header panel displaying ECS statistics in an 8-column grid (4 label-data pairs).
    /// </summary>
    [ControlPanelSection(defaultOrder: 0, defaultExpanded: true)]
    public partial class HeaderPanel : UIBuilder, IControlPanelSection
    {
        private World _world;

        // Stats labels (data values)
        private Label? _entitiesDataLabel;
        private Label? _archetypesDataLabel;
        private Label? _lastSaveDataLabel;
        private Label? _fpsDataLabel;
        private Label? _frameTimeDataLabel;
        private Label? _tickRateDataLabel;
        private Label? _nextSaveDataLabel;
        private Label? _memUsageDataLabel;
        private SpinBox? _saveRateSpinBox;
        private CheckBox? _autoSaveCheckBox;

        // === THROTTLED UPDATE SYSTEM ===
        private float _updateTimer = 0f;
        private const float UPDATE_INTERVAL = 0.5f; // Update UI twice per second
        private float _deltaSum = 0f;
        private int _frameCount = 0;

        public string Title => "Statistics";
        public string Id => "header_stats";
        public bool IsExpanded { get; set; }

        public HeaderPanel(World world)
        {
            _world = world;
        }

        public Control CreateUI()
        {
            // Main HBox to hold left and right sections
            var mainHBox = CreateHBox();
            mainHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            // === LEFT SECTION (ECS Stats + Performance) ===
            var leftGrid = CreateGrid(columns: 4);
            leftGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            mainHBox.AddChild(leftGrid);

            // Row 1: Entities, Archetypes
            AddContainerLabel(leftGrid, "Entities:");
            _entitiesDataLabel = AddContainerLabel(leftGrid, "0", hAlign: HorizontalAlignment.Right);

            AddContainerLabel(leftGrid, "Archetypes:");
            _archetypesDataLabel = AddContainerLabel(leftGrid, "0", hAlign: HorizontalAlignment.Right);

            // Row 2: FPS, RAM
            AddContainerLabel(leftGrid, "FPS:");
            _fpsDataLabel = AddContainerLabel(leftGrid, "0", hAlign: HorizontalAlignment.Right);

            AddContainerLabel(leftGrid, "RAM:");
            _memUsageDataLabel = AddContainerLabel(leftGrid, "0 MB", hAlign: HorizontalAlignment.Right);

            // Row 3: Frame Time, Tick Rate
            AddContainerLabel(leftGrid, "Frame Time:");
            _frameTimeDataLabel = AddContainerLabel(leftGrid, "0.00ms", hAlign: HorizontalAlignment.Right);

            AddContainerLabel(leftGrid, "Tick Rate:");
            _tickRateDataLabel = AddContainerLabel(leftGrid, "0.000ms", hAlign: HorizontalAlignment.Right);

            // === RIGHT SECTION (Save Info) ===
            var rightGrid = CreateGrid(columns: 2);
            rightGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            mainHBox.AddChild(rightGrid);

            AddContainerLabel(rightGrid, "Last Save:");
            _lastSaveDataLabel = AddContainerLabel(rightGrid, "Never", hAlign: HorizontalAlignment.Right);

            AddContainerLabel(rightGrid, "Next Save:");
            _nextSaveDataLabel = AddContainerLabel(rightGrid, "N/A", hAlign: HorizontalAlignment.Right);

            AddContainerLabel(rightGrid, "Auto Save:");
            var autoSaveContainer = CreateHBox();
            _autoSaveCheckBox = CreateCheckBox(onToggled: OnAutoSaveToggled);
            _autoSaveCheckBox.FocusMode = FocusModeEnum.None;
            autoSaveContainer.AddChild(_autoSaveCheckBox);

            AddContainerLabel(autoSaveContainer, "Rate:", vAlign: VerticalAlignment.Center);

            _saveRateSpinBox = new SpinBox
            {
                MinValue = 1f,
                MaxValue = 600f,
                Step = 1f,
                Value = _world.AutoSaveInterval,
                CustomMinimumSize = new Vector2(100, 0),
                TooltipText = "Auto-save interval in seconds (1-600s)",
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            _saveRateSpinBox.GetLineEdit().AddThemeConstantOverride("minimum_character_width", 0);
            _saveRateSpinBox.ValueChanged += OnSaveRateChanged;
            autoSaveContainer.AddChild(_saveRateSpinBox);

            rightGrid.AddChild(autoSaveContainer);

            return mainHBox;
        }

public void Update(double delta)
{
    if (_world == null) return;

    // === ACCUMULATE SIMULATION FRAME DATA ===
    float deltaF = (float)delta;
    _deltaSum += deltaF;
    _frameCount++;
    _updateTimer += deltaF;

    if (_updateTimer >= UPDATE_INTERVAL)
    {
        // Optimize: cache reciprocal to avoid division in FPS calculation
        float avgDelta = _deltaSum / _frameCount;
        float simFps = avgDelta > 0 ? 1f / avgDelta : 0f;

        // Calculate both frame time (full Godot process) and tick rate (ECS simulation only)
        float frameTimeMs = avgDelta * 1000f;  // Full Godot frame time
        float ecsTickTimeMs = (float)_world.LastTickTimeMs; // ECS simulation only
        ulong memoryMB = OS.GetStaticMemoryUsage() / (1024u * 1024u);

        // UPDATE UI — TRUTH ONLY (cache counts to avoid multiple property accesses)
        int entityCount = _world.EntityCount;
        int archetypeCount = _world.ArchetypeCount;

        _entitiesDataLabel!.Text = entityCount.ToString("N0");
        _archetypesDataLabel!.Text = archetypeCount.ToString("N0");
        _fpsDataLabel!.Text = simFps.ToString("F1");
        _memUsageDataLabel!.Text = $"{memoryMB:N0} MB";
        _frameTimeDataLabel!.Text = frameTimeMs.ToString("F2") + "ms";
        _tickRateDataLabel!.Text = ecsTickTimeMs.ToString("F3") + "ms";

        // Update SpinBox only if value changed (avoid feedback loop)
        float autoSaveInterval = _world.AutoSaveInterval;
        if (Math.Abs((float)_saveRateSpinBox!.Value - autoSaveInterval) > 0.01f)
            _saveRateSpinBox.Value = autoSaveInterval;

        _autoSaveCheckBox!.SetPressedNoSignal(_world.AutoSaveEnabled);

        // Update Last Save time
        var lastSaveTime = _world.LastSaveTime;
        if (lastSaveTime.HasValue)
        {
            var localTime = lastSaveTime.Value.ToLocalTime();
            _lastSaveDataLabel!.Text = localTime.ToString("HH:mm:ss");
        }
        else
        {
            _lastSaveDataLabel!.Text = "Never";
        }

        // Update Next Save time (optimize: combine GetNextSaveTime and GetTimeUntilNextSave calls)
        var timeRemaining = _world.GetTimeUntilNextSave();
        if (timeRemaining.HasValue)
        {
            int seconds = (int)timeRemaining.Value;
            _nextSaveDataLabel!.Text = seconds.ToString() + "s";
        }
        else
        {
            _nextSaveDataLabel!.Text = "N/A";
        }

        // DEBUG: Print truth
        GD.Print($"[UI] FPS: {simFps:F1} | Frame: {frameTimeMs:F2}ms | Tick: {ecsTickTimeMs:F3}ms | Entities: {entityCount:N0}");

        // Reset
        _updateTimer = _deltaSum = 0f;
        _frameCount = 0;
    }
}

        public void OnShow() { }
        public void OnHide() { }

        private void OnAutoSaveToggled(bool enabled)
        {
            if (enabled)
                _world.EnableAutoSave(_world.AutoSaveInterval);
            else
                _world.DisableAutoSave();

            Logging.Logger.Log($"Auto-save {(enabled ? "enabled" : "disabled")}");
        }

        private void OnSaveRateChanged(double value)
        {
            float newInterval = (float)value;
            _world.SetAutoSaveInterval(newInterval);
            Logging.Logger.Log($"Auto-save interval changed to {newInterval:F0}s");
        }
    }
}