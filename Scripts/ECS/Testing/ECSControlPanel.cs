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
    /// Enhanced ECS Control Panel with TICK SCHEDULING visualization.
    /// 
    /// NEW FEATURES:
    /// - Shows tick rate for each system
    /// - Visual indicators for systems that ran this frame (blinks green)
    /// - Manual system invocation via hotkeys (F10, F11, F12)
    /// - Tick scheduling diagnostics
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
        
        // NEW: Tick scheduling visualization
        private VBoxContainer? tickSchedulingContainer;
        private Dictionary<string, (Label nameLabel, Label rateLabel, ColorRect indicator, double lastBlinkTime)> tickSystemIndicators = new();
        private bool tickSchedulingVisible = false;
        private Button? tickSchedulingToggle;

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
            GD.Print("[ECSControlPanel] NEW: Press F10 to invoke Manual systems");
            GD.Print("[ECSControlPanel] NEW: Press F11 to trigger SaveSystem");
        }
        
        private void BuildUI()
        {
            // Larger panel to accommodate tick scheduling section
            SetAnchorsPreset(LayoutPreset.TopRight);
            SetOffset(Side.Left, -550); // Wider panel
            SetOffset(Side.Right, -10);
            SetOffset(Side.Top, 10);
            SetOffset(Side.Bottom, 900); // Taller panel
            
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
            title.Text = $"â•â•â•â• ECS CONTROL PANEL â•â•â•â•—";
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
            
            // NEW: Tick Scheduling section
            AddSection("TICK SCHEDULING");
            
            // Info label
            var tickInfoLabel = new Label();
            tickInfoLabel.Text = "Shows which systems ran this frame (blinks green)";
            tickInfoLabel.AddThemeFontSizeOverride("font_size", 11);
            tickInfoLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            container.AddChild(tickInfoLabel);
            
            AddSpacer(3);
            
            // Toggle button
            tickSchedulingToggle = CreateStyledButton("Show Tick Scheduling", new Color(0.5f, 0.5f, 0.5f));
            tickSchedulingToggle.Pressed += () => ToggleTickScheduling();
            container.AddChild(tickSchedulingToggle);
            
            AddSpacer(10);
            
            // Tick scheduling container (hidden by default)
            tickSchedulingContainer = new VBoxContainer();
            tickSchedulingContainer.Visible = false;
            container.AddChild(tickSchedulingContainer);
            
            AddSpacer(10);
            
            // Advanced Statistics toggle section
            AddSection("ADVANCED STATISTICS");
            
            // Warning label
            var warningLabel = new Label();
            warningLabel.Text = "âš  Enabling will cause 10-25% performance hit!";
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
            systemStatsContainer.Visible = false;
            container.AddChild(systemStatsContainer);
            
            AddSpacer(10);
            
            // Manual System Controls
            AddSection("MANUAL SYSTEMS");
            AddInfo("F10", "Run Manual Test", null);
            AddInfo("F11", "Trigger Save System", null);
            AddInfo("F12", "Show Tick Diagnostics", null);
            
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
            
            // Infinity spawn button
            var infinityButton = CreateStyledButton("âˆž Until FPS Low (â‰¤29)", new Color(1f, 0.3f, 0.3f));
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
        }
        
        // ... (rest of the helper methods remain the same) ...
        
        private void AddSection(string title)
        {
            var label = new Label();
            label.Text = $"""â"€â"€â"€ {title} â"€â"€â"€""";
            label.AddThemeFontSizeOverride("font_size", 14);
            label.AddThemeColorOverride("font_color", new Color(0.3f, 0.7f, 1f));
            container!.AddChild(label);
        }
        
        private void AddToggle(string key, string desc, string id)
        {
            var hbox = new HBoxContainer();
            
            var keyLabel = new Label();
            keyLabel.Text = $"[{key}]";
            keyLabel.CustomMinimumSize = new Vector2(50, 0);
            keyLabel.AddThemeFontSizeOverride("font_size", 12);
            hbox.AddChild(keyLabel);
            
            var descLabel = new Label();
            descLabel.Text = desc;
            descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            descLabel.AddThemeFontSizeOverride("font_size", 12);
            hbox.AddChild(descLabel);
            
            var indicator = new ColorRect();
            indicator.CustomMinimumSize = new Vector2(12, 12);
            indicator.Color = new Color(1f, 0.3f, 0.3f);
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
                keyLabel.AddThemeFontSizeOverride("font_size", 12);
                hbox.AddChild(keyLabel);
            }
            else
            {
                // Spacer for alignment
                var spacer = new Control();
                spacer.CustomMinimumSize = new Vector2(50, 0);
                hbox.AddChild(spacer);
            }
            
            var descLabel = new Label();
            descLabel.Text = desc;
            descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            descLabel.AddThemeFontSizeOverride("font_size", 12);
            hbox.AddChild(descLabel);
            
            if (id != null)
            {
                var valueLabel = new Label();
                valueLabel.CustomMinimumSize = new Vector2(120, 0);
                valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
                valueLabel.AddThemeFontSizeOverride("font_size", 12);
                hbox.AddChild(valueLabel);
                
                indicators[id] = (valueLabel, null!);
            }
            
            container!.AddChild(hbox);
        }
        
        private void AddSpacer(int height)
        {
            var spacer = new Control();
            spacer.CustomMinimumSize = new Vector2(0, height);
            container!.AddChild(spacer);
        }
        
        private Button CreateStyledButton(string text, Color color)
        {
            var button = new Button();
            button.Text = text;
            button.CustomMinimumSize = new Vector2(0, 30);
            button.AddThemeFontSizeOverride("font_size", 12);
            
            var styleBox = new StyleBoxFlat();
            styleBox.BgColor = color with { A = 0.7f };
            styleBox.BorderColor = color;
            styleBox.BorderWidthLeft = 2;
            styleBox.BorderWidthRight = 2;
            styleBox.BorderWidthTop = 2;
            styleBox.BorderWidthBottom = 2;
            styleBox.CornerRadiusTopLeft = 4;
            styleBox.CornerRadiusTopRight = 4;
            styleBox.CornerRadiusBottomLeft = 4;
            styleBox.CornerRadiusBottomRight = 4;
            button.AddThemeStyleboxOverride("normal", styleBox);
            
            return button;
        }
        
        private void AddSpawnButton(GridContainer grid, string label, int count)
        {
            var button = CreateStyledButton(label, new Color(0.3f, 0.8f, 0.3f));
            button.Pressed += () => SpawnEntities(count);
            grid.AddChild(button);
        }
        
        public override void _Process(double delta)
        {
            if (!hudVisible) return;
            
            lastFrameTime = delta;
            frameCount++;
            
            timeSinceUpdate += (float)delta;
            if (timeSinceUpdate >= updateInterval)
            {
                timeSinceUpdate = 0f;
                UpdateIndicators();
            }
            
            // NEW: Update tick scheduling indicators every frame
            if (tickSchedulingVisible)
            {
                UpdateTickSchedulingIndicators();
            }
        }
        
        private void UpdateIndicators()
        {
            if (world == null) return;
            
            // Update system toggles
            UpdateSystemIndicator<OptimizedPulsingMovementSystem>("pulsing");
            UpdateSystemIndicator<OptimizedMovementSystem>("movement");
            
            // Update render system (check which one exists)
            if (world.Systems.HasSystem<AdaptiveMultiMeshRenderSystem>())
                UpdateSystemIndicator<AdaptiveMultiMeshRenderSystem>("render");
            else if (world.Systems.HasSystem<MultiMeshRenderSystem>())
                UpdateSystemIndicator<MultiMeshRenderSystem>("render");
            
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
                if (fps >= 60)
                    fpsItem.label.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
                else if (fps >= 30)
                    fpsItem.label.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
                else
                    fpsItem.label.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
            }
            
            if (indicators.TryGetValue("memory", out var memItem))
            {
                long memBytes = GC.GetTotalMemory(false);
                double memMB = memBytes / (1024.0 * 1024.0);
                memItem.label.Text = $"{memMB:F1} MB";
            }
            
            // Update system performance stats if enabled
            if (advancedStatsEnabled)
            {
                UpdateSystemStatistics();
            }
        }
        
        // NEW: Tick scheduling visualization
        private void ToggleTickScheduling()
        {
            if (world == null || tickSchedulingContainer == null || tickSchedulingToggle == null)
                return;
            
            tickSchedulingVisible = !tickSchedulingVisible;
            tickSchedulingContainer.Visible = tickSchedulingVisible;
            
            if (tickSchedulingVisible)
            {
                tickSchedulingToggle.Text = $"""âœ" Tick Scheduling Active""";
                
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
                tickSchedulingToggle.AddThemeStyleboxOverride("normal", enabledStyle);
                
                RebuildTickSchedulingUI();
                GD.Print("[ECSControlPanel] Tick scheduling visualization ENABLED");
            }
            else
            {
                tickSchedulingToggle.Text = "Show Tick Scheduling";
                
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
                tickSchedulingToggle.AddThemeStyleboxOverride("normal", disabledStyle);
                
                GD.Print("[ECSControlPanel] Tick scheduling visualization DISABLED");
            }
        }
        
        // NEW: Build tick scheduling UI
        private void RebuildTickSchedulingUI()
        {
            if (world == null || tickSchedulingContainer == null)
                return;
            
            // Clear existing
            foreach (var child in tickSchedulingContainer.GetChildren())
            {
                tickSchedulingContainer.RemoveChild(child);
                child.QueueFree();
            }
            tickSystemIndicators.Clear();
            
            // Get all systems
            var systems = world.Systems.GetAllSystems();
            
            // Group by tick rate
            var grouped = systems.GroupBy(s => s.Rate).OrderBy(g => (ushort)g.Key);
            
            foreach (var group in grouped)
            {
                var rate = group.Key;
                
                // Group header
                var headerLabel = new Label();
                headerLabel.Text = $"""â"€â"€ {rate} ({rate.ToFrequencyString()}) â"€â"€""";
                headerLabel.AddThemeFontSizeOverride("font_size", 11);
                headerLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 1f));
                tickSchedulingContainer.AddChild(headerLabel);
                
                // Systems in this rate group
                foreach (var system in group)
                {
                    var hbox = new HBoxContainer();
                    
                    // System name
                    var nameLabel = new Label();
                    nameLabel.Text = system.Name;
                    nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    nameLabel.AddThemeFontSizeOverride("font_size", 11);
                    hbox.AddChild(nameLabel);
                    
                    // Rate display
                    var rateLabel = new Label();
                    rateLabel.Text = rate.ToFrequencyString();
                    rateLabel.CustomMinimumSize = new Vector2(80, 0);
                    rateLabel.HorizontalAlignment = HorizontalAlignment.Right;
                    rateLabel.AddThemeFontSizeOverride("font_size", 10);
                    rateLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
                    hbox.AddChild(rateLabel);
                    
                    // Activity indicator (blinks when system runs)
                    var indicator = new ColorRect();
                    indicator.CustomMinimumSize = new Vector2(12, 12);
                    indicator.Color = new Color(0.3f, 0.3f, 0.3f); // Gray = inactive
                    hbox.AddChild(indicator);
                    
                    tickSchedulingContainer.AddChild(hbox);
                    
                    tickSystemIndicators[system.Name] = (nameLabel, rateLabel, indicator, 0);
                }
                
                // Spacer between groups
                var spacer = new Control();
                spacer.CustomMinimumSize = new Vector2(0, 5);
                tickSchedulingContainer.AddChild(spacer);
            }
        }
        
        // NEW: Update tick scheduling indicators (called every frame)
        private void UpdateTickSchedulingIndicators()
        {
            if (world == null)
                return;
            
            double currentTime = Time.GetTicksMsec() / 1000.0;
            
            var systems = world.Systems.GetAllSystems();
            foreach (var system in systems)
            {
                if (!tickSystemIndicators.TryGetValue(system.Name, out var indicator))
                    continue;
                
                // Check if system just ran by looking at statistics
                var stats = system.Statistics;
                bool justRan = stats.IsEnabled && stats.UpdateCount > 0;
                
                if (justRan)
                {
                    // Store the time when we detected the system ran
                    var prevTime = indicator.lastBlinkTime;
                    var newTime = currentTime;
                    
                    // If system ran recently (within last 0.1s), show green
                    if (newTime - prevTime > 0.016) // Only update if frame changed
                    {
                        indicator.indicator.Color = new Color(0.3f, 1f, 0.3f); // Green = just ran
                        tickSystemIndicators[system.Name] = (indicator.nameLabel, indicator.rateLabel, indicator.indicator, newTime);
                    }
                }
                else
                {
                    // Fade back to gray
                    double timeSinceRun = currentTime - indicator.lastBlinkTime;
                    if (timeSinceRun > 0.2) // Fade after 200ms
                    {
                        indicator.indicator.Color = new Color(0.3f, 0.3f, 0.3f); // Gray = inactive
                    }
                }
            }
        }
        
        // ... (rest of methods continue: UpdateSystemStatistics, HandleKey, etc.) ...
        
        private void UpdateSystemStatistics()
        {
            if (world == null || systemStatsContainer == null || !systemStatsContainer.Visible)
                return;
            
            // Rebuild system stats if structure changed
            var systems = world.Systems.GetAllSystems();
            if (systems.Count != systemStatLabels.Count)
            {
                RebuildSystemStatistics();
                return;
            }
            
            // Update existing labels
            foreach (var system in systems)
            {
                var stats = system.Statistics;
                if (!systemStatLabels.TryGetValue(stats.SystemName, out var timeLabel))
                    continue;
                
                timeLabel.Text = $"{stats.LastUpdateTimeMs:F3}ms";
                
                // Color code based on time
                if (stats.LastUpdateTimeMs < 1.0)
                    timeLabel.AddThemeColorOverride("font_color", new Color(0.3f, 1f, 0.3f));
                else if (stats.LastUpdateTimeMs < 5.0)
                    timeLabel.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
                else
                    timeLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
            }
        }
        
        private void RebuildSystemStatistics()
        {
            if (world == null || systemStatsContainer == null)
                return;
            
            // Clear existing
            foreach (var child in systemStatsContainer.GetChildren())
            {
                systemStatsContainer.RemoveChild(child);
                child.QueueFree();
            }
            systemStatLabels.Clear();
            
            // Rebuild
            var systems = world.Systems.GetAllSystems();
            foreach (var system in systems)
            {
                var stats = system.Statistics;
                
                var hbox = new HBoxContainer();
                
                // System name
                var nameLabel = new Label();
                nameLabel.Text = stats.SystemName;
                nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
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
                systemStatLabels[stats.SystemName] = timeLabel;
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
                    if (world!.Systems.HasSystem<AdaptiveMultiMeshRenderSystem>())
                        ToggleSystem<AdaptiveMultiMeshRenderSystem>();
                    else if (world!.Systems.HasSystem<MultiMeshRenderSystem>())
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
                    
                // NEW: Manual system invocations
                case Key.F10:
                    InvokeManualSystem<ManualTestSystem>();
                    break;
                    
                case Key.F11:
                    InvokeManualSystem<SaveSystem>();
                    break;
                    
                case Key.F12:
                    PrintTickSchedulingDiagnostics();
                    break;
                    
                case Key.Escape:
                    GD.Print("[ECSControlPanel] Quitting application");
                    testManager?.OnApplicationQuit();
                    GetTree().Quit();
                    break;
            }
        }
        
        // NEW: Manual system invocation
        private void InvokeManualSystem<T>() where T : BaseSystem
        {
            if (world == null) return;
            
            var system = world.Systems.GetSystem<T>();
            if (system == null)
            {
                GD.PrintErr($"[ECSControlPanel] System {typeof(T).Name} not found!");
                return;
            }
            
            if (system.Rate != TickRate.Manual)
            {
                GD.PrintErr($"[ECSControlPanel] System {typeof(T).Name} is not a Manual system!");
                return;
            }
            
            GD.Print($"[ECSControlPanel] â–¶ï¸ Manually invoking {typeof(T).Name}...");
            world.Systems.RunManual(world, system);
        }
        
        // NEW: Print tick scheduling diagnostics
        private void PrintTickSchedulingDiagnostics()
        {
            if (world == null) return;
            
            string info = world.Systems.GetTickSchedulingInfo();
            GD.Print(info);
        }
        
        // ... (remaining methods: ToggleAdvancedStats, ToggleSystem, StartStressTest, SpawnEntities, SpawnUntilFPSLow)
        
        private void ToggleAdvancedStats()
        {
            if (world == null || systemStatsContainer == null || advancedStatsToggle == null)
                return;
            
            advancedStatsEnabled = !advancedStatsEnabled;
            systemStatsContainer.Visible = advancedStatsEnabled;
            
            var systems = world.Systems.GetAllSystems();
            foreach (var system in systems)
            {
                system.EnableStatistics = advancedStatsEnabled;
            }
            
            if (advancedStatsEnabled)
            {
                advancedStatsToggle.Text = $"""âœ" System Benchmarks Active""";
                
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
                
                GD.Print("[ECSControlPanel] âš  Advanced statistics ENABLED - Performance impact: 10-25%");
            }
            else
            {
                advancedStatsToggle.Text = "Enable System Benchmarks";
                
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
        
        private void SpawnEntities(int count)
        {
            if (world == null) return;
            
            GD.Print($"[ECSControlPanel] Spawning {count:N0} entities...");
            
            var builder = new EntityBuilder();
            for (int i = 0; i < count; i++)
            {
                float x = (float)(GD.Randf() * 200 - 100);
                float y = (float)(GD.Randf() * 200 - 100);
                float z = (float)(GD.Randf() * 200 - 100);

                builder
                    //.With(new Position { X = x, Y = y, Z = z })
                    //.With(new Velocity { X = 0, Y = 0, Z = 0 })
                    //.Create();
                    .Add<Position>(new Position { X = x, Y = y, Z = z })
                    .Add<Velocity>(new Velocity { X = 0, Y = 0, Z = 0 });
            }
            
            GD.Print($"[ECSControlPanel] âœ… Spawned {count:N0} entities");
        }
        
        private void SpawnUntilFPSLow()
        {
            if (world == null) return;
            
            GD.Print("[ECSControlPanel] âˆž Starting infinite spawn (stops at FPS â‰¤ 29)...");
            
            // Start spawn stress test with UntilCrash intensity
            StartStressTest(StressTestType.Spawn, StressIntensity.UntilCrash);
        }
    }
}