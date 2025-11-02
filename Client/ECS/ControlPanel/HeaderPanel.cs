
using System;
using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// Header panel displaying ECS statistics in an 8-column grid (4 label-data pairs).
    /// </summary>
    [ControlPanelSection(defaultOrder: 0, defaultExpanded: true)]
    public class HeaderPanel : IControlPanelSection
    {
        private World _world;

        // Stats labels (data values)
        private Label _entitiesDataLabel;
        private Label _archetypesDataLabel;
        private Label _lastSaveDataLabel;
        private Label _fpsDataLabel;
        private Label _frameTimeDataLabel;
        private Label _nextSaveDataLabel;
        private Label _memUsageDataLabel;
        private Label _saveRateDataLabel;
        private CheckBox _autoSaveCheckBox;

        public string Title => "Statistics";
        public string Id => "header_stats";
        public bool IsExpanded { get; set; }

        public HeaderPanel(World world)
        {
            _world = world;
        }

        public Control CreateUI()
        {
            // Use GridContainer: 8 columns = 4 pairs of (label, data)
            var grid = new GridContainer();
            grid.Columns = 8;
            grid.AddThemeConstantOverride("h_separation", 16);
            grid.AddThemeConstantOverride("v_separation", 8);

            // === ROW 1 ===
            // Pair 1: Entities
            grid.AddChild(CreateLabelCell("Entities:"));
            _entitiesDataLabel = CreateDataCell("0");
            grid.AddChild(_entitiesDataLabel);

            // Pair 2: Archetypes
            grid.AddChild(CreateLabelCell("Archetypes:"));
            _archetypesDataLabel = CreateDataCell("0");
            grid.AddChild(_archetypesDataLabel);

            // Pair 3: Empty
            grid.AddChild(CreateEmptyCell());
            grid.AddChild(CreateEmptyCell());

            // Pair 4: Last Save
            grid.AddChild(CreateLabelCell("Last Save:"));
            _lastSaveDataLabel = CreateDataCell("Never");
            grid.AddChild(_lastSaveDataLabel);

            // === ROW 2 ===
            // Pair 1: FPS
            grid.AddChild(CreateLabelCell("FPS:"));
            _fpsDataLabel = CreateDataCell("60");
            grid.AddChild(_fpsDataLabel);

            // Pair 2: Frame Time
            grid.AddChild(CreateLabelCell("Frame Time:"));
            _frameTimeDataLabel = CreateDataCell("0.00ms");
            grid.AddChild(_frameTimeDataLabel);

            // Pair 3: Empty
            grid.AddChild(CreateEmptyCell());
            grid.AddChild(CreateEmptyCell());

            // Pair 4: Next Save
            grid.AddChild(CreateLabelCell("Next Save:"));
            _nextSaveDataLabel = CreateDataCell("N/A");
            grid.AddChild(_nextSaveDataLabel);

            // === ROW 3 ===
            // Pair 1: Mem Usage
            grid.AddChild(CreateLabelCell("Mem Usage:"));
            _memUsageDataLabel = CreateDataCell("0 MB");
            grid.AddChild(_memUsageDataLabel);

            // Pair 2: Empty
            grid.AddChild(CreateEmptyCell());
            grid.AddChild(CreateEmptyCell());

            // Pair 3: Empty
            grid.AddChild(CreateEmptyCell());
            grid.AddChild(CreateEmptyCell());

            // Pair 4: Auto Save (checkbox + rate)
            var autoSaveLabel = CreateLabelCell("Auto Save:");
            grid.AddChild(autoSaveLabel);

            var autoSaveContainer = new HBoxContainer();
            autoSaveContainer.AddThemeConstantOverride("separation", 8);

            _autoSaveCheckBox = new CheckBox();
            _autoSaveCheckBox.FocusMode = Control.FocusModeEnum.None;
            _autoSaveCheckBox.Toggled += OnAutoSaveToggled;
            autoSaveContainer.AddChild(_autoSaveCheckBox);

            var rateLabel = new Label();
            rateLabel.Text = "Rate:";
            rateLabel.VerticalAlignment = VerticalAlignment.Center;
            autoSaveContainer.AddChild(rateLabel);

            _saveRateDataLabel = CreateDataCell("60.000s");
            _saveRateDataLabel.HorizontalAlignment = HorizontalAlignment.Left;
            autoSaveContainer.AddChild(_saveRateDataLabel);

            grid.AddChild(autoSaveContainer);

            return grid;
        }

        private Label CreateLabelCell(string text)
        {
            var label = new Label();
            label.Text = text;
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            return label;
        }

        private Label CreateDataCell(string text)
        {
            var label = new Label();
            label.Text = text;
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            return label;
        }

        private Control CreateEmptyCell()
        {
            var spacer = new Control();
            spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            return spacer;
        }

        public void Update(double delta)
        {
            if (_world == null)
                return;

            // Update ECS stats
            int entityCount = _world.EntityCount;
            int archetypeCount = _world.ArchetypeCount;
            _entitiesDataLabel.Text = $"{entityCount:N0}";
            _archetypesDataLabel.Text = $"{archetypeCount:N0}";

            // Update performance stats
            float fps = (float)Engine.GetFramesPerSecond();
            float frameTime = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
            long memoryMB = (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1024 / 1024;

            _fpsDataLabel.Text = $"{fps:F0}";
            _frameTimeDataLabel.Text = $"{frameTime:F2}ms";
            _memUsageDataLabel.Text = $"{memoryMB:N0} MB";

            // Update save-related stats
            _saveRateDataLabel.Text = $"{_world.AutoSaveInterval:F3}s";
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
