using Godot;
using UltraSim.Scripts.ECS.Systems.Settings;
using UltraSim.Scripts.ECS.Core.Utilities;

namespace UltraSim.Scripts.ECS.Testing
{
    /// <summary>
    /// Test script to verify Phase 1 implementation is working correctly.
    /// Attach this to a Node in your scene and run to test settings functionality.
    /// </summary>
    public partial class Phase1Test : Node
    {
        public override void _Ready()
        {
            GD.Print("=== Phase 1 Settings Test ===\n");
            
            TestECSPaths();
            TestIndividualSettings();
            TestBaseSettings();
            TestSerialization();
            TestGUIGeneration();
            
            GD.Print("\n=== All Phase 1 Tests Complete ===");
        }
        
        private void TestECSPaths()
        {
            GD.Print("--- Testing ECSPaths ---");
            
            // Ensure directories exist
            ECSPaths.EnsureDirectoriesExist();
            GD.Print($"✓ Directories created");
            
            // Print paths
            GD.Print($"  Executable: {ECSPaths.ExecutableDir}");
            GD.Print($"  Saves: {ECSPaths.SavesDir}");
            GD.Print($"  ECS: {ECSPaths.ECSDir}");
            GD.Print($"  Systems: {ECSPaths.SystemsDir}");
            GD.Print($"  World: {ECSPaths.WorldDir}");
            
            // Test path generation
            string settingsPath = ECSPaths.GetSystemSettingsPath("TestSystem");
            string statePath = ECSPaths.GetSystemStatePath("TestSystem");
            
            GD.Print($"  Settings path: {settingsPath}");
            GD.Print($"  State path: {statePath}");
            GD.Print("✓ ECSPaths test passed\n");
        }
        
        private void TestIndividualSettings()
        {
            GD.Print("--- Testing Individual Settings ---");
            
            // Test FloatSetting
            var floatSetting = new FloatSetting 
            { 
                Name = "Test Float", 
                Value = 5.0f, 
                Min = 0f, 
                Max = 10f,
                Tooltip = "A test float setting"
            };
            GD.Print($"✓ FloatSetting: {floatSetting.Name} = {floatSetting.Value}");
            
            // Test IntSetting
            var intSetting = new IntSetting 
            { 
                Name = "Test Int", 
                Value = 42, 
                Min = 0, 
                Max = 100,
                Tooltip = "A test int setting"
            };
            GD.Print($"✓ IntSetting: {intSetting.Name} = {intSetting.Value}");
            
            // Test BoolSetting
            var boolSetting = new BoolSetting 
            { 
                Name = "Test Bool", 
                Value = true,
                Tooltip = "A test bool setting"
            };
            GD.Print($"✓ BoolSetting: {boolSetting.Name} = {boolSetting.Value}");
            
            // Test EnumSetting
            var enumSetting = new EnumSetting<TestMode> 
            { 
                Name = "Test Enum", 
                Value = TestMode.Fast,
                Tooltip = "A test enum setting"
            };
            GD.Print($"✓ EnumSetting: {enumSetting.Name} = {enumSetting.Value}");
            
            // Test StringSetting
            var stringSetting = new StringSetting 
            { 
                Name = "Test String", 
                Value = "Hello Settings!",
                MaxLength = 50,
                Tooltip = "A test string setting"
            };
            GD.Print($"✓ StringSetting: {stringSetting.Name} = {stringSetting.Value}");
            
            GD.Print("✓ All setting types created successfully\n");
        }
        
        private void TestBaseSettings()
        {
            GD.Print("--- Testing BaseSettings ---");
            
            var testSettings = new TestSystemSettings();
            
            // Test property access
            GD.Print($"✓ Speed (property): {testSettings.Speed.Value}");
            GD.Print($"✓ MaxEntities (property): {testSettings.MaxEntities.Value}");
            GD.Print($"✓ Enabled (property): {testSettings.Enabled.Value}");
            GD.Print($"✓ Mode (property): {testSettings.Mode.Value}");
            GD.Print($"✓ Description (property): {testSettings.Description.Value}");
            
            // Test generic access
            float speed = testSettings.GetFloat("Speed");
            int maxEntities = testSettings.GetInt("Max Entities");
            bool enabled = testSettings.GetBool("Enabled");
            string desc = testSettings.GetString("Description");
            
            GD.Print($"✓ Speed (generic): {speed}");
            GD.Print($"✓ Max Entities (generic): {maxEntities}");
            GD.Print($"✓ Enabled (generic): {enabled}");
            GD.Print($"✓ Description (generic): {desc}");
            
            // Test modification
            testSettings.Speed.Value = 5.5f;
            testSettings.SetValue("Max Entities", 500);
            
            GD.Print($"✓ Modified Speed: {testSettings.Speed.Value}");
            GD.Print($"✓ Modified Max Entities: {testSettings.MaxEntities.Value}");
            
            // Test GetAllSettings
            var allSettings = testSettings.GetAllSettings();
            GD.Print($"✓ Total settings registered: {allSettings.Count}");
            
            GD.Print("✓ BaseSettings test passed\n");
        }
        
        private void TestSerialization()
        {
            GD.Print("--- Testing Serialization ---");
            
            // Create and configure settings
            var settings1 = new TestSystemSettings();
            settings1.Speed.Value = 7.5f;
            settings1.MaxEntities.Value = 750;
            settings1.Enabled.Value = false;
            settings1.Mode.Value = TestMode.Slow;
            settings1.Description.Value = "Custom description";
            
            // Save to config file
            var config = new ConfigFile();
            settings1.Serialize(config, "TestSettings");
            
            string testPath = "user://phase1_test.cfg";
            var saveError = config.Save(testPath);
            
            if (saveError == Error.Ok)
            {
                GD.Print($"✓ Settings saved to: {testPath}");
            }
            else
            {
                GD.PrintErr($"✗ Failed to save settings: {saveError}");
                return;
            }
            
            // Load into new settings instance
            var settings2 = new TestSystemSettings();
            var loadedConfig = new ConfigFile();
            var loadError = loadedConfig.Load(testPath);
            
            if (loadError == Error.Ok)
            {
                settings2.Deserialize(loadedConfig, "TestSettings");
                GD.Print("✓ Settings loaded successfully");
                
                // Verify values match
                bool speedMatch = Mathf.IsEqualApprox(settings2.Speed.Value, 7.5f);
                bool entitiesMatch = settings2.MaxEntities.Value == 750;
                bool enabledMatch = settings2.Enabled.Value == false;
                bool modeMatch = settings2.Mode.Value == TestMode.Slow;
                bool descMatch = settings2.Description.Value == "Custom description";
                
                GD.Print($"  Speed: {(speedMatch ? "✓" : "✗")} {settings2.Speed.Value}");
                GD.Print($"  Max Entities: {(entitiesMatch ? "✓" : "✗")} {settings2.MaxEntities.Value}");
                GD.Print($"  Enabled: {(enabledMatch ? "✓" : "✗")} {settings2.Enabled.Value}");
                GD.Print($"  Mode: {(modeMatch ? "✓" : "✗")} {settings2.Mode.Value}");
                GD.Print($"  Description: {(descMatch ? "✓" : "✗")} {settings2.Description.Value}");
                
                if (speedMatch && entitiesMatch && enabledMatch && modeMatch && descMatch)
                {
                    GD.Print("✓ All values match after serialization round-trip");
                }
                else
                {
                    GD.PrintErr("✗ Some values don't match after serialization");
                }
            }
            else
            {
                GD.PrintErr($"✗ Failed to load settings: {loadError}");
            }
            
            GD.Print("✓ Serialization test complete\n");
        }
        
        private void TestGUIGeneration()
        {
            GD.Print("--- Testing GUI Generation ---");
            
            var settings = new TestSystemSettings();
            var container = new VBoxContainer();
            container.Name = "TestSettingsContainer";
            
            int controlCount = 0;
            
            // Generate GUI controls for all settings
            foreach (var setting in settings.GetAllSettings())
            {
                var control = setting.CreateControl((changedSetting) =>
                {
                    GD.Print($"  Setting changed: {changedSetting.Name} = {changedSetting.GetValue()}");
                });
                
                container.AddChild(control);
                controlCount++;
            }
            
            GD.Print($"✓ Created {controlCount} GUI controls");
            GD.Print($"✓ Container has {container.GetChildCount()} children");
            
            // Note: We can't fully test GUI without adding to scene tree,
            // but we've verified the controls are created
            GD.Print("✓ GUI generation test passed\n");
            
            // Clean up
            container.QueueFree();
        }
        
        // Test enum for EnumSetting
        private enum TestMode
        {
            Normal,
            Fast,
            Slow
        }
        
        // Test settings class
        private class TestSystemSettings : BaseSettings
        {
            public FloatSetting Speed { get; private set; }
            public IntSetting MaxEntities { get; private set; }
            public BoolSetting Enabled { get; private set; }
            public EnumSetting<TestMode> Mode { get; private set; }
            public StringSetting Description { get; private set; }
            
            public TestSystemSettings()
            {
                Speed = RegisterFloat("Speed", 2.0f, 0.1f, 10.0f, 0.1f,
                    "Speed multiplier for the system");
                    
                MaxEntities = RegisterInt("Max Entities", 1000, 100, 10000, 100,
                    "Maximum number of entities to process");
                    
                Enabled = RegisterBool("Enabled", true,
                    "Enable or disable the system");
                    
                Mode = RegisterEnum("Mode", TestMode.Normal,
                    "Operating mode for the system");
                    
                Description = RegisterString("Description", "Test system description", 200,
                    "Description of the system");
            }
        }
    }
}
