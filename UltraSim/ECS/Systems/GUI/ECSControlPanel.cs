using Godot;
using System;
using System.Collections.Generic;
using UltraSim.ECS;
using UltraSim.ECS.Components;
using UltraSim.Scripts.ECS.Systems.Settings;

namespace UltraSim.ECS.GUI
{
    /// <summary>
    /// Fully code-generated ECS Control Panel - no .tscn files needed!
    /// Just attach this script to a Control or CanvasLayer node.
    /// </summary>
    public partial class ECSControlPanel : Control
    {
        #region Fields

        private World _world;
        private SystemManager _systemManager;
        private Dictionary<BaseSystem, SystemEntryUI> _systemEntries = new();
        private bool _showTimings = false;

        private PanelContainer _mainPanel;  // Store reference to main panel

        #endregion

        #region UI References

        private Label _titleLabel;
        private Label _statsLabel;
        private CheckBox _showTimingsCheckBox;
        private Button _closeButton;
        private VBoxContainer _systemsContainer;
        private ScrollContainer _scrollContainer;

        #endregion

        #region Initialization

        public override void _Ready()
        {
            // Force to top layer
            ZIndex = 100;

            // CRITICAL: Set anchors and size FIRST, before building UI
            SetAnchorsPreset(LayoutPreset.FullRect);
            OffsetLeft = 0;
            OffsetTop = 0;
            OffsetRight = 0;
            OffsetBottom = 0;

            // Block input to 3D scene when visible
            MouseFilter = MouseFilterEnum.Stop;

            // Ensure this control expands to fill available space
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;

            // Build the entire UI in code
            BuildUI();

            // Start hidden - press F12 to show
            Visible = false;

            GD.Print("ECSControlPanel ready! Press F12 to toggle.");

            // Force initial layout calculation
            CallDeferred(MethodName.UpdateMinimumSize);
        }

        private void BuildUI()
        {
            // Calculate padding
            Vector2 viewportSize = GetTree().Root.Size;
            int padding = (int)Mathf.Clamp(Mathf.Min(viewportSize.X, viewportSize.Y) * 0.05f, 50f, 150f);

            GD.Print($"â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
            GD.Print($"â•‘ ECSControlPanel BuildUI                      â•‘");
            GD.Print($"â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
            GD.Print($"â•‘ Viewport: {viewportSize.X}x{viewportSize.Y}");
            GD.Print($"â•‘ ECSControlPanel Size: {Size}");
            GD.Print($"â•‘ Padding: {padding}px");
            GD.Print($"â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");

            // ROOT MARGIN - creates the padding border around the panel
            var rootMargin = new MarginContainer();
            rootMargin.Name = "RootMargin";
            rootMargin.SetAnchorsPreset(LayoutPreset.FullRect);
            rootMargin.AddThemeConstantOverride("margin_left", padding);
            rootMargin.AddThemeConstantOverride("margin_right", padding);
            rootMargin.AddThemeConstantOverride("margin_top", padding);
            rootMargin.AddThemeConstantOverride("margin_bottom", padding);
            AddChild(rootMargin);

            // PANEL - the visible background
            _mainPanel = new PanelContainer();
            _mainPanel.Name = "MainPanel";
            rootMargin.AddChild(_mainPanel);

            // INNER MARGIN - padding inside the panel
            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", 16);
            margin.AddThemeConstantOverride("margin_right", 16);
            margin.AddThemeConstantOverride("margin_top", 16);
            margin.AddThemeConstantOverride("margin_bottom", 16);
            _mainPanel.AddChild(margin);

            // Main vertical layout
            var mainVBox = new VBoxContainer();
            mainVBox.SizeFlagsVertical = SizeFlags.ExpandFill; // CRITICAL: Must expand to give ScrollContainer space!
            mainVBox.AddThemeConstantOverride("separation", 12);
            margin.AddChild(mainVBox);

            // === HEADER ===
            var headerPanel = new PanelContainer();
            mainVBox.AddChild(headerPanel);

            var headerMargin = new MarginContainer();
            headerMargin.AddThemeConstantOverride("margin_left", 12);
            headerMargin.AddThemeConstantOverride("margin_right", 12);
            headerMargin.AddThemeConstantOverride("margin_top", 12);
            headerMargin.AddThemeConstantOverride("margin_bottom", 12);
            headerPanel.AddChild(headerMargin);

            var headerVBox = new VBoxContainer();
            headerVBox.AddThemeConstantOverride("separation", 8);
            headerMargin.AddChild(headerVBox);

            // Title
            _titleLabel = new Label();
            _titleLabel.Text = "ECS Control Panel";
            _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _titleLabel.AddThemeFontSizeOverride("font_size", 24);
            headerVBox.AddChild(_titleLabel);

            // Stats
            _statsLabel = new Label();
            _statsLabel.Text = "Entities: 0 | Archetypes: 0 | FPS: 60 | Frame: 0.0ms | Memory: 0 MB";
            _statsLabel.AutowrapMode = TextServer.AutowrapMode.Word;
            headerVBox.AddChild(_statsLabel);

            // Options row
            var optionsHBox = new HBoxContainer();
            optionsHBox.AddThemeConstantOverride("separation", 16);
            headerVBox.AddChild(optionsHBox);

            _showTimingsCheckBox = new CheckBox();
            _showTimingsCheckBox.Text = "Show Advanced Timings";
            _showTimingsCheckBox.ButtonPressed = false;
            _showTimingsCheckBox.Toggled += OnShowTimingsToggled;
            optionsHBox.AddChild(_showTimingsCheckBox);

            // Spacer
            var spacer = new Control();
            spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            optionsHBox.AddChild(spacer);

            _closeButton = new Button();
            _closeButton.Text = "Close (F12)";
            _closeButton.Pressed += OnClosePressed;
            optionsHBox.AddChild(_closeButton);

            // Separator
            var separator = new HSeparator();
            mainVBox.AddChild(separator);

            // === SYSTEMS SCROLL AREA ===
            _scrollContainer = new ScrollContainer();
            _scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
            _scrollContainer.CustomMinimumSize = new Vector2(0, 200); // Ensure minimum height
            _scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
            _scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
            mainVBox.AddChild(_scrollContainer);

            _systemsContainer = new VBoxContainer();
            _systemsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _systemsContainer.AddThemeConstantOverride("separation", 8);
            _scrollContainer.AddChild(_systemsContainer);

            GD.Print("UI built successfully!");
        }

        /// <summary>
        /// Initialize the control panel with a world reference.
        /// Call this after the world is set up.
        /// </summary>
        public void Initialize(World world)
        {
            _world = world;
            _systemManager = world.Systems;

            RefreshSystemsList();

            GD.Print($"ECSControlPanel initialized with {_systemEntries.Count} systems (with settings)");
        }

        #endregion

        #region System List Management

        public void RefreshSystemsList()
        {
            // Clear existing entries - remove immediately to prevent duplicates
            foreach (var child in _systemsContainer.GetChildren())
            {
                _systemsContainer.RemoveChild(child);
                child.QueueFree();
            }
            _systemEntries.Clear();

            if (_systemManager == null)
            {
                GD.PrintErr("SystemManager is null! Cannot refresh systems list.");
                return;
            }

            GD.Print("\n=== Refreshing Systems List ===");

            // Create entry for each system with settings
            int totalSystems = 0;
            int systemsWithSettings = 0;

            foreach (var system in _systemManager.GetAllSystems())
            {
                totalSystems++;
                var settings = system.GetSettings();

                if (settings != null)
                {
                    GD.Print($"  Ã¢Å“â€œ {system.Name} has settings");
                    CreateSystemEntry(system);
                    systemsWithSettings++;
                }
                else
                {
                    GD.Print($"  Ã¢Å“â€” {system.Name} has NO settings");
                }
            }

            GD.Print($"Total: {totalSystems} systems, {systemsWithSettings} with settings");
            GD.Print($"Added {_systemEntries.Count} entries to UI");

            // DEBUG: Check container after adding
            GD.Print($"SystemsContainer children count: {_systemsContainer.GetChildCount()}");
            GD.Print($"SystemsContainer size: {_systemsContainer.Size}");
            GD.Print($"ScrollContainer size: {_scrollContainer.Size}\n");
        }

        private void CreateSystemEntry(BaseSystem system)
        {
            var entry = new SystemEntryUI(system, _showTimings, OnSettingChanged);
            _systemsContainer.AddChild(entry);
            _systemEntries[system] = entry;

            GD.Print($"  Ã¢â€ â€™ Added SystemEntryUI to tree, total children: {_systemsContainer.GetChildCount()}");
        }

        private void OnSettingChanged(BaseSystem system)
        {
            // Setting changed - the SystemEntryUI handles showing its own Apply button
            GD.Print($"Setting changed in {system.Name}");
        }

        #endregion

        #region Update Loop

        public override void _Process(double delta)
        {
            if (!Visible)
                return;

            UpdateStats();

            if (_showTimings)
            {
                UpdateTimings();
            }
        }

        private void UpdateStats()
        {
            if (_world == null)
                return;

            int entityCount = _world.EntityCount;
            int archetypeCount = _world.ArchetypeCount;

            float fps = (float)Engine.GetFramesPerSecond();
            float frameTime = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
            long memoryMB = (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1024 / 1024;

            _statsLabel.Text = $"Entities: {entityCount} | Archetypes: {archetypeCount} | " +
                              $"FPS: {fps:F0} | Frame: {frameTime:F2}ms | Memory: {memoryMB} MB";
        }

        private void UpdateTimings()
        {
            // TODO: Get actual timing data from SystemManager
            foreach (var kvp in _systemEntries)
            {
                float timing = 0.0f; // Replace with actual timing
                kvp.Value.UpdateTiming(timing);
            }
        }

        #endregion

        #region Event Handlers

        private void OnShowTimingsToggled(bool enabled)
        {
            _showTimings = enabled;

            foreach (var entry in _systemEntries.Values)
            {
                entry.SetTimingVisible(enabled);
            }
        }

        private void OnClosePressed()
        {
            Hide();
        }

        #endregion

        #region Public API

        public void Toggle()
        {
            Visible = !Visible;

            if (Visible)
            {
                // Force layout update
                CallDeferred(MethodName.ForceLayoutUpdate);
                RefreshSystemsList();
            }
        }

        public new void Show()
        {
            Visible = true;

            // Force layout update
            CallDeferred(MethodName.ForceLayoutUpdate);
            RefreshSystemsList();
        }

        private void ForceLayoutUpdate()
        {
            GD.Print($"\nâ•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
            GD.Print($"â•‘ ForceLayoutUpdate                            â•‘");
            GD.Print($"â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
            GD.Print($"â•‘ ECSControlPanel Size: {Size}");

            if (_mainPanel != null)
            {
                GD.Print($"â•‘ MainPanel Size: {_mainPanel.Size}");
                GD.Print($"â•‘ MainPanel Position: {_mainPanel.Position}");

                // Just reset sizes to force recalculation
                _mainPanel.ResetSize();
                _scrollContainer?.ResetSize();
                _systemsContainer?.ResetSize();

                GD.Print($"â•‘ After Reset - MainPanel: {_mainPanel.Size}");
            }
            else
            {
                GD.Print($"â•‘ ERROR: MainPanel is null!");
            }

            GD.Print($"â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");

            // Queue debug output for next frame
            CallDeferred(MethodName.DebugSizesAfterShow);
        }

        private void DebugSizesAfterShow()
        {
            GD.Print($"\nâ•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
            GD.Print($"â•‘ Final Layout (After Show)                    â•‘");
            GD.Print($"â• â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•£");
            GD.Print($"â•‘ ECSControlPanel: {Size}");

            // Get the root margin
            var rootMargin = GetChildOrNull<MarginContainer>(0);
            if (rootMargin != null)
            {
                GD.Print($"â•‘ RootMargin: {rootMargin.Size}");
            }

            if (_mainPanel != null)
            {
                GD.Print($"â•‘ MainPanel: {_mainPanel.Size}");
                GD.Print($"â•‘ MainPanel Position: {_mainPanel.Position}");
            }

            if (_scrollContainer != null)
            {
                GD.Print($"â•‘ ScrollContainer: {_scrollContainer.Size}");
            }

            if (_systemsContainer != null)
            {
                GD.Print($"â•‘ SystemsContainer: {_systemsContainer.Size}");
            }

            GD.Print($"â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•\n");
        }

        public new void Hide()
        {
            Visible = false;
        }

        #endregion

        #region Input Handling

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                if (keyEvent.Keycode == Key.F12)
                {
                    Toggle();
                    GetViewport().SetInputAsHandled();
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// UI for a single system entry - completely code-generated
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

        public SystemEntryUI(BaseSystem system, bool showTimings, Action<BaseSystem> onSettingChanged)
        {
            _system = system;
            _onSettingChanged = onSettingChanged;

            // CRITICAL: Set size flags so it shows up!
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(0, 50); // Minimum height so it's visible

            BuildUI(showTimings);

            GD.Print($"[SystemEntryUI] Created for {system.Name}, CustomMinSize: {CustomMinimumSize}");
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

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 8);
            margin.AddChild(vbox);

            // === HEADER ===
            var headerHBox = new HBoxContainer();
            headerHBox.AddThemeConstantOverride("separation", 8);
            vbox.AddChild(headerHBox);

            // Expand button (using simple ASCII for compatibility)
            _expandButton = new Button();
            _expandButton.Text = ">";
            _expandButton.CustomMinimumSize = new Vector2(32, 0);
            _expandButton.Pressed += OnExpandToggled;
            headerHBox.AddChild(_expandButton);

            // System name
            _nameLabel = new Label();
            _nameLabel.Text = _system.Name;
            _nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _nameLabel.VerticalAlignment = VerticalAlignment.Center;
            headerHBox.AddChild(_nameLabel);

            // Enabled checkbox
            _enabledCheckBox = new CheckBox();
            _enabledCheckBox.Text = "Enabled";
            _enabledCheckBox.ButtonPressed = _system.IsEnabled;
            _enabledCheckBox.Toggled += OnEnabledToggled;
            headerHBox.AddChild(_enabledCheckBox);

            /*
            // Timing label
            _timingLabel = new Label();
            _timingLabel.Text = "0.000ms";
            _timingLabel.CustomMinimumSize = new Vector2(80, 0);
            _timingLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _timingLabel.Visible = showTimings;
            headerHBox.AddChild(_timingLabel);
            */
            // Timing container - ALWAYS reserves 100px of space
            var timingContainer = new Control();
            timingContainer.CustomMinimumSize = new Vector2(100, 0); // For "999.999ms"
            headerHBox.AddChild(timingContainer);

            // Timing label (inside container)
            _timingLabel = new Label();
            _timingLabel.Text = "0.000ms";
            _timingLabel.SetAnchorsPreset(LayoutPreset.FullRect);
            _timingLabel.HorizontalAlignment = HorizontalAlignment.Right;
            _timingLabel.VerticalAlignment = VerticalAlignment.Center;
            _timingLabel.Visible = showTimings;  // â† Label can hide, but space is reserved!
            timingContainer.AddChild(_timingLabel);

            // === SETTINGS CONTAINER ===
            _settingsContainer = new VBoxContainer();
            _settingsContainer.AddThemeConstantOverride("separation", 4);
            _settingsContainer.Visible = false; // Collapsed initially
            vbox.AddChild(_settingsContainer);

            // Generate settings UI
            if (_system.GetSettings() is BaseSettings settings)
            {
                foreach (var setting in settings.GetAllSettings())
                {
                    var control = setting.CreateControl(OnSettingChangedInternal);
                    _settingsContainer.AddChild(control);
                }

                GD.Print($"[SystemEntryUI] Added {settings.GetAllSettings().Count} setting controls for {_system.Name}");
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

            GD.Print($"[SystemEntryUI] {_system.Name} expanded: {_isExpanded}");
        }

        private void OnEnabledToggled(bool enabled)
        {
            _system.IsEnabled = enabled;
            GD.Print($"System '{_system.Name}' {(enabled ? "enabled" : "disabled")}");
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
            GD.Print($"Ã¢Å“â€œ Applied settings for {_system.Name}");
        }

        public void UpdateTiming(float ms)
        {
            if (_timingLabel.Visible)
            {
                _timingLabel.Text = $"{ms:F3}ms";
            }
        }

        public void SetTimingVisible(bool visible)
        {
            _timingLabel.Visible = visible;
        }
    }
}