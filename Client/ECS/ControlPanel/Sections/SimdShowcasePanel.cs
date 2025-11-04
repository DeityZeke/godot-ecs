#nullable enable

using Godot;
using UltraSim.ECS;
using UltraSim.ECS.SIMD;
using UltraSim.UI;

namespace UltraSim.Client.ECS.ControlPanel
{
    /// <summary>
    /// Control panel section for SIMD performance showcase and benchmarking.
    /// Allows manual selection of SIMD modes for Core ECS vs Systems.
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

        // Core ECS radio buttons
        private CheckBox? _coreScalarRadio;
        private CheckBox? _core128Radio;
        private CheckBox? _core256Radio;
        private CheckBox? _core512Radio;

        // Systems radio buttons
        private CheckBox? _systemsScalarRadio;
        private CheckBox? _systems128Radio;
        private CheckBox? _systems256Radio;
        private CheckBox? _systems512Radio;

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
                "(When disabled, uses max hardware capability)",
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

            mainVBox.AddChild(new HSeparator());

            // === CORE ECS SECTION ===
            var coreLabel = CreateLabel("Core ECS (/UltraSim/ECS/)", fontSize: 14);
            mainVBox.AddChild(coreLabel);

            var coreGrid = CreateGrid(columns: 4, hSeparation: 8, vSeparation: 4);
            mainVBox.AddChild(coreGrid);

            _coreScalarRadio = CreateSimdRadioButton("Scalar", SimdMode.Scalar);
            _core128Radio = CreateSimdRadioButton("128-bit (SSE)", SimdMode.Simd128);
            _core256Radio = CreateSimdRadioButton("256-bit (AVX)", SimdMode.Simd256);
            _core512Radio = CreateSimdRadioButton("512-bit (AVX-512)", SimdMode.Simd512);

            coreGrid.AddChild(_coreScalarRadio);
            coreGrid.AddChild(_core128Radio);
            coreGrid.AddChild(_core256Radio);
            coreGrid.AddChild(_core512Radio);

            // Connect signals for Core radios
            _coreScalarRadio.Toggled += (pressed) => OnCoreRadioToggled(SimdMode.Scalar, pressed);
            _core128Radio.Toggled += (pressed) => OnCoreRadioToggled(SimdMode.Simd128, pressed);
            _core256Radio.Toggled += (pressed) => OnCoreRadioToggled(SimdMode.Simd256, pressed);
            _core512Radio.Toggled += (pressed) => OnCoreRadioToggled(SimdMode.Simd512, pressed);

            mainVBox.AddChild(new HSeparator());

            // === SYSTEMS SECTION ===
            var systemsLabel = CreateLabel("Systems (Game Logic)", fontSize: 14);
            mainVBox.AddChild(systemsLabel);

            var systemsGrid = CreateGrid(columns: 4, hSeparation: 8, vSeparation: 4);
            mainVBox.AddChild(systemsGrid);

            _systemsScalarRadio = CreateSimdRadioButton("Scalar", SimdMode.Scalar);
            _systems128Radio = CreateSimdRadioButton("128-bit (SSE)", SimdMode.Simd128);
            _systems256Radio = CreateSimdRadioButton("256-bit (AVX)", SimdMode.Simd256);
            _systems512Radio = CreateSimdRadioButton("512-bit (AVX-512)", SimdMode.Simd512);

            systemsGrid.AddChild(_systemsScalarRadio);
            systemsGrid.AddChild(_systems128Radio);
            systemsGrid.AddChild(_systems256Radio);
            systemsGrid.AddChild(_systems512Radio);

            // Connect signals for Systems radios
            _systemsScalarRadio.Toggled += (pressed) => OnSystemsRadioToggled(SimdMode.Scalar, pressed);
            _systems128Radio.Toggled += (pressed) => OnSystemsRadioToggled(SimdMode.Simd128, pressed);
            _systems256Radio.Toggled += (pressed) => OnSystemsRadioToggled(SimdMode.Simd256, pressed);
            _systems512Radio.Toggled += (pressed) => OnSystemsRadioToggled(SimdMode.Simd512, pressed);

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

        private void OnCoreRadioToggled(SimdMode mode, bool pressed)
        {
            if (_updatingRadios) return; // Prevent event loops

            // If user clicked the already-selected button, keep it selected
            if (!pressed && SimdManager.GetMode(SimdCategory.Core) == mode)
            {
                UpdateCoreRadios(mode); // Re-select the button
                return;
            }

            if (!pressed) return; // Ignore other uncheck events

            SimdManager.SetMode(SimdCategory.Core, mode);
            UpdateCoreRadios(mode);
        }

        private void OnSystemsRadioToggled(SimdMode mode, bool pressed)
        {
            if (_updatingRadios) return; // Prevent event loops

            // If user clicked the already-selected button, keep it selected
            if (!pressed && SimdManager.GetMode(SimdCategory.Systems) == mode)
            {
                UpdateSystemsRadios(mode); // Re-select the button
                return;
            }

            if (!pressed) return; // Ignore other uncheck events

            SimdManager.SetMode(SimdCategory.Systems, mode);
            UpdateSystemsRadios(mode);
        }

        private void UpdateUIState()
        {
            bool showcaseEnabled = SimdManager.ShowcaseEnabled;

            // Enable/disable all radio buttons
            SetRadioEnabled(_coreScalarRadio, SimdMode.Scalar, showcaseEnabled);
            SetRadioEnabled(_core128Radio, SimdMode.Simd128, showcaseEnabled);
            SetRadioEnabled(_core256Radio, SimdMode.Simd256, showcaseEnabled);
            SetRadioEnabled(_core512Radio, SimdMode.Simd512, showcaseEnabled);

            SetRadioEnabled(_systemsScalarRadio, SimdMode.Scalar, showcaseEnabled);
            SetRadioEnabled(_systems128Radio, SimdMode.Simd128, showcaseEnabled);
            SetRadioEnabled(_systems256Radio, SimdMode.Simd256, showcaseEnabled);
            SetRadioEnabled(_systems512Radio, SimdMode.Simd512, showcaseEnabled);

            // Update selected radios
            UpdateCoreRadios(SimdManager.GetMode(SimdCategory.Core));
            UpdateSystemsRadios(SimdManager.GetMode(SimdCategory.Systems));
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

        private void UpdateCoreRadios(SimdMode selected)
        {
            _updatingRadios = true;
            try
            {
                if (_coreScalarRadio != null) _coreScalarRadio.ButtonPressed = (selected == SimdMode.Scalar);
                if (_core128Radio != null) _core128Radio.ButtonPressed = (selected == SimdMode.Simd128);
                if (_core256Radio != null) _core256Radio.ButtonPressed = (selected == SimdMode.Simd256);
                if (_core512Radio != null) _core512Radio.ButtonPressed = (selected == SimdMode.Simd512);
            }
            finally
            {
                _updatingRadios = false;
            }
        }

        private void UpdateSystemsRadios(SimdMode selected)
        {
            _updatingRadios = true;
            try
            {
                if (_systemsScalarRadio != null) _systemsScalarRadio.ButtonPressed = (selected == SimdMode.Scalar);
                if (_systems128Radio != null) _systems128Radio.ButtonPressed = (selected == SimdMode.Simd128);
                if (_systems256Radio != null) _systems256Radio.ButtonPressed = (selected == SimdMode.Simd256);
                if (_systems512Radio != null) _systems512Radio.ButtonPressed = (selected == SimdMode.Simd512);
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
