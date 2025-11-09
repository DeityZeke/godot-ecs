#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using UltraSim;
using UltraSim.ECS;
using UltraSim.ECS.SIMD;
using Client.UI;

namespace Client.ECS.ControlPanel
{
    /// <summary>
    /// Environment and hardware information panel.
    /// Displays build type, platform, CPU, GPU, and SIMD capabilities.
    /// </summary>
    [ControlPanelSection(defaultOrder: 5, defaultExpanded: false)]
    public partial class EnvironmentPanel : UIBuilder, IControlPanelSection
    {
        private IHost? _host;

        // UI References
        private Label? _ramLabel;

        public string Title => "Environment";
        public string Id => "environment_panel";
        public bool IsExpanded { get; set; }

        public EnvironmentPanel(World? world)
        {
            _host = world?.Host;
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

            var envInfo = _host?.Runtime.Environment;

            AddContainerLabel(buildGrid, "Environment:");
            AddContainerLabel(buildGrid, (_host?.Environment ?? EnvironmentType.Client).ToString());

            AddContainerLabel(buildGrid, "Build Type:");
            AddContainerLabel(buildGrid, envInfo?.IsDebugBuild == true ? "Debug" : "Release");

            AddContainerLabel(buildGrid, "Platform:");
            AddContainerLabel(buildGrid, envInfo?.Platform ?? "Unknown");

            AddContainerLabel(buildGrid, "Engine:");
            AddContainerLabel(buildGrid, envInfo?.Engine ?? "Unknown");

            AddContainerLabel(buildGrid, ".NET Runtime:");
            AddContainerLabel(buildGrid, envInfo?.DotNetVersion ?? "Unknown");

            // MEMORY Section
            var memoryGrid = CreateGrid(columns: 2);
            leftColumn.AddChild(memoryGrid);

            AddGridHeader(memoryGrid, "Memory");

            AddContainerLabel(memoryGrid, "Godot RAM:");
            _ramLabel = AddContainerLabel(memoryGrid, FormatRam(envInfo?.AvailableRamMB));

            AddContainerLabel(memoryGrid, "Peak:");
            AddContainerLabel(memoryGrid, FormatRam(envInfo?.TotalRamMB));

            // === RIGHT COLUMN ===
            var rightColumn = CreateVBox(separation: 16);
            rightColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            mainHBox.AddChild(rightColumn);

            // CPU Section
            var cpuGrid = CreateGrid(columns: 2);
            rightColumn.AddChild(cpuGrid);

            AddGridHeader(cpuGrid, "CPU");

            AddContainerLabel(cpuGrid, "Processor:");
            var processorLabel = AddContainerLabel(cpuGrid, envInfo?.ProcessorName ?? "Unknown");
            processorLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            processorLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            AddContainerLabel(cpuGrid, "Cores:");
            if (envInfo != null)
                AddContainerLabel(cpuGrid, $"{envInfo.PhysicalCores} Physical / {envInfo.LogicalCores} Logical");
            else
                AddContainerLabel(cpuGrid, "Unknown");

            AddContainerLabel(cpuGrid, "Max SIMD:");
            AddContainerLabel(cpuGrid, (envInfo?.SimdSupport ?? SimdSupport.Scalar).ToString());

            // GPU Section
            var gpuGrid = CreateGrid(columns: 2);
            rightColumn.AddChild(gpuGrid);

            AddGridHeader(gpuGrid, "GPU");

            AddContainerLabel(gpuGrid, "GPU:");
            var gpuLabel = AddContainerLabel(gpuGrid,
                envInfo != null ? $"{envInfo.GpuVendor} {envInfo.GpuName}" : "Unknown");
            gpuLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            gpuLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;

            AddContainerLabel(gpuGrid, "VRAM Used:");
            AddContainerLabel(gpuGrid, FormatRam(envInfo?.TotalVramMB));

            AddContainerLabel(gpuGrid, "Graphics API:");
            AddContainerLabel(gpuGrid, envInfo?.GraphicsAPI ?? "Unknown");

            return mainHBox;
        }

        public void Update(double delta)
        {
            var envInfo = _host?.Runtime.Environment;
            if (envInfo != null && _ramLabel != null)
            {
                _ramLabel.Text = FormatRam(envInfo.AvailableRamMB);
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

        private static string FormatRam(long? value) =>
            value.HasValue ? $"{value.Value:N0} MB" : "Unknown";
    }
}
