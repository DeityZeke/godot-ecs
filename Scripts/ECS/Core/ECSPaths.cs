using System;
using System.IO;

using Godot;

namespace UltraSim.Scripts.ECS.Core.Utilities
{
    public static class ECSPaths
    {
        /// <summary>
        /// Central path management for ECS save files.
        /// Each system gets its own folder for settings and state.
        /// </summary>
        public static string ExecutableDir => OS.GetExecutablePath().GetBaseDir();

        // Check if running in editor (not exported)
        // OS.HasFeature("editor") returns true when running from Godot editor
        private static bool IsRunningInEditor => OS.HasFeature("editor");

        private static string BaseDir => IsRunningInEditor ? ProjectSettings.GlobalizePath("user://") : ExecutableDir;
        public static string SavesDir => Path.Combine(BaseDir, "Saves");
        public static string ECSDir => Path.Combine(SavesDir, "ECS");
        public static string SystemsDir => Path.Combine(ECSDir, "Systems");
        public static string WorldDir => Path.Combine(ECSDir, "World");

        public static string MasterConfigPath => Path.Combine(ECSDir, "ecs_master.cfg");

        /// <summary>
        /// Ensures base ECS directories exist.
        /// </summary>
        public static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(SavesDir);
            Directory.CreateDirectory(ECSDir);
            Directory.CreateDirectory(SystemsDir);
            Directory.CreateDirectory(WorldDir);
        }

        /// <summary>
        /// Gets the directory path for a specific system.
        /// Creates the directory if it doesn't exist.
        /// </summary>
        public static string GetSystemDirectory(string systemName)
        {
            var systemDir = Path.Combine(SystemsDir, systemName);
            Directory.CreateDirectory(systemDir); // Ensure it exists
            return systemDir;
        }

        /// <summary>
        /// Gets the settings file path for a system.
        /// Format: Saves/ECS/Systems/[SystemName]/[SystemName].settings.cfg
        /// </summary>
        public static string GetSystemSettingsPath(string systemName)
        {
            var systemDir = GetSystemDirectory(systemName);
            return Path.Combine(systemDir, $"{systemName}.settings.cfg");
        }

        /// <summary>
        /// Gets the state file path for a system.
        /// Format: Saves/ECS/Systems/[SystemName]/[SystemName].sav
        /// </summary>
        public static string GetSystemStatePath(string systemName)
        {
            var systemDir = GetSystemDirectory(systemName);
            return Path.Combine(systemDir, $"{systemName}.sav");
        }
    }
}