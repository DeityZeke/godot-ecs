#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UltraSim.IO
{
    /// <summary>
    /// Manages path resolution for save files, settings, and data.
    /// Uses assembly location as base (like RunUO's Main.cs).
    /// 
    /// This makes the ECS work anywhere without relying on engine-specific paths.
    /// </summary>

    public static class PathManager
    {
        private static string? _basePath;
        private static string? _savePath;
        private static string? _settingsPath;
        private static string? _dataPath;

        /// <summary>
        /// Base directory (where the executable is located).
        /// </summary>
        public static string BasePath
        {
            get
            {
                if (_basePath == null)
                    Initialize();
                return _basePath!;
            }
        }

        /// <summary>
        /// Directory for save files (e.g., "Saves/").
        /// </summary>
        public static string SavePath
        {
            get
            {
                if (_savePath == null)
                    Initialize();
                return _savePath!;
            }
        }

        /// <summary>
        /// Directory for settings files (e.g., "Settings/").
        /// </summary>
        public static string SettingsPath
        {
            get
            {
                if (_settingsPath == null)
                    Initialize();
                return _settingsPath!;
            }
        }

        /// <summary>
        /// Directory for data files (e.g., "Data/").
        /// </summary>
        public static string DataPath
        {
            get
            {
                if (_dataPath == null)
                    Initialize();
                return _dataPath!;
            }
        }

        /// <summary>
        /// Initialize path manager. Automatically called on first access.
        /// Can be called explicitly to override paths (useful for tests).
        /// </summary>
        public static void Initialize(string? customBasePath = null)
        {
            if (customBasePath != null)
            {
                _basePath = Path.GetFullPath(customBasePath);
            }
            else
            {
                // Get assembly location (like RunUO's ExeDirectory)
                string? assemblyLocation = Assembly.GetExecutingAssembly().Location;

                if (string.IsNullOrEmpty(assemblyLocation))
                {
                    // Fallback to current directory
                    _basePath = Directory.GetCurrentDirectory();
                }
                else
                {
                    _basePath = Path.GetDirectoryName(assemblyLocation) ?? Directory.GetCurrentDirectory();
                }
            }

            // Create subdirectories using CombineFast for minimal allocation
            _savePath = CombineFast(_basePath, "Saves");
            _settingsPath = CombineFast(_basePath, "Settings");
            _dataPath = CombineFast(_basePath, "Data");

            // Ensure directories exist
            EnsureDirectoryExists(_savePath);
            EnsureDirectoryExists(_settingsPath);
            EnsureDirectoryExists(_dataPath);
        }

        /// <summary>
        /// Resolve a path relative to the base directory.
        /// Handles special prefixes:
        /// - "save://" → SavePath
        /// - "settings://" → SettingsPath
        /// - "data://" → DataPath
        /// - Absolute paths → returned as-is
        /// - Relative paths → resolved relative to BasePath
        /// </summary>
        public static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return BasePath;

            // Handle special prefixes
            if (path.StartsWith("save://"))
                return CombineFast(SavePath, path.Substring(7));

            if (path.StartsWith("settings://"))
                return CombineFast(SettingsPath, path.Substring(11));

            if (path.StartsWith("data://"))
                return CombineFast(DataPath, path.Substring(7));

            // If already absolute, return as-is
            if (Path.IsPathRooted(path))
                return path;

            // Otherwise, resolve relative to base
            return CombineFast(BasePath, path);
        }

        /// <summary>
        /// Get the full path for a save file.
        /// </summary>
        public static string GetSavePath(string filename)
        {
            return CombineFast(SavePath, filename);
        }

        /// <summary>
        /// Get the full path for a settings file.
        /// </summary>
        public static string GetSettingsPath(string filename)
        {
            return CombineFast(SettingsPath, filename);
        }

        /// <summary>
        /// Get the full path for a data file.
        /// </summary>
        public static string GetDataPath(string filename)
        {
            return CombineFast(DataPath, filename);
        }

        /// <summary>
        /// Fast path concatenation using string.Concat (avoids Path.Combine overhead).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string CombineFast(string a, string b)
        {
            return string.Concat(a, Path.DirectorySeparatorChar, b);
        }

        /// <summary>
        /// Ensures a directory exists, creating it if necessary.
        /// </summary>
        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    // Can't use Logger here (might not be initialized)
                    Console.WriteLine($"[PathManager] Failed to create directory: {path} - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Reset path manager (useful for tests).
        /// </summary>
        public static void Reset()
        {
            _basePath = null;
            _savePath = null;
            _settingsPath = null;
            _dataPath = null;
        }
    }
}
