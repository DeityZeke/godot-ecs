using System;
using System.IO;
using Godot;

namespace Scripts.ECS.Core.Utilities
{
    public static class ECSPaths
    {
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

        public static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(SavesDir);
            Directory.CreateDirectory(ECSDir);
            Directory.CreateDirectory(SystemsDir);
            Directory.CreateDirectory(WorldDir);
        }

        public static string GetSystemSettingsPath(string systemName)
            => Path.Combine(SystemsDir, $"{systemName}.settings.cfg");

        public static string GetSystemStatePath(string systemName)
            => Path.Combine(SystemsDir, $"{systemName}.state.sav");
    }
}