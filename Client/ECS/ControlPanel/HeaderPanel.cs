
using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// Header panel displaying ECS statistics (entities, archetypes, FPS, memory, etc.).
    /// </summary>
    [ControlPanelSection(defaultOrder: 0, defaultExpanded: true)]
    public class HeaderPanel : IControlPanelSection
    {
        private World _world;
        private Label _statsLabel;

        public string Title => "Statistics";
        public string Id => "header_stats";
        public bool IsExpanded { get; set; }

        public HeaderPanel(World world)
        {
            _world = world;
        }

        public Control CreateUI()
        {
            var container = new VBoxContainer();
            container.AddThemeConstantOverride("separation", 8);

            // Stats label
            _statsLabel = new Label();
            _statsLabel.Text = "Entities: 0 | Archetypes: 0 | FPS: 60 | Frame: 0.0ms | Memory: 0 MB";
            _statsLabel.AutowrapMode = TextServer.AutowrapMode.Word;
            container.AddChild(_statsLabel);

            return container;
        }

        public void Update(double delta)
        {
            if (_world == null || _statsLabel == null)
                return;

            int entityCount = _world.EntityCount;
            int archetypeCount = _world.ArchetypeCount;

            float fps = (float)Engine.GetFramesPerSecond();
            float frameTime = (float)Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000f;
            long memoryMB = (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1024 / 1024;

            _statsLabel.Text = $"Entities: {entityCount} | Archetypes: {archetypeCount} | " +
                              $"FPS: {fps:F0} | Frame: {frameTime:F2}ms | Memory: {memoryMB} MB";
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
