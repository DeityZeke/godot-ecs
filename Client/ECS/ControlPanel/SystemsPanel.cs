
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Godot;

using UltraSim.ECS.Systems;
using UltraSim.ECS.Settings;

namespace UltraSim.ECS
{
    /// <summary>
    /// Systems panel displaying all ECS systems with their settings.
    /// </summary>
    [ControlPanelSection(defaultOrder: 10, defaultExpanded: true)]
    public class SystemsPanel : IControlPanelSection
    {
        private World _world;
        private SystemManager _systemManager;
        private Dictionary<SettingsManager, SystemEntryUI> _systemEntries = new();
        private bool _showTimings = false;

        private VBoxContainer _systemsContainer;
        private CheckBox _showTimingsCheckBox;

        public string Title => "Systems";
        public string Id => "systems_panel";
        public bool IsExpanded { get; set; }

        public SystemsPanel(World world)
        {
            _world = world;
            _systemManager = world.Systems;
        }

        public Control CreateUI()
        {
            var container = new VBoxContainer();
            container.AddThemeConstantOverride("separation", 8);

            // Options row
            var optionsHBox = new HBoxContainer();
            optionsHBox.AddThemeConstantOverride("separation", 16);
            container.AddChild(optionsHBox);

            _showTimingsCheckBox = new CheckBox();
            _showTimingsCheckBox.Text = "Show Advanced Timings";
            _showTimingsCheckBox.ButtonPressed = false;
            _showTimingsCheckBox.Toggled += OnShowTimingsToggled;
            optionsHBox.AddChild(_showTimingsCheckBox);

            // Separator
            var separator = new HSeparator();
            container.AddChild(separator);

            // Systems list
            _systemsContainer = new VBoxContainer();
            _systemsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _systemsContainer.AddThemeConstantOverride("separation", 8);
            container.AddChild(_systemsContainer);

            return container;
        }

        public void Update(double delta)
        {
            if (_showTimings)
            {
                foreach (var entry in _systemEntries.Values)
                {
                    entry.UpdateTiming();
                }
            }
        }

        public void OnShow()
        {
            RefreshSystemsList();
        }

        public void OnHide()
        {
            // Nothing to do on hide
        }

        private void RefreshSystemsList()
        {
            // Clear existing entries
            foreach (var child in _systemsContainer.GetChildren())
            {
                _systemsContainer.RemoveChild(child);
                child.QueueFree();
            }
            _systemEntries.Clear();

            if (_systemManager == null)
            {
                Logging.Logger.Log("SystemManager is null! Cannot refresh systems list.", Logging.LogSeverity.Error);
                return;
            }

            Logging.Logger.Log("\n=== Refreshing Systems List ===", Logging.LogSeverity.Debug);

            int totalSystems = 0;
            int systemsWithSettings = 0;

            foreach (var system in _systemManager.GetAllSystems())
            {
                totalSystems++;
                var settings = system.GetSettings();

                if (settings != null)
                {
                    Logging.Logger.Log($"  - {system.Name} has settings");
                    CreateSystemEntry(system);
                    systemsWithSettings++;
                }
                else
                {
                    Logging.Logger.Log($"  - {system.Name} has NO settings");
                }
            }

            Logging.Logger.Log($"Total: {totalSystems} systems, {systemsWithSettings} with settings");
            Logging.Logger.Log($"Added {_systemEntries.Count} entries to UI");
        }

        private static ISettingUI GetSettingUI(ISetting setting)
        {
            // Check for specific setting types
            if (setting is BoolSetting)
                return new BoolSettingUI(setting);
            if (setting is IntSetting)
                return new IntSettingUI(setting);
            if (setting is FloatSetting)
                return new FloatSettingUI(setting);
            if (setting is StringSetting)
                return new StringSettingUI(setting);

            // Check for EnumSetting<T> (generic type)
            var settingType = setting.GetType();
            if (settingType.IsGenericType && settingType.GetGenericTypeDefinition().Name.StartsWith("EnumSetting"))
                return new EnumSettingUI(setting);

            return null;
        }

        private void CreateSystemEntry(BaseSystem system)
        {
            var entry = new SystemEntryUI(system, _showTimings, OnSettingChanged, GetSettingUI);
            _systemsContainer.AddChild(entry);
            _systemEntries[system.GetSettings()] = entry;

            Logging.Logger.Log($"  - Added SystemEntryUI to tree, total children: {_systemsContainer.GetChildCount()}");
        }

        private void OnSettingChanged(BaseSystem system)
        {
            // Setting changed - the SystemEntryUI handles showing its own Apply button
            Logging.Logger.Log($"Setting changed in {system.Name}");
        }

        private void OnShowTimingsToggled(bool enabled)
        {
            _showTimings = enabled;

            foreach (var entry in _systemEntries.Values)
            {
                entry.SetTimingVisible(enabled);
            }
        }
    }

    /// <summary>
    /// UI for a single system entry.
    /// </summary>
    internal partial class SystemEntryUI : PanelContainer
    {
        private BaseSystem _system;
        private bool _isExpanded = false;
        private bool _isDirty = false;

        private Button _expandButton;
        private Label _nameLabel;
        private CheckBox _enabledCheckBox;
        private Label _timingLabel;
        private VBoxContainer _settingsContainer;
        private Button _applyButton;

        private Action<BaseSystem> _onSettingChanged;
        private Func<ISetting, ISettingUI> _getSettingUI;

        public SystemEntryUI(BaseSystem system, bool showTimings, Action<BaseSystem> onSettingChanged, Func<ISetting, ISettingUI> getSettingUI)
        {
            _system = system;
            _onSettingChanged = onSettingChanged;
            _getSettingUI = getSettingUI;

            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(0, 50);

            BuildUI(showTimings);

            Logging.Logger.Log($"[SystemEntryUI] Created for {system.Name}, CustomMinSize: {CustomMinimumSize}");
        }

        private void BuildUI(bool showTimings)
        {
            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_right", 8);
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_bottom", 8);
            AddChild(margin);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 8);
            margin.AddChild(vbox);

            // === HEADER ===
            var headerHBox = new HBoxContainer();
            headerHBox.AddThemeConstantOverride("separation", 8);
            vbox.AddChild(headerHBox);

            // Expand button
            _expandButton = new Button();
            _expandButton.Text = ">";
            _expandButton.CustomMinimumSize = new Vector2(32, 0);
            _expandButton.Pressed += OnExpandToggled;
            headerHBox.AddChild(_expandButton);

            // System name
            _nameLabel = new Label();
            _nameLabel.Text = _system.Name;
            _nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _nameLabel.VerticalAlignment = VerticalAlignment.Center;
            headerHBox.AddChild(_nameLabel);

            // Enabled checkbox
            _enabledCheckBox = new CheckBox();
            _enabledCheckBox.Text = "Enabled";
            _enabledCheckBox.ButtonPressed = _system.IsEnabled;
            _enabledCheckBox.Toggled += OnEnabledToggled;
            headerHBox.AddChild(_enabledCheckBox);

            // Timing container
            var timingContainer = new Control();
            timingContainer.CustomMinimumSize = new Vector2(100, 0);
            headerHBox.AddChild(timingContainer);

            // Timing label
            _timingLabel = new Label();
            _timingLabel.Text = "0.000ms";
            _timingLabel.SetAnchorsPreset(LayoutPreset.FullRect);
            _timingLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _timingLabel.VerticalAlignment = VerticalAlignment.Center;
            _timingLabel.Visible = showTimings;
            timingContainer.AddChild(_timingLabel);

            // === SETTINGS CONTAINER ===
            _settingsContainer = new VBoxContainer();
            _settingsContainer.AddThemeConstantOverride("separation", 4);
            _settingsContainer.Visible = false;
            vbox.AddChild(_settingsContainer);

            // Generate settings UI
            if (_system.GetSettings() is SettingsManager settings)
            {
                int settingCount = 0;
                foreach (var setting in settings.GetAllSettings())
                {
                    Logging.Logger.Log($"[SystemEntryUI]   - Creating UI for setting '{setting.Name}'");

                    var settingUI = _getSettingUI(setting);
                    if (settingUI != null)
                    {
                        Logging.Logger.Log($"[SystemEntryUI]     - Got UI wrapper, adding node to container");
                        _settingsContainer.AddChild(settingUI.Node);
                        settingUI.Bind();
                        settingCount++;
                    }
                    else
                    {
                        Logging.Logger.Log($"[SystemEntryUI]     - WARNING: No UI wrapper for setting type {setting.GetType().Name}", Logging.LogSeverity.Warning);
                    }
                }

                Logging.Logger.Log($"[SystemEntryUI] Added {settingCount} setting controls for {_system.Name}");
            }

            // Apply button
            _applyButton = new Button();
            _applyButton.Text = "Apply Changes";
            _applyButton.Visible = false;
            _applyButton.Pressed += OnApplyPressed;
            _settingsContainer.AddChild(_applyButton);
        }

        private void OnExpandToggled()
        {
            _isExpanded = !_isExpanded;
            _settingsContainer.Visible = _isExpanded;
            _expandButton.Text = _isExpanded ? "v" : ">";

            Logging.Logger.Log($"[SystemEntryUI] {_system.Name} expanded: {_isExpanded}");
        }

        private void OnEnabledToggled(bool enabled)
        {
            _system.IsEnabled = enabled;
            Logging.Logger.Log($"System '{_system.Name}' {(enabled ? "enabled" : "disabled")}");
        }

        private void OnSettingChangedInternal(ISetting setting)
        {
            _isDirty = true;
            _applyButton.Visible = true;
            _onSettingChanged?.Invoke(_system);
        }

        private void OnApplyPressed()
        {
            _system.SaveSettings();
            _isDirty = false;
            _applyButton.Visible = false;
            Logging.Logger.Log($"Applied settings for {_system.Name}");
        }

        public void UpdateTiming()
        {
            if (_timingLabel.Visible)
            {
                double ms = _system.Statistics.AverageUpdateTimeMs;
                _timingLabel.Text = $"{ms:F3}ms";
            }
        }

        public void SetTimingVisible(bool visible)
        {
            _timingLabel.Visible = visible;
        }
    }
}
