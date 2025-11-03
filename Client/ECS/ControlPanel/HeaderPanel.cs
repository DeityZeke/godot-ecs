
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
            // Pair 1: Entities
            AddContainerLabel(grid, "Entities:");
            _entitiesDataLabel = AddContainerLabel(grid, "0", hAlign: HorizontalAlignment.Right);

            // Pair 2: Archetypes
            AddContainerLabel(grid, "Archetypes:");
            _archetypesDataLabel = AddContainerLabel(grid, "0", hAlign: HorizontalAlignment.Right);

            // Pair 3: Empty
            grid.AddChild(new Control());
            grid.AddChild(new Control());

            // Pair 4: Last Save
            AddContainerLabel(grid, "Last Save:");
            _lastSaveDataLabel = AddContainerLabel(grid, "Never", hAlign: HorizontalAlignment.Right);

            // === ROW 2 ===
            // Pair 1: FPS
            AddContainerLabel(grid, "FPS:");
            _fpsDataLabel = AddContainerLabel(grid, "60", hAlign: HorizontalAlignment.Right);

            // Pair 2: Frame Time
            AddContainerLabel(grid, "Frame Time:");
            _frameTimeDataLabel = AddContainerLabel(grid, "0.00ms", hAlign: HorizontalAlignment.Right);

            // Pair 3: Empty
            grid.AddChild(new Control());
            grid.AddChild(new Control());

            // Pair 4: Next Save
            AddContainerLabel(grid, "Next Save:");
            _nextSaveDataLabel = AddContainerLabel(grid, "N/A", hAlign: HorizontalAlignment.Right);

            // === ROW 3 ===
            // Pair 1: Mem Usage
            AddContainerLabel(grid, "Mem Usage:");
            _memUsageDataLabel = AddContainerLabel(grid, "0 MB", hAlign: HorizontalAlignment.Right);

            // Pair 2: Empty
            grid.AddChild(new Control());
            grid.AddChild(new Control());

            // Pair 3: Empty
            grid.AddChild(new Control());
            grid.AddChild(new Control());

            // Pair 4: Auto Save (checkbox + rate)
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
            if (_world == null)
                return;

            // Update ECS stats
            int entityCount = _world.EntityCount;
            int archetypeCount = _world.ArchetypeCount;
            if (_entitiesDataLabel != null)
                _entitiesDataLabel.Text = $"{entityCount:N0}";
            if (_archetypesDataLabel != null)
                _archetypesDataLabel.Text = $"{archetypeCount:N0}";

            // Update performance stats
            float fps = (float)Engine.GetFramesPerSecond();
            float frameTime = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
            long memoryMB = (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1024 / 1024;

            if (_fpsDataLabel != null)
                _fpsDataLabel.Text = $"{fps:F0}";
            if (_frameTimeDataLabel != null)
                _frameTimeDataLabel.Text = $"{frameTime:F2}ms";
            if (_memUsageDataLabel != null)
                _memUsageDataLabel.Text = $"{memoryMB:N0} MB";

            // Update save-related stats
            if (_saveRateDataLabel != null)
                _saveRateDataLabel.Text = $"{_world.AutoSaveInterval:F3}s";
            if (_autoSaveCheckBox != null)
                _autoSaveCheckBox.SetPressedNoSignal(_world.AutoSaveEnabled);
        }

        public void OnShow()
        {
            // Nothing to do on show
        }

        public void OnHide()
        {
            // Nothing to do on hide
        }

        private void OnAutoSaveToggled(bool enabled)
        {
            if (enabled)
            {
                _world.EnableAutoSave(_world.AutoSaveInterval);
            }
            else
            {
                _world.DisableAutoSave();
            }

            Logging.Logger.Log($"Auto-save {(enabled ? "enabled" : "disabled")}");
        }
    }
}