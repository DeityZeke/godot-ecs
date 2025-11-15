#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.Settings;
using UltraSim.ECS.Systems;
using Client.ECS.Settings;
using Client.ECS.Systems;
using Client.UI;

namespace Client.ECS.ControlPanel
{
    /// <summary>
    /// Systems panel displaying all ECS systems with their settings.
    /// </summary>
    [ControlPanelSection(defaultOrder: 10, defaultExpanded: true)]
    public partial class SystemsPanel : UIBuilder, IControlPanelSection
    {
        private World _world;
        private SystemManager? _systemManager;
        private Dictionary<SystemSettings, SystemEntryUI> _systemEntries = new();
        private bool _showTimings = false;

        private VBoxContainer? _systemsContainer;
        private CheckBox? _showTimingsCheckBox;
        private Button? _saveAllButton;
        private bool _anyDirty = false;

        public string Title => "Systems";
        public string Id => "systems_panel";
        public bool IsExpanded { get; set; }

        public SystemsPanel(World world)
        {
            _world = world;
            if (world != null)
            {
                _systemManager = world.Systems;
            }
        }

        public Control? CreateHeaderButtons()
        {
            var container = CreateHBox();

            // Save All button (left, only visible when there are unsaved changes)
            _saveAllButton = AddContainerButton(container, "Save All", OnSaveAllPressed);
            _saveAllButton.Visible = false;

            // Show Advanced Timings checkbox (right, always visible)
            _showTimingsCheckBox = AddContainerCheckBox(container, false, OnShowTimingsToggled);
            _showTimingsCheckBox.Text = "Show Advanced Timings";

            return container;
        }

        public Control CreateUI()
        {
            var container = CreateVBox();

            // Systems list
            _systemsContainer = CreateVBox();
            _systemsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            container.AddChild(_systemsContainer);

            return container;
        }

        public void Update(double delta)
        {
            foreach (var entry in _systemEntries.Values)
            {
                entry.UpdateStatus();
                if (_showTimings)
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
            if (_systemsContainer == null)
                return;

            // Clear existing entries
            foreach (var child in _systemsContainer.GetChildren())
            {
                _systemsContainer.RemoveChild(child);
                child.QueueFree();
            }
            _systemEntries.Clear();

            if (_systemManager == null)
            {
                Logging.Log("SystemManager is null! Cannot refresh systems list.", LogSeverity.Error);
                return;
            }

            int totalSystems = 0;
            int systemsWithSettings = 0;

            foreach (var system in _systemManager.GetAllSystems())
            {
                totalSystems++;
                var settings = system.GetSettings();

                if (settings != null)
                {
                    CreateSystemEntry(system);
                    systemsWithSettings++;
                }
            }

            Logging.Log($"Systems Panel: {systemsWithSettings}/{totalSystems} systems with settings");
        }

        private static ISettingUI? GetSettingUI(ISetting setting)
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
            if (setting is ButtonSetting)
                return new ButtonSettingUI(setting);

            // Check for EnumSetting<T> (generic type)
            var settingType = setting.GetType();
            if (settingType.IsGenericType && settingType.GetGenericTypeDefinition().Name.StartsWith("EnumSetting"))
                return new EnumSettingUI(setting);

            return null;
        }

        private void CreateSystemEntry(BaseSystem system)
        {
            if (_systemsContainer == null)
                return;

            var entry = new SystemEntryUI(system, _showTimings, OnSettingChanged, GetSettingUI);
            _systemsContainer.AddChild(entry);

            var settings = system.GetSettings();
            if (settings != null)
                _systemEntries[settings] = entry;
        }

        private void OnSettingChanged(BaseSystem system)
        {
            // Setting changed - check if any system has pending changes
            UpdateSaveAllButton();
        }

        private void OnSaveAllPressed()
        {
            // Save all systems with pending changes (optimized: avoid enumerator allocation)
            int savedCount = 0;
            foreach (var kvp in _systemEntries)
            {
                if (kvp.Value.IsDirty)
                {
                    kvp.Value.ApplySettings();
                    savedCount++;
                }
            }

            UpdateSaveAllButton();
            Logging.Log($"Saved settings for {savedCount} system(s)");
        }

        private void UpdateSaveAllButton()
        {
            if (_saveAllButton == null)
                return;

            // Check if any system has unsaved changes (optimized: manual loop instead of LINQ)
            _anyDirty = false;
            foreach (var kvp in _systemEntries)
            {
                if (kvp.Value.IsDirty)
                {
                    _anyDirty = true;
                    break;
                }
            }
            _saveAllButton.Visible = _anyDirty;
        }

        private void OnShowTimingsToggled(bool enabled)
        {
            _showTimings = enabled;

            // Optimize: use struct enumerator to avoid allocation
            foreach (var kvp in _systemEntries)
            {
                kvp.Value.SetTimingVisible(enabled);
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

        private Button? _expandButton;
        private Label? _nameLabel;
        private CheckBox? _enabledCheckBox;
        private Label? _timingLabel;
        private Label? _statusLabel;
        private VBoxContainer? _settingsContainer;
        private Button? _applyButton;

        private Action<BaseSystem> _onSettingChanged;
        private Func<ISetting, ISettingUI?> _getSettingUI;

        public bool IsDirty => _isDirty;

        public SystemEntryUI(BaseSystem system, bool showTimings, Action<BaseSystem> onSettingChanged, Func<ISetting, ISettingUI?> getSettingUI)
        {
            _system = system;
            _onSettingChanged = onSettingChanged;
            _getSettingUI = getSettingUI;

            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(0, 50);

            BuildUI(showTimings);

            // System entry UI created successfully
        }

        private void BuildUI(bool showTimings)
        {
            // Margin
            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_right", 8);
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_bottom", 8);
            AddChild(margin);

            // Main VBox
            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 8);
            margin.AddChild(vbox);

            // === HEADER ===
            var headerHBox = new HBoxContainer();
            headerHBox.AddThemeConstantOverride("separation", 8);
            vbox.AddChild(headerHBox);

            // Expand button
            _expandButton = new Button
            {
                Text = ">",
                CustomMinimumSize = new Vector2(32, 0)
            };
            _expandButton.Pressed += OnExpandToggled;
            headerHBox.AddChild(_expandButton);

            // System name
            _nameLabel = new Label
            {
                Text = _system.Name,
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerHBox.AddChild(_nameLabel);

            // Timing container (before enabled checkbox)
            var timingContainer = new Control();
            timingContainer.CustomMinimumSize = new Vector2(100, 0);
            headerHBox.AddChild(timingContainer);

            // Timing label
            _timingLabel = new Label
            {
                Text = "0.000ms",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Visible = showTimings
            };
            _timingLabel.SetAnchorsPreset(LayoutPreset.FullRect);
            timingContainer.AddChild(_timingLabel);

            // Enabled checkbox (on far right)
            _enabledCheckBox = new CheckBox
            {
                ButtonPressed = _system.IsEnabled,
                Text = "Enabled",
                FocusMode = FocusModeEnum.None
            };
            _enabledCheckBox.Toggled += OnEnabledToggled;
            headerHBox.AddChild(_enabledCheckBox);

            // === SETTINGS CONTAINER (VBox with Grid inside for 2-column layout) ===
            _settingsContainer = new VBoxContainer();
            _settingsContainer.AddThemeConstantOverride("separation", 8);
            _settingsContainer.Visible = false;
            vbox.AddChild(_settingsContainer);

            // Generate settings UI in a 4-column grid (label, control, label, control)
            if (_system.GetSettings() is SystemSettings settings)
            {
                var settingsGrid = new GridContainer { Columns = 4 };
                settingsGrid.AddThemeConstantOverride("h_separation", 16);
                settingsGrid.AddThemeConstantOverride("v_separation", 10);
                _settingsContainer.AddChild(settingsGrid);

                int settingCount = 0;
                foreach (var setting in settings.GetAllSettings())
                {
                    var settingUI = _getSettingUI(setting);
                    if (settingUI != null)
                    {

                        // Unwrap the HBox children into separate grid cells
                        var hbox = settingUI.Node as HBoxContainer;
                        if (hbox != null && hbox.GetChildCount() >= 1)
                        {
                            // Optimize: cache children in array (no LINQ, reuse for label + controls)
                            int childCount = hbox.GetChildCount();
                            var children = new Node[childCount];
                            int idx = 0;
                            foreach (Node child in hbox.GetChildren())
                            {
                                children[idx++] = child;
                            }

                            // First child is the label
                            var label = children[0] as Control;
                            if (label != null)
                            {
                                hbox.RemoveChild(label);
                                label.CustomMinimumSize = new Vector2(150, 0);
                                label.SizeFlagsHorizontal = Control.SizeFlags.Fill;
                                label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

                                // Left-align labels for consistent column alignment
                                if (label is Label labelControl)
                                {
                                    labelControl.HorizontalAlignment = HorizontalAlignment.Left;
                                    labelControl.VerticalAlignment = VerticalAlignment.Center;
                                }

                                settingsGrid.AddChild(label);
                            }

                            // Remaining children go in a control container
                            var controlContainer = new HBoxContainer
                            {
                                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
                            };
                            controlContainer.AddThemeConstantOverride("separation", 8);

                            for (int i = 1; i < children.Length; i++)
                            {
                                var child = children[i] as Control;
                                if (child != null)
                                {
                                    hbox.RemoveChild(child);
                                    child.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                                    controlContainer.AddChild(child);
                                }
                            }

                            settingsGrid.AddChild(controlContainer);

                            // Bind after adding to grid
                            settingUI.Bind();

                            // Subscribe to setting value changes (deferred to main thread)
                            var capturedSetting = setting;
                            var deferredCall = Callable.From(() => OnSettingChangedInternal(capturedSetting));
                            setting.ValueChanged += (_) => deferredCall.CallDeferred();

                            settingCount++;
                        }
                        else
                        {
                            Logging.Log($"Setting UI is not an HBoxContainer or has insufficient children for {_system.Name}", LogSeverity.Error);
                        }
                    }
                    else
                    {
                        Logging.Log($"No UI wrapper for setting type {setting.GetType().Name} in {_system.Name}", LogSeverity.Error);
                    }
                }

                // If odd number of settings, add two empty spacers to complete the row
                if (settingCount % 2 == 1)
                {
                    settingsGrid.AddChild(new Control());
                    settingsGrid.AddChild(new Control());
                }
            }

            // Apply button container (for right alignment with padding)
            var applyButtonContainer = new HBoxContainer();
            applyButtonContainer.AddThemeConstantOverride("separation", 8);
            _settingsContainer.AddChild(applyButtonContainer);

            // Spacer to push button to the right
            var spacer = new Control();
            spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            applyButtonContainer.AddChild(spacer);

            // Apply button (smaller, right-aligned)
            var applyMargin = new MarginContainer();
            applyMargin.AddThemeConstantOverride("margin_left", 0);
            applyMargin.AddThemeConstantOverride("margin_right", 8);
            applyMargin.AddThemeConstantOverride("margin_top", 0);
            applyMargin.AddThemeConstantOverride("margin_bottom", 0);
            applyButtonContainer.AddChild(applyMargin);

            _applyButton = new Button
            {
                Text = "Apply",
                CustomMinimumSize = new Vector2(80, 0),
                Visible = false
            };
            _applyButton.Pressed += OnApplyPressed;
            applyMargin.AddChild(_applyButton);
        }

        private void OnExpandToggled()
        {
            _isExpanded = !_isExpanded;
            if (_settingsContainer != null)
                _settingsContainer.Visible = _isExpanded;
            if (_expandButton != null)
                _expandButton.Text = _isExpanded ? "v" : ">";
        }

        private void OnEnabledToggled(bool enabled)
        {
            _system.IsEnabled = enabled;
            Logging.Log($"System '{_system.Name}' {(enabled ? "enabled" : "disabled")}");
        }

        private void OnSettingChangedInternal(ISetting setting)
        {
            if (setting is StringSetting stringSetting && !stringSetting.IsEditable)
                return;

            _isDirty = true;
            if (_applyButton != null)
                _applyButton.Visible = true;
            _onSettingChanged?.Invoke(_system);
        }

        private void OnApplyPressed()
        {
            ApplySettings();
        }

        public void ApplySettings()
        {
            _system.SaveSettings();
            _isDirty = false;
            if (_applyButton != null)
                _applyButton.Visible = false;
            Logging.Log($"Applied settings for {_system.Name}");

            // Notify parent that dirty state changed
            _onSettingChanged?.Invoke(_system);
        }

        public void UpdateTiming()
        {
            // Optimize: avoid string interpolation allocation
            if (_timingLabel != null && _timingLabel.Visible)
            {
                double ms = _system.Statistics.AverageUpdateTimeMs;
                _timingLabel.Text = ms.ToString("F3") + "ms";
            }
        }

        public void UpdateStatus()
        {

        }

        public void SetTimingVisible(bool visible)
        {
            if (_timingLabel != null)
                _timingLabel.Visible = visible;
        }
    }
}


