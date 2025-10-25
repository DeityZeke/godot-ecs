#nullable enable

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using UltraSim.ECS;
using UltraSim.ECS.Systems;
using UltraSim.ECS.Testing;

namespace UltraSim
{
    /// <summary>
    /// Enhanced real-time ECS control panel HUD with GUI controls.
    /// Shows system status, entity counts, FPS, memory usage, and provides both hotkey and button controls.
    /// </summary>
    public partial class ECSControlPanel : Control
    {
        [Export] public NodePath? WorldECSPath;
        
        private WorldECS? worldECS;
        private World? world;
        private StressTestManager? testManager;
        
        private Panel? panel;
        private VBoxContainer? container;
        private Dictionary<string, (Label label, ColorRect? indicator)> indicators = new();
        
        private bool hudVisible = true;
        private float updateInterval = 0.1f;
        private float timeSinceUpdate = 0f;
        
        private double lastFrameTime = 0;
        private int frameCount = 0;
        
        // System statistics section
        private VBoxContainer? systemStatsContainer;
        private Dictionary<string, Label> systemStatLabels = new();
        private bool advancedStatsEnabled = false;
        private Button? advancedStatsToggle;

        public override void _Ready()
        {
            if (WorldECSPath == null)
            {
                GD.PrintErr("[ECSControlPanel] WorldECSPath not set!");
                return;
            }

            worldECS = GetNode<WorldECS>(WorldECSPath);
            if (worldECS == null)
            {
                GD.PrintErr("[ECSControlPanel] Failed to get WorldECS node!");
                return;
            }

            world = worldECS.GetWorld();
            testManager = new StressTestManager(world);
            
            BuildUI();
            UpdateIndicators();
            
            GD.Print("[ECSControlPanel] Ready - Press F1 to toggle");
        }
        
        private void BuildUI()
        {
            // Larger panel to accommodate more info
            SetAnchorsPreset(LayoutPreset.TopRight);
            SetOffset(Side.Left, -500);
            SetOffset(Side.Right, -10);
            SetOffset(Side.Top, 10);
            SetOffset(Side.Bottom, 850);
            
            // Semi-transparent panel background
            panel = new Panel();
            panel.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(panel);
            
            // Style the panel
            var styleBox = new StyleBoxFlat();
            styleBox.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.92f);
            styleBox.BorderColor = new Color(0.2f, 0.5f, 0.9f);
            styleBox.BorderWidthLeft = 2;
            styleBox.BorderWidthRight = 2;
            styleBox.BorderWidthTop = 2;
            styleBox.BorderWidthBottom = 2;
            styleBox.CornerRadiusTopLeft = 8;
            styleBox.CornerRadiusTopRight = 8;
            styleBox.CornerRadiusBottomLeft = 8;
            styleBox.CornerRadiusBottomRight = 8;
            panel.AddThemeStyleboxOverride("panel", styleBox);
            
            // ScrollContainer for when content overflows
            var scrollContainer = new ScrollContainer();
            scrollContainer.SetAnchorsPreset(LayoutPreset.FullRect);
            scrollContainer.SetOffset(Side.Left, 5);
            scrollContainer.SetOffset(Side.Right, -5);
            scrollContainer.SetOffset(Side.Top, 5);
            scrollContainer.SetOffset(Side.Bottom, -5);
            panel.AddChild(scrollContainer);
            
            // Main container with padding
            container = new VBoxContainer();
            container.SetAnchorsPreset(LayoutPreset.TopWide);
            container.SetOffset(Side.Left, 10);
            container.SetOffset(Side.Right, -10);
            container.SetOffset(Side.Top, 10);
            scrollContainer.AddChild(container);
            
            // Title
            var title = new Label();
            title.Text = "╔═══ ECS CONTROL PANEL ═══╗";
            title.AddThemeFontSizeOverride("font_size", 16);
            title.AddThemeColorOverride("font_color", new Color(0.2f, 0.7f, 1f));
            container.AddChild(title);
            
            AddSpacer(8);
            
            // Systems section
            AddSection("SYSTEMS");
            AddToggle("F2", "Pulsing System", "pulsing");
            AddToggle("F3", "Movement System", "movement");
            AddToggle("F4", "Render System", "render");
            
            AddSpacer(10);
            
            // Statistics section
            AddSection("STATISTICS");
            AddInfo("", "Entities", "entities");
            AddInfo("", "Archetypes", "archetypes");
            AddInfo("", "Frame Time", "frametime");
            AddInfo("", "FPS", "fps");
            AddInfo("", "Memory Usage", "memory");
            
            AddSpacer(10);
            
            // Advanced Statistics toggle section
            AddSection("ADVANCED STATISTICS");
            
            // Warning label
            var warningLabel = new Label();
            warningLabel.Text = "⚠ Enabling will cause 10-25% performance hit!";
            warningLabel.AddThemeFontSizeOverride("font_size", 11);
            warningLabel.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.2f));
            container.AddChild(warningLabel);
            
            AddSpacer(3);
            
            // Toggle button
            advancedStatsToggle = CreateStyledButton("Enable System Benchmarks", new Color(0.5f, 0.5f, 0.5f));
            advancedStatsToggle.Pressed += () => ToggleAdvancedStats();
            container.AddChild(advancedStatsToggle);
            
            AddSpacer(10);
            
            // System Performance section (hidden by default)
            AddSection("SYSTEM PERFORMANCE");
            systemStatsContainer = new VBoxContainer();
            systemStatsContainer.Visible = false; // Hidden until enabled
            container.AddChild(systemStatsContainer);
            
            AddSpacer(10);
            
            // Entity Spawning section with GUI buttons
            AddSection("ADD ENTITIES");
            
            // Create a grid of spawn buttons
            var spawnGrid = new GridContainer();
            spawnGrid.Columns = 3;
            spawnGrid.AddThemeConstantOverride("h_separation", 5);
            spawnGrid.AddThemeConstantOverride("v_separation", 5);
            container.AddChild(spawnGrid);
            
            // Spawn buttons: 1K, 5K, 10K, 50K, 100K, 1M
            AddSpawnButton(spawnGrid, "+1K", 1000);
            AddSpawnButton(spawnGrid, "+5K", 5000);
            AddSpawnButton(spawnGrid, "+10K", 10000);
            AddSpawnButton(spawnGrid, "+50K", 50000);
            AddSpawnButton(spawnGrid, "+100K", 100000);
            AddSpawnButton(spawnGrid, "+1M", 1000000);
            
            AddSpacer(5);
            
            // Infinity spawn button (spawns until FPS <= 29)
            var infinityButton = CreateStyledButton("∞ Until FPS Low (≤29)", new Color(1f, 0.3f, 0.3f));
            infinityButton.Pressed += () => SpawnUntilFPSLow();
            container.AddChild(infinityButton);
            
            AddSpacer(10);
            
            // Stress Testing section
            AddSection("STRESS TESTING");
            AddInfo("F5", "Spawn Test (Light)", null);
            AddInfo("F6", "Spawn Test (Medium)", null);
            AddInfo("F7", "Spawn Test (Heavy)", null);
            AddInfo("F8", "Churn Test (Medium)", null);
            AddInfo("F9", "Stop Test", null);
            
            AddSpacer(10);
            
            // Controls section
            AddSection("CONTROLS");
            AddInfo("F1", "Toggle HUD", null);
            AddInfo("ESC", "Quit Application", null);
            
            AddSpacer(5);
            
            var footer = new Label();
            footer.Text = "╚═══════════════════════════";
            footer.AddThemeFontSizeOverride("font_size", 16);
            footer.AddThemeColorOverride("font_color", new Color(0.2f, 0.7f, 1f));
            container.AddChild(footer);
        }
        
        private Button CreateStyledButton(string text, Color color)
        {
            var button = new Button();
            button.Text = text;
            button.CustomMinimumSize = new Vector2(0, 30);
            
            // Normal state
            var normalStyle = new StyleBoxFlat();
            normalStyle.BgColor = color with { A = 0.7f };
            normalStyle.BorderColor = color;
            normalStyle.BorderWidthLeft = 2;
            normalStyle.BorderWidthRight = 2;
            normalStyle.BorderWidthTop = 2;
            normalStyle.BorderWidthBottom = 2;
            normalStyle.CornerRadiusTopLeft = 4;
            normalStyle.CornerRadiusTopRight = 4;
            normalStyle.CornerRadiusBottomLeft = 4;
            normalStyle.CornerRadiusBottomRight = 4;
            button.AddThemeStyleboxOverride("normal", normalStyle);
            
            // Hover state
            var hoverStyle = new StyleBoxFlat();
            hoverStyle.BgColor = color with { A = 0.9f };
            hoverStyle.BorderColor = color;
            hoverStyle.BorderWidthLeft = 2;
            hoverStyle.BorderWidthRight = 2;
            hoverStyle.BorderWidthTop = 2;
            hoverStyle.BorderWidthBottom = 2;
            hoverStyle.CornerRadiusTopLeft = 4;
            hoverStyle.CornerRadiusTopRight = 4;
            hoverStyle.CornerRadiusBottomLeft = 4;
            hoverStyle.CornerRadiusBottomRight = 4;
            button.AddThemeStyleboxOverride("hover", hoverStyle);
            
            // Pressed state
            var pressedStyle = new StyleBoxFlat();
            pressedStyle.BgColor = color;
            pressedStyle.BorderColor = Colors.White;
            pressedStyle.BorderWidthLeft = 2;
            pressedStyle.BorderWidthRight = 2;
            pressedStyle.BorderWidthTop = 2;
            pressedStyle.BorderWidthBottom = 2;
            pressedStyle.CornerRadiusTopLeft = 4;
            pressedStyle.CornerRadiusTopRight = 4;
            pressedStyle.CornerRadiusBottomLeft = 4;
            pressedStyle.CornerRadiusBottomRight = 4;
            button.AddThemeStyleboxOverride("pressed", pressedStyle);
            
            return button;
        }
        
        private void AddSpawnButton(GridContainer parent, string text, int count)
        {
            var button = CreateStyledButton(text, new Color(0.3f, 0.8f, 0.3f));
            button.Pressed += () => SpawnEntities(count);
            parent.AddChild(button);
        }
        
        private void SpawnEntities(int count)
        {
            if (world == null)
            {
                GD.PrintErr("[ECSControlPanel] World is null!");
                return;
            }
            
            GD.Print($"[ECSControlPanel] Spawning {count:N0} entities...");
            
            var buffer = new StructuralCommandBuffer();
            
            for (int i = 0; i < count; i++)
            {
                buffer.CreateEntity(builder =>
                {
                    builder.Add(new Position
                    {
                        X = (float)(GD.RandRange(-50, 50)),
                        Y = (float)(GD.RandRange(-50, 50)),
                        Z = (float)(GD.RandRange(-50, 50))
                    })
                    .Add(new Velocity
                    {
                        X = (float)(GD.RandRange(-5, 5)),
                        Y = (float)(GD.RandRange(-5, 5)),
                        Z = (float)(GD.RandRange(-5, 5))
                    })
                    .Add(new RenderTag { })
                    .Add(new Visible { });
                });
            }
            
            buffer.Apply(world);
            GD.Print($"[ECSControlPanel] ✓ Spawned {count:N0} entities");
        }
        
        private void SpawnUntilFPSLow()
        {
            if (world == null || testManager == null)
            {
                GD.PrintErr("[ECSControlPanel] World or TestManager is null!");
                return;
            }
            
            if (testManager.IsRunning)
            {
                GD.PrintErr("[ECSControlPanel] A stress test is already running!");
                return;
            }
            
            GD.Print("[ECSControlPanel] Starting continuous spawn until FPS ≤ 29...");
            
            // Use UntilCrash intensity which spawns until FPS drops
            var config = StressTestConfig.CreatePreset(StressTestType.Spawn, StressIntensity.UntilCrash);
            var test = new SpawnStressTest(world, config);
            
            testManager.QueueTest(test);
            testManager.Start();
        }
        
        private void AddSection(string title)
        {
            var label = new Label();
            label.Text = $"── {title} ──";
            label.AddThemeFontSizeOverride("font_size", 13);
            label.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
            container!.AddChild(label);
            AddSpacer(2);
        }
        
        private void AddToggle(string key, string desc, string id)
        {
            var hbox = new HBoxContainer();
            
            if (!string.IsNullOrEmpty(key))
            {
                var keyLabel = new Label();
                keyLabel.Text = $"[{key}]";
                keyLabel.CustomMinimumSize = new Vector2(50, 0);
                keyLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.9f, 1f));
                hbox.AddChild(keyLabel);
            }
            
            var descLabel = new Label();
            descLabel.Text = desc;
            descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            hbox.AddChild(descLabel);
            
            var indicator = new ColorRect();
            indicator.CustomMinimumSize = new Vector2(12, 12);
            indicator.Color = Colors.Red;
            hbox.AddChild(indicator);
            
            container!.AddChild(hbox);
            indicators[id] = (descLabel, indicator);
        }
        
        private void AddInfo(string key, string desc, string? id)
        {
            var hbox = new HBoxContainer();
            
            if (!string.IsNullOrEmpty(key))
            {
                var keyLabel = new Label();
                keyLabel.Text = $"[{key}]";
                keyLabel.CustomMinimumSize = new Vector2(50, 0);
                keyLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.9f, 1f));
                hbox.AddChild(keyLabel);
            }
            else
            {
                var spacer = new Control();
                spacer.CustomMinimumSize = new Vector2(10, 0);
                hbox.AddChild(spacer);
            }
            
            var descLabel = new Label();
            descLabel.Text = desc;
            descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            descLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            hbox.AddChild(descLabel);
            
            if (id != null)
            {
                var valueLabel = new Label();
                valueLabel.CustomMinimumSize = new Vector2(120, 0);
                valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
                valueLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
                hbox.AddChild(valueLabel);
                
                indicators[id] = (valueLabel, null);
            }
            
            container!.AddChild(hbox);
        }
        
        private void AddSpacer(int height)
        {
            var spacer = new Control();
            spacer.CustomMinimumSize = new Vector2(0, height);
            container!.AddChild(spacer);
        }
        
        public override void _Process(double delta)
        {
            if (!hudVisible || world == null) return;
            
            lastFrameTime = delta;
            frameCount++;
            
            // Update test manager if running
            testManager?.Update((float)delta);
            
            timeSinceUpdate += (float)delta;
            if (timeSinceUpdate >= updateInterval)
            {
                timeSinceUpdate = 0f;
                UpdateIndicators();
                
                // Only update system stats if enabled
                if (advancedStatsEnabled)
                    UpdateSystemStatistics();
            }
        }
        
        private void UpdateIndicators()
        {
            if (world == null) return;

            // Update system toggles - FIX: Check for AdaptiveMultiMeshRenderSystem OR MultiMeshRenderSystem
            UpdateSystemIndicator<OptimizedPulsingMovementSystem>("pulsing");
            UpdateSystemIndicator<OptimizedMovementSystem>("movement");
            
            // Check if either render system is enabled
            bool adaptiveEnabled = world.Systems.IsSystemEnabled<AdaptiveMultiMeshRenderSystem>();
            bool regularEnabled = world.Systems.IsSystemEnabled<MultiMeshRenderSystem>();
            bool renderEnabled = adaptiveEnabled || regularEnabled;
            
            if (indicators.TryGetValue("render", out var renderItem) && renderItem.indicator != null)
            {
                renderItem.indicator.Color = renderEnabled ? 
                    new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);
            }
            
            // Update statistics
            int entityCount = 0;
            int archetypeCount = world.GetArchetypes().Count;
            
            foreach (var arch in world.GetArchetypes())
                entityCount += arch.Count;
            
            if (indicators.TryGetValue("entities", out var entItem))
                entItem.label.Text = $"{entityCount:N0}";
            
            if (indicators.TryGetValue("archetypes", out var archItem))
                archItem.label.Text = $"{archetypeCount}";
            
            if (indicators.TryGetValue("frametime", out var ftItem))
                ftItem.label.Text = $"{lastFrameTime * 1000:F2}ms";
            
            if (indicators.TryGetValue("fps", out var fpsItem))
            {
                double fps = lastFrameTime > 0 ? 1.0 / lastFrameTime : 0;
                fpsItem.label.Text = $"{fps:F1}";
                
                // Color code FPS
                if (fps >= 55)
                    fpsItem.label.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
                else if (fps >= 30)
                    fpsItem.label.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
                else
                    fpsItem.label.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
            }
            
            // Memory usage
            if (indicators.TryGetValue("memory", out var memItem))
            {
                long memoryBytes = GC.GetTotalMemory(false);
                double memoryMB = memoryBytes / 1024.0 / 1024.0;
                memItem.label.Text = $"{memoryMB:F1} MB";
            }
        }
        
        private void UpdateSystemStatistics()
        {
            if (world == null || systemStatsContainer == null || !advancedStatsEnabled)
                return; // Don't waste time updating if not enabled
            
            // Clear old labels
            foreach (var child in systemStatsContainer.GetChildren())
            {
                child.QueueFree();
            }
            systemStatLabels.Clear();
            
            // Get all systems and their statistics
            var systems = world.Systems.GetAllSystems();
            
            foreach (var system in systems)
            {
                if (!system.IsEnabled) continue;
                
                var stats = system.Statistics;
                
                var hbox = new HBoxContainer();
                
                // System name
                var nameLabel = new Label();
                nameLabel.Text = system.Name;//stats.SystemName;
                nameLabel.CustomMinimumSize = new Vector2(200, 0);
                nameLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
                nameLabel.AddThemeFontSizeOverride("font_size", 11);
                hbox.AddChild(nameLabel);
                
                // Last update time
                var timeLabel = new Label();
                timeLabel.Text = $"{stats.LastUpdateTimeMs:F3}ms";
                timeLabel.CustomMinimumSize = new Vector2(80, 0);
                timeLabel.HorizontalAlignment = HorizontalAlignment.Right;
                timeLabel.AddThemeFontSizeOverride("font_size", 11);
                
                // Color code based on time
                if (stats.LastUpdateTimeMs < 1.0)
                    timeLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
                else if (stats.LastUpdateTimeMs < 5.0)
                    timeLabel.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
                else
                    timeLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
                
                hbox.AddChild(timeLabel);
                
                systemStatsContainer.AddChild(hbox);
                systemStatLabels[system.Name] = timeLabel;
            }
        }
        
        private void UpdateSystemIndicator<T>(string id) where T : BaseSystem
        {
            if (!indicators.TryGetValue(id, out var item) || item.indicator == null)
                return;
                
            bool enabled = world!.Systems.IsSystemEnabled<T>();
            item.indicator.Color = enabled ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);
        }
        
        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                HandleKey(keyEvent.Keycode);
            }
        }
        
        private void HandleKey(Key key)
        {
            switch (key)
            {
                case Key.F1:
                    hudVisible = !hudVisible;
                    Visible = hudVisible;
                    GD.Print($"[ECSControlPanel] HUD {(hudVisible ? "shown" : "hidden")}");
                    break;
                    
                case Key.F2:
                    ToggleSystem<OptimizedPulsingMovementSystem>();
                    break;
                    
                case Key.F3:
                    ToggleSystem<OptimizedMovementSystem>();
                    break;
                    
                case Key.F4:
                    // Toggle whichever render system is present
                    if (world!.Systems.IsSystemEnabled<AdaptiveMultiMeshRenderSystem>())
                        ToggleSystem<AdaptiveMultiMeshRenderSystem>();
                    else
                        ToggleSystem<MultiMeshRenderSystem>();
                    break;
                    
                case Key.F5:
                    StartStressTest(StressTestType.Spawn, StressIntensity.Light);
                    break;
                    
                case Key.F6:
                    StartStressTest(StressTestType.Spawn, StressIntensity.Medium);
                    break;
                    
                case Key.F7:
                    StartStressTest(StressTestType.Spawn, StressIntensity.Heavy);
                    break;
                    
                case Key.F8:
                    StartStressTest(StressTestType.Churn, StressIntensity.Medium);
                    break;
                    
                case Key.F9:
                    testManager?.Stop();
                    GD.Print("[ECSControlPanel] Stress test stopped");
                    break;
                    
                case Key.Escape:
                    GD.Print("[ECSControlPanel] Quitting application");
                    testManager?.OnApplicationQuit();
                    GetTree().Quit();
                    break;
            }
        }
        
        private void ToggleAdvancedStats()
        {
            if (world == null || systemStatsContainer == null || advancedStatsToggle == null)
                return;
            
            advancedStatsEnabled = !advancedStatsEnabled;
            
            // Toggle visibility of system stats
            systemStatsContainer.Visible = advancedStatsEnabled;
            
            // Enable/disable statistics tracking on all systems
            var systems = world.Systems.GetAllSystems();
            foreach (var system in systems)
            {
                system.EnableStatistics = advancedStatsEnabled;
            }
            
            // Update button appearance
            if (advancedStatsEnabled)
            {
                advancedStatsToggle.Text = "✓ System Benchmarks Active";
                
                // Green style for enabled
                var enabledStyle = new StyleBoxFlat();
                enabledStyle.BgColor = new Color(0.3f, 0.8f, 0.3f) with { A = 0.7f };
                enabledStyle.BorderColor = new Color(0.3f, 0.8f, 0.3f);
                enabledStyle.BorderWidthLeft = 2;
                enabledStyle.BorderWidthRight = 2;
                enabledStyle.BorderWidthTop = 2;
                enabledStyle.BorderWidthBottom = 2;
                enabledStyle.CornerRadiusTopLeft = 4;
                enabledStyle.CornerRadiusTopRight = 4;
                enabledStyle.CornerRadiusBottomLeft = 4;
                enabledStyle.CornerRadiusBottomRight = 4;
                advancedStatsToggle.AddThemeStyleboxOverride("normal", enabledStyle);
                
                GD.Print("[ECSControlPanel] ⚠ Advanced statistics ENABLED - Performance impact: 10-25%");
            }
            else
            {
                advancedStatsToggle.Text = "Enable System Benchmarks";
                
                // Gray style for disabled
                var disabledStyle = new StyleBoxFlat();
                disabledStyle.BgColor = new Color(0.5f, 0.5f, 0.5f) with { A = 0.7f };
                disabledStyle.BorderColor = new Color(0.5f, 0.5f, 0.5f);
                disabledStyle.BorderWidthLeft = 2;
                disabledStyle.BorderWidthRight = 2;
                disabledStyle.BorderWidthTop = 2;
                disabledStyle.BorderWidthBottom = 2;
                disabledStyle.CornerRadiusTopLeft = 4;
                disabledStyle.CornerRadiusTopRight = 4;
                disabledStyle.CornerRadiusBottomLeft = 4;
                disabledStyle.CornerRadiusBottomRight = 4;
                advancedStatsToggle.AddThemeStyleboxOverride("normal", disabledStyle);
                
                GD.Print("[ECSControlPanel] Advanced statistics DISABLED - Performance restored");
            }
        }
        
        private void ToggleSystem<T>() where T : BaseSystem
        {
            if (world == null) return;
            
            if (world.Systems.IsSystemEnabled<T>())
            {
                world.Systems.DisableSystem<T>();
                GD.Print($"[ECSControlPanel] Disabled {typeof(T).Name}");
            }
            else
            {
                world.Systems.EnableSystem<T>();
                GD.Print($"[ECSControlPanel] Enabled {typeof(T).Name}");
            }
        }
        
        private void StartStressTest(StressTestType type, StressIntensity intensity)
        {
            if (world == null || testManager == null) return;
            
            if (testManager.IsRunning)
            {
                GD.PrintErr("[ECSControlPanel] A stress test is already running!");
                return;
            }
            
            var config = StressTestConfig.CreatePreset(type, intensity);
            
            StressTestModule test = type switch
            {
                StressTestType.Spawn => new SpawnStressTest(world, config),
                StressTestType.Churn => new ChurnStressTest(world, config),
                StressTestType.Archetype => new ArchetypeStressTest(world, config),
                _ => throw new NotImplementedException($"Test type {type} not implemented")
            };
            
            testManager.QueueTest(test);
            testManager.Start();
            
            GD.Print($"[ECSControlPanel] Started {type} test at {intensity} intensity");
        }
    }
}