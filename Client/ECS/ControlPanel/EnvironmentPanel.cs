
#nullable enable

using System;

using Godot;

using UltraSim.UI;

namespace UltraSim.ECS
{
    /// <summary>
    /// Environment and hardware information panel.
    /// Displays build type, platform, CPU, GPU, and SIMD capabilities.
    /// </summary>
    [ControlPanelSection(defaultOrder: 5, defaultExpanded: false)]
    public partial class EnvironmentPanel : UIBuilder, IControlPanelSection
    {
        private IEnvironmentInfo? _env;

        // UI References
        private Label? _ramLabel;

        public string Title => "Environment";
        public string Id => "environment_panel";
        public bool IsExpanded { get; set; }

        public EnvironmentPanel(World? world)
        {
            if (world != null)
            {
                _env = SimContext.Host;
            }
        }

        public Control? CreateHeaderButtons() => null;

        public Control CreateUI()
        {
            // Main HBox to hold two columns
            var mainHBox = CreateHBox(separation: 32);

            // === LEFT COLUMN ===
            var leftColumn = CreateVBox(separation: 16);
            leftColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            mainHBox.AddChild(leftColumn);

            // BUILD & PLATFORM Section
            var buildGrid = CreateGrid(columns: 2);
            leftColumn.AddChild(buildGrid);

            AddGridHeader(buildGrid, "Build & Platform");

            AddContainerLabel(buildGrid, "Environment:");
            AddContainerLabel(buildGrid, _env!.Environment.ToString());

            AddContainerLabel(buildGrid, "Build Type:");
            AddContainerLabel(buildGrid, _env.IsDebugBuild ? "Debug" : "Release");

            AddContainerLabel(buildGrid, "Platform:");
            AddContainerLabel(buildGrid, _env.Platform);

            AddContainerLabel(buildGrid, "Engine:");
            AddContainerLabel(buildGrid, _env.Engine);

            AddContainerLabel(buildGrid, ".NET Runtime:");
            AddContainerLabel(buildGrid, _env.DotNetVersion);

            // MEMORY Section
            var memoryGrid = CreateGrid(columns: 2);
            leftColumn.AddChild(memoryGrid);

            AddGridHeader(memoryGrid, "Memory");

            AddContainerLabel(memoryGrid, "Godot RAM:");
            _ramLabel = AddContainerLabel(memoryGrid, $"{_env.AvailableRamMB:N0} MB");

            AddContainerLabel(memoryGrid, "Peak:");
            AddContainerLabel(memoryGrid, $"{_env.TotalRamMB:N0} MB");

            // === RIGHT COLUMN ===
            var rightColumn = CreateVBox(separation: 16);
            rightColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            mainHBox.AddChild(rightColumn);

            // CPU Section
            var cpuGrid = CreateGrid(columns: 2);
            rightColumn.AddChild(cpuGrid);

            AddGridHeader(cpuGrid, "CPU");

            AddContainerLabel(cpuGrid, "Processor:");
            var processorLabel = AddContainerLabel(cpuGrid, _env.ProcessorName);
            processorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            processorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            AddContainerLabel(cpuGrid, "Cores:");
            AddContainerLabel(cpuGrid, $"{_env.PhysicalCores} Physical / {_env.LogicalCores} Logical");

            AddContainerLabel(cpuGrid, "Max SIMD:");
            AddContainerLabel(cpuGrid, _env.MaxSimdSupport.ToString());

            // GPU Section
            var gpuGrid = CreateGrid(columns: 2);
            rightColumn.AddChild(gpuGrid);

            AddGridHeader(gpuGrid, "GPU");

            AddContainerLabel(gpuGrid, "GPU:");
            var gpuLabel = AddContainerLabel(gpuGrid, $"{_env.GpuVendor} {_env.GpuName}");
            gpuLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            gpuLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            AddContainerLabel(gpuGrid, "VRAM Used:");
            AddContainerLabel(gpuGrid, $"{_env.TotalVramMB:N0} MB");

            AddContainerLabel(gpuGrid, "Graphics API:");
            AddContainerLabel(gpuGrid, _env.GraphicsAPI);

            return mainHBox;
        }

        public void Update(double delta)
        {
            if (_env != null && _ramLabel != null)
            {
                // Update current RAM usage
                _ramLabel.Text = $"{_env.AvailableRamMB:N0} MB";
            }
        }

        public void OnShow()
        {
            // Nothing to do on show
        }

        public void OnHide()
        {
            // Nothing to do on hide
        }
    }
}