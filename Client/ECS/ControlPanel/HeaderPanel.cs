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
        private Label? _nextSaveDataLabel;
        private Label? _memUsageDataLabel;
        private Label? _saveRateDataLabel;
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
            var grid = CreateGrid(columns: 8);

            // === ROW 1 ===
            AddContainerLabel(grid, "Entities:");
            _entitiesDataLabel = AddContainerLabel(grid, "0", hAlign: HorizontalAlignment.Right);

            AddContainerLabel(grid, "Archetypes:");
            _archetypesDataLabel = AddContainerLabel(grid, "0", hAlign: HorizontalAlignment.Right);

            grid.AddChild(new Control());
            grid.AddChild(new Control());

            AddContainerLabel(grid, "Last Save:");
            _lastSaveDataLabel = AddContainerLabel(grid, "Never", hAlign: HorizontalAlignment.Right);

            // === ROW 2 ===
            AddContainerLabel(grid, "FPS:");
            _fpsDataLabel = AddContainerLabel(grid, "0", hAlign: HorizontalAlignment.Right);

            AddContainerLabel(grid, "Frame Time:");
            _frameTimeDataLabel = AddContainerLabel(grid, "0.00ms", hAlign: HorizontalAlignment.Right);

            grid.AddChild(new Control());
            grid.AddChild(new Control());

            AddContainerLabel(grid, "Next Save:");
            _nextSaveDataLabel = AddContainerLabel(grid, "N/A", hAlign: HorizontalAlignment.Right);

            // === ROW 3 ===
            AddContainerLabel(grid, "Mem Usage:");
            _memUsageDataLabel = AddContainerLabel(grid, "0 MB", hAlign: HorizontalAlignment.Right);

            grid.AddChild(new Control());
            grid.AddChild(new Control());
            grid.AddChild(new Control());
            grid.AddChild(new Control());

            AddContainerLabel(grid, "Auto Save:");

            var autoSaveContainer = CreateHBox();
            _autoSaveCheckBox = CreateCheckBox(onToggled: OnAutoSaveToggled);
            _autoSaveCheckBox.FocusMode = FocusModeEnum.None;
            autoSaveContainer.AddChild(_autoSaveCheckBox);

            AddContainerLabel(autoSaveContainer, "Rate:", vAlign: VerticalAlignment.Center);
            _saveRateDataLabel = AddContainerLabel(autoSaveContainer, "60.000s", vAlign: VerticalAlignment.Center);

            grid.AddChild(autoSaveContainer);

            return grid;
        }

public void Update(double delta)
{
    if (_world == null) return;

    // === ACCUMULATE SIMULATION FRAME DATA ===
    _deltaSum += (float)delta;
    _frameCount++;
    _updateTimer += (float)delta;

    if (_updateTimer >= UPDATE_INTERVAL)
    {
        float avgDelta = _deltaSum / _frameCount;
        float simFps = avgDelta > 0 ? 1f / avgDelta : 0f;
        float frameTimeMs = avgDelta * 1000f;

        // Use Godot's *process* time (CPU) — NOT render FPS!
        float cpuTimeMs = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
        ulong memoryMB = OS.GetStaticMemoryUsage() / 1024 / 1024;

        // UPDATE UI — TRUTH ONLY
        _entitiesDataLabel!.Text = $"{_world.EntityCount:N0}";
        _archetypesDataLabel!.Text = $"{_world.ArchetypeCount:N0}";
        _fpsDataLabel!.Text = $"{simFps:F1}";           // ← REAL SIM FPS
        _frameTimeDataLabel!.Text = $"{frameTimeMs:F1}ms"; // ← REAL FRAME
        _memUsageDataLabel!.Text = $"{memoryMB:N0} MB";
        _saveRateDataLabel!.Text = $"{_world.AutoSaveInterval:F3}s";
        _autoSaveCheckBox!.SetPressedNoSignal(_world.AutoSaveEnabled);

        // DEBUG: Print truth
        GD.Print($"[UI] SIM FPS: {simFps:F1} | CPU: {cpuTimeMs:F1}ms | Entities: {_world.EntityCount:N0}");

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
    }
}