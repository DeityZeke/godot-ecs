#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using UltraSim;
using UltraSim.ECS;
using Client.UI;

namespace Client.ECS.ControlPanel
{
    /// <summary>
    /// Modular ECS Control Panel - lightweight container for panel sections.
    /// Press F12 to toggle visibility.
    /// </summary>
    public partial class ECSControlPanel : UIBuilder
    {
        private World _world = null!;
        private PanelConfiguration _config = null!;
        private List<PanelSectionUI> _panels = new();

        // UI References
        private PanelContainer _mainPanel = null!;
        private VBoxContainer _panelsContainer = null!;

        #region Initialization

        public void Initialize(World world)
        {
            _world = world;
            Logging.Log("Control Panel Initializing...");
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

            Logging.Log("Control Panel Ready! Press F12 to toggle");

            // Force initial layout calculation
            CallDeferred(MethodName.UpdateMinimumSize);
        }

        private void BuildUI()
        {
            // Calculate padding
            Vector2 viewportSize = GetTree().Root.Size;
            int padding = (int)Mathf.Clamp(Mathf.Min(viewportSize.X, viewportSize.Y) * 0.05f, 50f, 150f);

            // ROOT MARGIN - creates the padding border around the panel
            var rootMargin = CreateMargin(padding);
            rootMargin.Name = "RootMargin";
            rootMargin.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(rootMargin);

            // PANEL - the visible background
            _mainPanel = CreatePanel();
            _mainPanel.Name = "MainPanel";
            rootMargin.AddChild(_mainPanel);

            // INNER MARGIN - padding inside the panel
            var margin = CreateMargin(16);
            _mainPanel.AddChild(margin);

            // Main vertical layout
            var mainVBox = CreateVBox(separation: 0);
            mainVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
            margin.AddChild(mainVBox);

            // === STATIC HEADER ===
            BuildStaticHeader(mainVBox);

            // === SEPARATOR UNDER HEADER ===
            var topSeparator = new HSeparator();
            mainVBox.AddChild(topSeparator);

            // === PANELS CONTAINER ===
            var scrollContainer = new ScrollContainer();
            scrollContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
            scrollContainer.CustomMinimumSize = new Vector2(0, 200);
            scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
            scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
            mainVBox.AddChild(scrollContainer);

            _panelsContainer = CreateVBox(separation: 8);
            _panelsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            scrollContainer.AddChild(_panelsContainer);
        }

        private void BuildStaticHeader(VBoxContainer parent)
        {
            var headerPanel = CreatePanel();
            headerPanel.CustomMinimumSize = new Vector2(0, 48);
            parent.AddChild(headerPanel);

            var headerMargin = CreateMargin(12, 8, 12, 8);
            headerPanel.AddChild(headerMargin);

            var headerHBox = CreateHBox(separation: 16);
            headerMargin.AddChild(headerHBox);

            // Spacer (pushes title to center)
            var leftSpacer = new Control();
            leftSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            headerHBox.AddChild(leftSpacer);

            // Title
            var titleLabel = CreateLabel("ECS CONTROL PANEL", fontSize: 20,
                hAlign: HorizontalAlignment.Center, vAlign: VerticalAlignment.Center);
            headerHBox.AddChild(titleLabel);

            // Spacer (pushes close button to right)
            var rightSpacer = new Control();
            rightSpacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            headerHBox.AddChild(rightSpacer);

            // Close button
            var closeButton = CreateButton("Close (F12)", OnClosePressed);
            headerHBox.AddChild(closeButton);
        }

        private void LoadAndCreatePanels()
        {
            _config = new PanelConfiguration();
            var panelSettings = _config.LoadOrDiscover();

            Logging.Log($" - {panelSettings.Count} Panels Detected");
            Logging.Log(" - Loaded and Applied Panel Settings");

            int createdCount = 0;
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
                        Logging.Log($"Failed to find panel type: {settings.TypeName}", LogSeverity.Error);
                        continue;
                    }

                    var panel = Activator.CreateInstance(type, _world) as IControlPanelSection;
                    if (panel == null)
                    {
                        Logging.Log($"Failed to create panel instance: {type.Name}", LogSeverity.Error);
                        continue;
                    }

                    // Set expanded state from config
                    panel.IsExpanded = settings.Expanded;

                    // Create wrapper UI
                    var panelUI = new PanelSectionUI(panel);
                    _panelsContainer.AddChild(panelUI);
                    _panels.Add(panelUI);
                    createdCount++;
                }
                catch (Exception ex)
                {
                    Logging.Log($"Error creating panel {settings.TypeName}: {ex.Message}", LogSeverity.Error);
                }
            }

            Logging.Log(" - Built Panel UI's");
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

        private Button? _expandButton;
        private Label? _titleLabel;
        private Control? _contentContainer;

        public PanelSectionUI(IControlPanelSection panel)
        {
            Panel = panel;

            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ShrinkBegin;
            CustomMinimumSize = new Vector2(0, 40);

            BuildUI();
        }

        private void BuildUI()
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
                Text = Panel.IsExpanded ? "v" : ">",
                CustomMinimumSize = new Vector2(32, 0)
            };
            _expandButton.Pressed += OnExpandToggled;
            headerHBox.AddChild(_expandButton);

            // Title
            _titleLabel = new Label
            {
                Text = Panel.Title,
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
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
            _contentContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            vbox.AddChild(_contentContainer);
        }

        private void OnExpandToggled()
        {
            Panel.IsExpanded = !Panel.IsExpanded;
            if (_contentContainer != null)
                _contentContainer.Visible = Panel.IsExpanded;
            if (_expandButton != null)
                _expandButton.Text = Panel.IsExpanded ? "v" : ">";
        }
    }
}


