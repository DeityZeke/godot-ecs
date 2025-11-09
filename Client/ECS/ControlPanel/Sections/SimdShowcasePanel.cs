#nullable enable

using Godot;
using UltraSim.ECS;
using UltraSim.ECS.SIMD;
using Client.UI;

namespace Client.ECS.ControlPanel.Sections
{
    /// <summary>
    /// Control panel section for SIMD performance showcase and benchmarking.
    /// Allows manual selection of SIMD modes for testing and demonstration.
    /// When disabled, uses optimal per-operation modes automatically.
    /// </summary>
    [ControlPanelSection(defaultOrder: 15, defaultExpanded: false)]
    public partial class SimdShowcasePanel : UIBuilder, IControlPanelSection
    {
        private World? _world;
        private bool _isExpanded = false;
        private bool _updatingRadios = false; // Flag to prevent event loops

        // UI Controls
        private CheckBox? _showcaseEnabledCheckbox;
        private Label? _hardwareInfoLabel;
        private Label? _optimalModesLabel;

        // SIMD level radio buttons
        private CheckBox? _scalarRadio;
        private CheckBox? _simd128Radio;
        private CheckBox? _simd256Radio;
        private CheckBox? _simd512Radio;

        public SimdShowcasePanel(World? world)
        {
            _world = world;
        }

        public string Id => "simd_showcase_panel";
        public string Title => "SIMD Showcase";

        public bool IsExpanded
        {
            get => _isExpanded;
            set => _isExpanded = value;
        }

        public Control? CreateHeaderButtons()
        {
            return null; // No custom header buttons
        }

        public Control CreateUI()
        {
            var mainVBox = CreateVBox(separation: 12);
            mainVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;

            // === SHOWCASE ENABLE TOGGLE ===
            var enableHBox = CreateHBox(separation: 8);
            mainVBox.AddChild(enableHBox);

            _showcaseEnabledCheckbox = new CheckBox
            {
                Text = "Enable Showcase Mode",
                ButtonPressed = SimdManager.ShowcaseEnabled
            };
            _showcaseEnabledCheckbox.Toggled += OnShowcaseToggled;
            enableHBox.AddChild(_showcaseEnabledCheckbox);

            var helpLabel = CreateLabel(
                "(When disabled, uses optimal mode per operation)",
                fontSize: 12,
                color: new Color(0.7f, 0.7f, 0.7f)
            );
            enableHBox.AddChild(helpLabel);

            // === HARDWARE INFO ===
            _hardwareInfoLabel = CreateLabel(
                $"Hardware: {SimdManager.GetHardwareInfo()}",
                fontSize: 12,
                color: new Color(0.5f, 0.8f, 1.0f)
            );
            mainVBox.AddChild(_hardwareInfoLabel);

            // === OPTIMAL MODES INFO ===
            _optimalModesLabel = CreateLabel(
                "Optimal: ApplyVelocity=SSE, ProcessPulsing=Scalar",
                fontSize: 11,
                color: new Color(0.5f, 1.0f, 0.5f)
            );
            mainVBox.AddChild(_optimalModesLabel);

            mainVBox.AddChild(new HSeparator());

            // === SIMD LEVEL SELECTOR ===
            var levelLabel = CreateLabel("SIMD Level (Systems)", fontSize: 14);
            mainVBox.AddChild(levelLabel);

            var levelGrid = CreateGrid(columns: 4, hSeparation: 8, vSeparation: 4);
            mainVBox.AddChild(levelGrid);

            _scalarRadio = CreateSimdRadioButton("Scalar", SimdMode.Scalar);
            _simd128Radio = CreateSimdRadioButton("128-bit (SSE)", SimdMode.Simd128);
            _simd256Radio = CreateSimdRadioButton("256-bit (AVX2)", SimdMode.Simd256);
            _simd512Radio = CreateSimdRadioButton("512-bit (AVX-512)", SimdMode.Simd512);

            levelGrid.AddChild(_scalarRadio);
            levelGrid.AddChild(_simd128Radio);
            levelGrid.AddChild(_simd256Radio);
            levelGrid.AddChild(_simd512Radio);

            // Connect signals for SIMD level radios
            _scalarRadio.Toggled += (pressed) => OnSimdRadioToggled(SimdMode.Scalar, pressed);
            _simd128Radio.Toggled += (pressed) => OnSimdRadioToggled(SimdMode.Simd128, pressed);
            _simd256Radio.Toggled += (pressed) => OnSimdRadioToggled(SimdMode.Simd256, pressed);
            _simd512Radio.Toggled += (pressed) => OnSimdRadioToggled(SimdMode.Simd512, pressed);

            // === INFO TEXT ===
            var infoLabel = CreateLabel(
                "In showcase mode: All SIMD operations use the selected level.\n" +
                "When disabled: Each operation uses its optimal mode automatically.",
                fontSize: 11,
                color: new Color(0.7f, 0.7f, 0.7f)
            );
            mainVBox.AddChild(infoLabel);

            // Initial update
            UpdateUIState();

            return mainVBox;
        }

        private CheckBox CreateSimdRadioButton(string text, SimdMode mode)
        {
            var radio = new CheckBox
            {
                Text = text,
                ButtonPressed = false,
                Disabled = !SimdManager.IsModeSupported(mode) || !SimdManager.ShowcaseEnabled
            };

            // Grey out if not supported
            if (!SimdManager.IsModeSupported(mode))
            {
                radio.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            }

            return radio;
        }

        private void OnShowcaseToggled(bool enabled)
        {
            SimdManager.ShowcaseEnabled = enabled;
            UpdateUIState();
        }

        private void OnSimdRadioToggled(SimdMode mode, bool pressed)
        {
            if (_updatingRadios) return; // Prevent event loops

            // If user clicked the already-selected button, keep it selected
            if (!pressed && SimdManager.GetMode(SimdCategory.Systems) == mode)
            {
                UpdateSimdRadios(mode); // Re-select the button
                return;
            }

            if (!pressed) return; // Ignore other uncheck events

            SimdManager.SetMode(SimdCategory.Systems, mode);
            UpdateSimdRadios(mode);
        }

        private void UpdateUIState()
        {
            bool showcaseEnabled = SimdManager.ShowcaseEnabled;

            // Enable/disable all radio buttons
            SetRadioEnabled(_scalarRadio, SimdMode.Scalar, showcaseEnabled);
            SetRadioEnabled(_simd128Radio, SimdMode.Simd128, showcaseEnabled);
            SetRadioEnabled(_simd256Radio, SimdMode.Simd256, showcaseEnabled);
            SetRadioEnabled(_simd512Radio, SimdMode.Simd512, showcaseEnabled);

            // Update selected radio
            UpdateSimdRadios(SimdManager.GetMode(SimdCategory.Systems));

            // Update optimal modes label visibility
            if (_optimalModesLabel != null)
            {
                _optimalModesLabel.Visible = !showcaseEnabled;
            }
        }

        private void SetRadioEnabled(CheckBox? radio, SimdMode mode, bool showcaseEnabled)
        {
            if (radio == null) return;

            bool hardwareSupported = SimdManager.IsModeSupported(mode);
            radio.Disabled = !hardwareSupported || !showcaseEnabled;

            // Grey out if not supported or not in showcase mode
            if (!hardwareSupported)
            {
                radio.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            }
            else if (!showcaseEnabled)
            {
                radio.Modulate = new Color(0.7f, 0.7f, 0.7f, 0.7f);
            }
            else
            {
                radio.Modulate = Colors.White;
            }
        }

        private void UpdateSimdRadios(SimdMode selected)
        {
            _updatingRadios = true;
            try
            {
                if (_scalarRadio != null) _scalarRadio.ButtonPressed = (selected == SimdMode.Scalar);
                if (_simd128Radio != null) _simd128Radio.ButtonPressed = (selected == SimdMode.Simd128);
                if (_simd256Radio != null) _simd256Radio.ButtonPressed = (selected == SimdMode.Simd256);
                if (_simd512Radio != null) _simd512Radio.ButtonPressed = (selected == SimdMode.Simd512);
            }
            finally
            {
                _updatingRadios = false;
            }
        }

        public void Update(double delta)
        {
            // No per-frame updates needed
        }

        public void OnShow()
        {
            UpdateUIState();
        }

        public void OnHide()
        {
            // Nothing to do
        }
    }
}
