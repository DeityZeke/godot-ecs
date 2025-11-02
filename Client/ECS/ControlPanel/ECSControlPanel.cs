
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// Modular ECS Control Panel - lightweight container for panel sections.
    /// Press F12 to toggle visibility.
    /// </summary>
    public partial class ECSControlPanel : Control
    {
        private World _world;
        private PanelConfiguration _config;
        private List<PanelSectionUI> _panels = new();

        // UI References
        private PanelContainer _mainPanel;
        private VBoxContainer _panelsContainer;

        #region Initialization

        public void Initialize(World world)
        {
            _world = world;
            Logging.Logger.Log("ECSControlPanel initialized");
        }

        public override void _Ready()
        {
            // Force to top layer
            ZIndex = 100;

            // Set anchors and size
            SetAnchorsPreset(LayoutPreset.FullRect);
            OffsetLeft = 0;
            OffsetTop = 0;
            OffsetRight = 0;
            OffsetBottom = 0;

            // Set minimum size
            CustomMinimumSize = new Vector2(800, 600);

            // Block input to 3D scene when visible
            MouseFilter = MouseFilterEnum.Stop;

            // Ensure this control expands to fill available space
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;

            // Build UI
            BuildUI();

            // Load panel configuration and create panels
            LoadAndCreatePanels();

            // Start hidden - press F12 to show
            Visible = false;

            Logging.Logger.Log("ECSControlPanel ready! Press F12 to toggle.");

            // Force initial layout calculation
            CallDeferred(MethodName.UpdateMinimumSize);
        }

        private void BuildUI()
        {
            // Calculate padding
            Vector2 viewportSize = GetTree().Root.Size;
            int padding = (int)Mathf.Clamp(Mathf.Min(viewportSize.X, viewportSize.Y) * 0.05f, 50f, 150f);

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
            mainVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
            mainVBox.AddThemeConstantOverride("separation", 0);
            margin.AddChild(mainVBox);

            // === STATIC HEADER ===
            BuildStaticHeader(mainVBox);

            // === SEPARATOR UNDER HEADER ===
            var topSeparator = new HSeparator();
            mainVBox.AddChild(topSeparator);

            // === PANELS CONTAINER ===
            var scrollContainer = new ScrollContainer();
            scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
            scrollContainer.CustomMinimumSize = new Vector2(0, 200);
            scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
            scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
            mainVBox.AddChild(scrollContainer);

            _panelsContainer = new VBoxContainer();
            _panelsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _panelsContainer.AddThemeConstantOverride("separation", 0);
            scrollContainer.AddChild(_panelsContainer);

            Logging.Logger.Log("[ECSControlPanel] UI built successfully");
        }

        private void BuildStaticHeader(VBoxContainer parent)
        {
            var headerPanel = new PanelContainer();
            headerPanel.CustomMinimumSize = new Vector2(0, 48);
            parent.AddChild(headerPanel);

            var headerMargin = new MarginContainer();
            headerMargin.AddThemeConstantOverride("margin_left", 12);
            headerMargin.AddThemeConstantOverride("margin_right", 12);
            headerMargin.AddThemeConstantOverride("margin_top", 8);
            headerMargin.AddThemeConstantOverride("margin_bottom", 8);
            headerPanel.AddChild(headerMargin);

            var headerHBox = new HBoxContainer();
            headerHBox.AddThemeConstantOverride("separation", 16);
            headerMargin.AddChild(headerHBox);

            // Spacer (pushes title to center)
            var leftSpacer = new Control();
            leftSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            headerHBox.AddChild(leftSpacer);

            // Title
            var titleLabel = new Label();
            titleLabel.Text = "ECS CONTROL PANEL";
            titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
            titleLabel.VerticalAlignment = VerticalAlignment.Center;
            titleLabel.AddThemeFontSizeOverride("font_size", 20);
            headerHBox.AddChild(titleLabel);

            // Spacer (pushes close button to right)
            var rightSpacer = new Control();
            rightSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            headerHBox.AddChild(rightSpacer);

            // Close button
            var closeButton = new Button();
            closeButton.Text = "Close (F12)";
            closeButton.Pressed += OnClosePressed;
            headerHBox.AddChild(closeButton);
        }

        private void LoadAndCreatePanels()
        {
            _config = new PanelConfiguration();
            var panelSettings = _config.LoadOrDiscover();

            Logging.Logger.Log($"[ECSControlPanel] Creating {panelSettings.Count} panels...");

            foreach (var settings in panelSettings)
            {
                if (!settings.Enabled)
                    continue;

                try
                {
                    // Create panel instance via reflection
                    var type = Type.GetType(settings.TypeName);
                    if (type == null)
                    {
                        Logging.Logger.Log($"[ECSControlPanel] Failed to find type: {settings.TypeName}", Logging.LogSeverity.Warning);
                        continue;
                    }

                    var panel = Activator.CreateInstance(type, _world) as IControlPanelSection;
                    if (panel == null)
                    {
                        Logging.Logger.Log($"[ECSControlPanel] Failed to create instance of {type.Name}", Logging.LogSeverity.Warning);
                        continue;
                    }

                    // Set expanded state from config
                    panel.IsExpanded = settings.Expanded;

                    // Create wrapper UI
                    var panelUI = new PanelSectionUI(panel);
                    _panelsContainer.AddChild(panelUI);
                    _panels.Add(panelUI);

                    Logging.Logger.Log($"[ECSControlPanel] Created panel: {panel.Title}");
                }
                catch (Exception ex)
                {
                    Logging.Logger.Log($"[ECSControlPanel] Error creating panel {settings.TypeName}: {ex.Message}", Logging.LogSeverity.Error);
                }
            }
        }

        #endregion

        #region Update Loop

        public override void _Process(double delta)
        {
            if (!Visible)
                return;

            // Update all panels
            foreach (var panelUI in _panels)
            {
                panelUI.Panel.Update(delta);
            }
        }

        #endregion

        #region Event Handlers

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
                CallDeferred(MethodName.OnShowInternal);
            }
            else
            {
                OnHideInternal();
            }
        }

        public new void Show()
        {
            Visible = true;
            CallDeferred(MethodName.OnShowInternal);
        }

        public new void Hide()
        {
            Visible = false;
            OnHideInternal();
        }

        private void OnShowInternal()
        {
            foreach (var panelUI in _panels)
            {
                panelUI.Panel.OnShow();
            }
        }

        private void OnHideInternal()
        {
            // Save panel configuration
            var panels = _panels.Select(p => p.Panel).ToList();
            _config?.Save(panels);

            foreach (var panelUI in _panels)
            {
                panelUI.Panel.OnHide();
            }
        }

        #endregion
    }

    /// <summary>
    /// UI wrapper for a panel section with collapsible header.
    /// </summary>
    internal partial class PanelSectionUI : PanelContainer
    {
        public IControlPanelSection Panel { get; }

        private Button _expandButton;
        private Label _titleLabel;
        private Control _contentContainer;

        public PanelSectionUI(IControlPanelSection panel)
        {
            Panel = panel;

            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(0, 40);

            BuildUI();
        }

        private void BuildUI()
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
            _expandButton.Text = Panel.IsExpanded ? "v" : ">";
            _expandButton.CustomMinimumSize = new Vector2(32, 0);
            _expandButton.Pressed += OnExpandToggled;
            headerHBox.AddChild(_expandButton);

            // Title
            _titleLabel = new Label();
            _titleLabel.Text = Panel.Title;
            _titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _titleLabel.VerticalAlignment = VerticalAlignment.Center;
            headerHBox.AddChild(_titleLabel);

            // Custom header buttons (if any)
            var customButtons = Panel.CreateHeaderButtons();
            if (customButtons != null)
            {
                headerHBox.AddChild(customButtons);
            }

            // === SEPARATOR ===
            var separator = new HSeparator();
            vbox.AddChild(separator);

            // === CONTENT ===
            _contentContainer = Panel.CreateUI();
            _contentContainer.Visible = Panel.IsExpanded;
            vbox.AddChild(_contentContainer);
        }

        private void OnExpandToggled()
        {
            Panel.IsExpanded = !Panel.IsExpanded;
            _contentContainer.Visible = Panel.IsExpanded;
            _expandButton.Text = Panel.IsExpanded ? "v" : ">";
        }
    }
}
