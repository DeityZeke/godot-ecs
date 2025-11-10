#nullable enable

using Godot;

namespace Client.UI
{
    /// <summary>
    /// Simple performance overlay that renders FPS, frame times, and rendering stats in the top-left corner.
    /// Attach to a Control node (or add as a child of any CanvasLayer) for quick benchmarking while flying around.
    /// </summary>
    [GlobalClass]
    public partial class PerfOverlay : Control
    {
        private Label? _label;
        private double _refreshTimer;

        [Export(PropertyHint.Range, "0.05,1,0.05")]
        public double UpdateIntervalSeconds { get; set; } = 0.2;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsPreset(LayoutPreset.TopLeft);
            OffsetLeft = 8;
            OffsetTop = 8;

            _label = new Label
            {
                AutowrapMode = TextServer.AutowrapMode.Off,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Text = "PerfOverlay"
            };

            var settings = new LabelSettings
            {
                FontSize = 16,
                OutlineSize = 1,
                OutlineColor = Colors.Black,
                FontColor = Colors.Chartreuse
            };
            _label.LabelSettings = settings;

            // Lightweight background for readability
            var background = new ColorRect
            {
                Color = new Color(0f, 0f, 0f, 0.45f),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };

            AddChild(background);
            background.AddChild(_label);
        }

        public override void _Process(double delta)
        {
            _refreshTimer -= delta;
            if (_refreshTimer > 0 || _label == null)
                return;

            _refreshTimer = UpdateIntervalSeconds;

            double fps = Engine.GetFramesPerSecond();
            double frameMs = fps > 0 ? 1000.0 / fps : 0;
            double processMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
            double physicsMs = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;

            int objects = (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
            int primitives = (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
            int drawCalls = (int)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);

            _label.Text =
                $"FPS: {fps,6:0.0}\n" +
                $"Frame: {frameMs,6:0.00} ms | Proc: {processMs,5:0.00} ms | Phys: {physicsMs,5:0.00} ms\n" +
                $"Objects: {objects,7:N0}\n" +
                $"Primitives: {primitives,7:N0}\n" +
                $"Draw Calls: {drawCalls,7:N0}";
        }
    }
}
