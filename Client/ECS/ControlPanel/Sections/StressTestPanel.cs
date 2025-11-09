#nullable enable

using Godot;
using UltraSim;
using UltraSim.ECS;
using Client.ECS.StressTests;
using Client.UI;

namespace Client.ECS.ControlPanel.Sections
{
    /// <summary>
    /// Control panel section for running ECS stress tests.
    /// Allows running spawn, churn, and archetype transition tests at different intensities.
    /// </summary>
    [ControlPanelSection(defaultOrder: 20, defaultExpanded: false)]
    public partial class StressTestPanel : UIBuilder, IControlPanelSection
    {
        private World _world = null!;
        private TestManager? _testManager;

        // UI Controls
        private Label? _statusLabel;
        private Label? _resultsLabel;
        private Button? _stopButton;

        public string Title => "Stress Tests";
        public string Id => "stress_test_panel";
        public bool IsExpanded { get; set; }

        public StressTestPanel(World world)
        {
            _world = world;
            _testManager = new TestManager(world);
        }

        public Control? CreateHeaderButtons()
        {
            return null;
        }

        public Control CreateUI()
        {
            var mainVBox = CreateVBox(separation: 12);

            // === INFO SECTION ===
            var infoLabel = CreateLabel(
                "Run automated stress tests to validate ECS performance.\n" +
                "Tests create/destroy entities and measure frame times.\n" +
                "Results are exported to CSV in Saves/StressTests/",
                fontSize: 11,
                color: new Color(0.7f, 0.7f, 0.7f)
            );
            mainVBox.AddChild(infoLabel);

            mainVBox.AddChild(new HSeparator());

            // === STATUS SECTION ===
            _statusLabel = CreateLabel("Status: Ready", fontSize: 12, color: new Color(0.5f, 1.0f, 0.5f));
            mainVBox.AddChild(_statusLabel);

            _resultsLabel = CreateLabel("No tests run yet", fontSize: 11, color: new Color(0.7f, 0.7f, 0.7f));
            mainVBox.AddChild(_resultsLabel);

            // Stop button
            _stopButton = CreateButton("Stop Current Test", OnStopPressed);
            _stopButton.Visible = false;
            mainVBox.AddChild(_stopButton);

            mainVBox.AddChild(new HSeparator());

            // === TEST BUTTONS ===
            CreateTestSection(mainVBox, "Entity Spawn Test", "Tests pure entity creation throughput", StressTestType.Spawn);
            CreateTestSection(mainVBox, "Entity Churn Test", "Tests create/destroy cycle efficiency", StressTestType.Churn);
            CreateTestSection(mainVBox, "Archetype Transition Test", "Tests component add/remove performance", StressTestType.Archetype);

            return mainVBox;
        }

        private void CreateTestSection(VBoxContainer parent, string title, string description, StressTestType testType)
        {
            // Section label
            var sectionLabel = CreateLabel(title, fontSize: 13);
            parent.AddChild(sectionLabel);

            // Description
            var descLabel = CreateLabel(description, fontSize: 10, color: new Color(0.6f, 0.6f, 0.6f));
            parent.AddChild(descLabel);

            // Intensity buttons in horizontal layout
            var buttonHBox = CreateHBox(separation: 8);
            parent.AddChild(buttonHBox);

            CreateIntensityButton(buttonHBox, "Light (1K)", testType, StressIntensity.Light);
            CreateIntensityButton(buttonHBox, "Medium (10K)", testType, StressIntensity.Medium);
            CreateIntensityButton(buttonHBox, "Heavy (50K)", testType, StressIntensity.Heavy);
            CreateIntensityButton(buttonHBox, "Extreme (100K)", testType, StressIntensity.Extreme);

            parent.AddChild(new HSeparator());
        }

        private void CreateIntensityButton(HBoxContainer parent, string text, StressTestType testType, StressIntensity intensity)
        {
            var button = CreateButton(text, () => OnTestButtonPressed(testType, intensity));
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            parent.AddChild(button);
        }

        private void OnTestButtonPressed(StressTestType testType, StressIntensity intensity)
        {
            if (_testManager == null || _testManager.IsRunning)
            {
                Logging.Log("Test already running", LogSeverity.Warning);
                return;
            }

            // Queue the test based on type
            var config = StressTestConfig.CreatePreset(testType, intensity);

            StressTestBase? test = testType switch
            {
                StressTestType.Spawn => new EntitySpawnTest(_world, config),
                StressTestType.Churn => new EntityChurnTest(_world, config),
                StressTestType.Archetype => new ArchetypeTransitionTest(_world, config),
                _ => null
            };

            if (test != null)
            {
                _testManager.QueueTest(test);
                _testManager.Start();
                UpdateStatusUI();
            }
        }

        private void OnStopPressed()
        {
            _testManager?.Stop();
            UpdateStatusUI();
        }

        public void Update(double delta)
        {
            if (_testManager == null) return;

            _testManager.Update((float)delta);
            UpdateStatusUI();
        }

        private void UpdateStatusUI()
        {
            if (_testManager == null) return;

            if (_testManager.IsRunning)
            {
                var test = _testManager.CurrentTest;
                if (_statusLabel != null && test != null)
                {
                    _statusLabel.Text = $"Status: Running {test.TestName}...";
                    _statusLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.3f));
                }

                if (_stopButton != null)
                    _stopButton.Visible = true;
            }
            else
            {
                if (_statusLabel != null)
                {
                    _statusLabel.Text = "Status: Ready";
                    _statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1.0f, 0.5f));
                }

                if (_stopButton != null)
                    _stopButton.Visible = false;
            }

            // Update results
            if (_resultsLabel != null && _testManager.CompletedResults.Count > 0)
            {
                int passed = 0;
                int failed = 0;
                foreach (var result in _testManager.CompletedResults)
                {
                    if (result.Crashed) failed++; else passed++;
                }

                _resultsLabel.Text = $"Tests completed: {_testManager.CompletedResults.Count} (Passed: {passed}, Failed: {failed})";
            }
        }

        public void OnShow()
        {
            UpdateStatusUI();
        }

        public void OnHide()
        {
            // Nothing to do
        }
    }
}


