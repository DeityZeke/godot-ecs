#nullable enable

using System;
using System.IO;
using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// World persistence - coordinates save/load across all managers.
    /// Each manager is responsible for saving its own data.
    /// </summary>
    public sealed partial class World
    {
        #region Events

        public event Action? OnBeforeSave;
        public event Action? OnAfterSave;
        public event Action? OnBeforeLoad;
        public event Action? OnAfterLoad;

        #endregion

        #region Paths

        /// <summary>
        /// Path management for save files.
        /// </summary>
        public static class Paths
        {
            public static string ExecutableDir => OS.GetExecutablePath().GetBaseDir();
            private static bool IsRunningInEditor => OS.HasFeature("editor");
            private static string BaseDir => IsRunningInEditor ? ProjectSettings.GlobalizePath("user://") : ExecutableDir;
            
            public static string SavesDir => Path.Combine(BaseDir, "Saves");
            public static string ECSDir => Path.Combine(SavesDir, "ECS");
            public static string SystemsDir => Path.Combine(ECSDir, "Systems");
            public static string WorldDir => Path.Combine(ECSDir, "World");
            
            public static string MasterConfigPath => Path.Combine(ECSDir, "ecs_master.cfg");

            public static void EnsureDirectoriesExist()
            {
                Directory.CreateDirectory(SavesDir);
                Directory.CreateDirectory(ECSDir);
                Directory.CreateDirectory(SystemsDir);
                Directory.CreateDirectory(WorldDir);
            }

            public static string GetSystemDirectory(string systemName)
            {
                var systemDir = Path.Combine(SystemsDir, systemName);
                Directory.CreateDirectory(systemDir);
                return systemDir;
            }

            public static string GetSystemSettingsPath(string systemName)
            {
                var systemDir = GetSystemDirectory(systemName);
                return Path.Combine(systemDir, $"{systemName}.settings.cfg");
            }

            public static string GetSystemStatePath(string systemName)
            {
                var systemDir = GetSystemDirectory(systemName);
                return Path.Combine(systemDir, $"{systemName}.sav");
            }
        }

        #endregion

        #region Save

        /// <summary>
        /// Saves the entire world state to a file.
        /// Coordinates saves across all managers.
        /// </summary>
        /// <param name="filename">Filename relative to World save directory (e.g., "quicksave.sav")</param>
        public void Save(string filename = "world.sav")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            OnBeforeSave?.Invoke();
            
            try
            {
                Paths.EnsureDirectoriesExist();
                
                var savePath = Path.Combine(Paths.WorldDir, filename);
                var config = new ConfigFile();

                // Save World's own state
                SaveWorldState(config);

                // TODO Phase 5: When managers are extracted, add:
                // _entities.Save(config, "Entities");
                // _components.Save(config, "Components");
                // _archetypes.Save(config, "Archetypes");

                // Save all systems (settings + state)
                _systems.SaveAllSettings();
                SaveAllSystemStates(config);

                // Write to disk
                var error = config.Save(savePath);
                if (error != Error.Ok)
                {
                    GD.PrintErr($"[World] ❌ Failed to save: {error}");
                    return;
                }

                sw.Stop();
                GD.Print($"\n💾 WORLD SAVED");
                GD.Print($"   Path: {savePath}");
                GD.Print($"   Time: {sw.Elapsed.TotalMilliseconds:F2}ms");
                GD.Print($"   Entities: {EntityCount}, Systems: {_systems.Count}");
                
                OnAfterSave?.Invoke();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[World] ❌ Save failed with exception: {ex.Message}");
                GD.PrintErr(ex.StackTrace);
            }
        }

        /// <summary>
        /// Saves World's own state (not delegated to managers).
        /// </summary>
        private void SaveWorldState(ConfigFile config)
        {
            config.SetValue("World", "Version", "2.0");
            config.SetValue("World", "EntityCount", EntityCount);
            config.SetValue("World", "ArchetypeCount", ArchetypeCount);
            config.SetValue("World", "AutoSaveEnabled", AutoSaveEnabled);
            config.SetValue("World", "AutoSaveInterval", AutoSaveInterval);
            config.SetValue("World", "SaveTimestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        /// <summary>
        /// Saves all system states by calling Serialize() on each system.
        /// </summary>
        private void SaveAllSystemStates(ConfigFile config)
        {
            var allSystems = _systems.GetAllSystems();
            int statesSaved = 0;

            foreach (var system in allSystems)
            {
                // Call system's Serialize() method to save game state
                system.Serialize();
                statesSaved++;
            }

            config.SetValue("World", "SystemStatesSaved", statesSaved);
        }

        #endregion

        #region Load

        /// <summary>
        /// Loads the entire world state from a file.
        /// Coordinates loads across all managers.
        /// </summary>
        /// <param name="filename">Filename relative to World save directory (e.g., "quicksave.sav")</param>
        public void Load(string filename = "world.sav")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            OnBeforeLoad?.Invoke();
            
            try
            {
                var loadPath = Path.Combine(Paths.WorldDir, filename);
                
                if (!File.Exists(loadPath))
                {
                    GD.PrintErr($"[World] ❌ Save file not found: {loadPath}");
                    return;
                }

                var config = new ConfigFile();
                var error = config.Load(loadPath);
                if (error != Error.Ok)
                {
                    GD.PrintErr($"[World] ❌ Failed to load: {error}");
                    return;
                }

                // Load World's own state
                LoadWorldState(config);

                // TODO Phase 5: When managers are extracted, add:
                // _entities.Load(config, "Entities");
                // _components.Load(config, "Components");
                // _archetypes.Load(config, "Archetypes");

                // Load all system states
                LoadAllSystemStates(config);

                sw.Stop();
                GD.Print($"\n📂 WORLD LOADED");
                GD.Print($"   Path: {loadPath}");
                GD.Print($"   Time: {sw.Elapsed.TotalMilliseconds:F2}ms");
                GD.Print($"   Entities: {EntityCount}, Systems: {_systems.Count}");
                
                OnAfterLoad?.Invoke();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[World] ❌ Load failed with exception: {ex.Message}");
                GD.PrintErr(ex.StackTrace);
            }
        }

        /// <summary>
        /// Loads World's own state (not delegated to managers).
        /// </summary>
        private void LoadWorldState(ConfigFile config)
        {
            if (!config.HasSection("World"))
            {
                GD.PrintErr("[World] ⚠️ No World section found in save file");
                return;
            }

            // Load auto-save settings
            AutoSaveEnabled = (bool)config.GetValue("World", "AutoSaveEnabled", false);
            AutoSaveInterval = (float)config.GetValue("World", "AutoSaveInterval", 60f);

            var savedEntityCount = (int)config.GetValue("World", "EntityCount", 0);
            var savedArchetypeCount = (int)config.GetValue("World", "ArchetypeCount", 0);
            var timestamp = (long)config.GetValue("World", "SaveTimestamp", 0);

            GD.Print($"[World] 📂 Loaded world state from {DateTimeOffset.FromUnixTimeSeconds(timestamp):yyyy-MM-dd HH:mm:ss}");
            GD.Print($"[World]    Expected - Entities: {savedEntityCount}, Archetypes: {savedArchetypeCount}");
        }

        /// <summary>
        /// Loads all system states by calling Deserialize() on each system.
        /// </summary>
        private void LoadAllSystemStates(ConfigFile config)
        {
            var allSystems = _systems.GetAllSystems();
            int statesLoaded = 0;

            foreach (var system in allSystems)
            {
                // Load system settings first
                system.LoadSettings();
                
                // Then call system's Deserialize() method to load game state
                system.Deserialize();
                statesLoaded++;
            }

            GD.Print($"[World] 📂 Loaded {statesLoaded} system states");
        }

        #endregion

        #region Quick Save/Load

        /// <summary>
        /// Quick save to most recent save file.
        /// </summary>
        public void QuickSave() => Save("quicksave.sav");

        /// <summary>
        /// Quick load from most recent save file.
        /// </summary>
        public void QuickLoad() => Load("quicksave.sav");

        #endregion
    }
}
