
using Godot;

using UltraSim;

namespace UltraSim.ECS
{
    /// <summary>
    /// Environment and hardware information panel.
    /// Displays build type, platform, CPU, GPU, and SIMD capabilities.
    /// </summary>
    [ControlPanelSection(defaultOrder: 5, defaultExpanded: false)]
    public class EnvironmentPanel : IControlPanelSection
    {
        private IEnvironmentInfo _env;

        // UI References
        private Label _environmentLabel;
        private Label _buildTypeLabel;
        private Label _platformLabel;
        private Label _engineLabel;
        private Label _dotnetLabel;
        private Label _cpuLabel;
        private Label _coresLabel;
        private Label _simdLabel;
        private Label _ramLabel;
        private Label _gpuLabel;
        private Label _vramLabel;
        private Label _graphicsApiLabel;

        public string Title => "Environment";
        public string Id => "environment_panel";
        public bool IsExpanded { get; set; }

        public EnvironmentPanel(World world)
        {
            _env = SimContext.Host;
        }

        public Control CreateHeaderButtons() => null;

        public Control CreateUI()
        {
            // Use GridContainer: 2 columns (label, value)
            var grid = new GridContainer();
            grid.Columns = 2;
            grid.AddThemeConstantOverride("h_separation", 16);
            grid.AddThemeConstantOverride("v_separation", 8);

            // === BUILD & PLATFORM ===
            AddHeader(grid, "Build & Platform");

            grid.AddChild(CreateLabelCell("Environment:"));
            _environmentLabel = CreateDataCell(_env.Environment.ToString());
            grid.AddChild(_environmentLabel);

            grid.AddChild(CreateLabelCell("Build Type:"));
            _buildTypeLabel = CreateDataCell(_env.IsDebugBuild ? "Debug" : "Release");
            grid.AddChild(_buildTypeLabel);

            grid.AddChild(CreateLabelCell("Platform:"));
            _platformLabel = CreateDataCell(_env.Platform);
            grid.AddChild(_platformLabel);

            grid.AddChild(CreateLabelCell("Engine:"));
            _engineLabel = CreateDataCell(_env.Engine);
            grid.AddChild(_engineLabel);

            grid.AddChild(CreateLabelCell(".NET Runtime:"));
            _dotnetLabel = CreateDataCell(_env.DotNetVersion);
            grid.AddChild(_dotnetLabel);

            // === CPU ===
            AddSpacer(grid);
            AddHeader(grid, "CPU");

            grid.AddChild(CreateLabelCell("Processor:"));
            _cpuLabel = CreateDataCell(_env.ProcessorName);
            grid.AddChild(_cpuLabel);

            grid.AddChild(CreateLabelCell("Cores:"));
            _coresLabel = CreateDataCell($"{_env.PhysicalCores} Physical / {_env.LogicalCores} Logical");
            grid.AddChild(_coresLabel);

            grid.AddChild(CreateLabelCell("Max SIMD:"));
            _simdLabel = CreateDataCell(_env.MaxSimdSupport.ToString());
            grid.AddChild(_simdLabel);

            // === MEMORY ===
            AddSpacer(grid);
            AddHeader(grid, "Memory");

            grid.AddChild(CreateLabelCell("System RAM:"));
            _ramLabel = CreateDataCell($"{_env.TotalRamMB:N0} MB");
            grid.AddChild(_ramLabel);

            // === GPU ===
            AddSpacer(grid);
            AddHeader(grid, "GPU");

            grid.AddChild(CreateLabelCell("GPU:"));
            _gpuLabel = CreateDataCell($"{_env.GpuVendor} {_env.GpuName}");
            grid.AddChild(_gpuLabel);

            grid.AddChild(CreateLabelCell("VRAM:"));
            _vramLabel = CreateDataCell($"{_env.TotalVramMB:N0} MB");
            grid.AddChild(_vramLabel);

            grid.AddChild(CreateLabelCell("Graphics API:"));
            _graphicsApiLabel = CreateDataCell(_env.GraphicsAPI);
            grid.AddChild(_graphicsApiLabel);

            return grid;
        }

        public void Update(double delta)
        {
            // Update dynamic values (RAM availability changes)
            _ramLabel.Text = $"{_env.TotalRamMB:N0} MB (Available: {_env.AvailableRamMB:N0} MB)";
        }

        public void OnShow()
        {
            // Nothing to do on show
        }

        public void OnHide()
        {
            // Nothing to do on hide
        }

        private Label CreateLabelCell(string text)
        {
            var label = new Label();
            label.Text = text;
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Center;
            return label;
        }

        private Label CreateDataCell(string text)
        {
            var label = new Label();
            label.Text = text;
            label.HorizontalAlignment = HorizontalAlignment.Left;
            label.VerticalAlignment = VerticalAlignment.Center;
            return label;
        }

        private void AddHeader(GridContainer grid, string headerText)
        {
            var header = new Label();
            header.Text = headerText;
            header.AddThemeFontSizeOverride("font_size", 14);
            header.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 1.0f));
            grid.AddChild(header);

            // Empty cell for second column
            grid.AddChild(new Control());
        }

        private void AddSpacer(GridContainer grid)
        {
            var spacer1 = new Control();
            spacer1.CustomMinimumSize = new Vector2(0, 8);
            grid.AddChild(spacer1);

            var spacer2 = new Control();
            spacer2.CustomMinimumSize = new Vector2(0, 8);
            grid.AddChild(spacer2);
        }
    }
}
