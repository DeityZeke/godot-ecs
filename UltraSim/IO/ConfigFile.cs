#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UltraSim.Logging;
using UltraSim.IO;

namespace UltraSim.Configuration
{
    /// <summary>
    /// Engine-independent configuration file format (INI-style).
    /// Uses PathManager + System.IO for file operations (no engine dependency).
    /// </summary>
    public class ConfigFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections = new();

        /// <summary>
        /// Sets a value in the specified section.
        /// </summary>
        public void SetValue(string section, string key, object value)
        {
            if (!_sections.ContainsKey(section))
                _sections[section] = new Dictionary<string, string>();

            _sections[section][key] = value?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Gets a value from the specified section.
        /// Returns defaultValue if the key doesn't exist.
        /// </summary>
        public T GetValue<T>(string section, string key, T defaultValue = default!)
        {
            if (!_sections.ContainsKey(section) || !_sections[section].ContainsKey(key))
                return defaultValue;

            string value = _sections[section][key];

            try
            {
                // Handle different types
                if (typeof(T) == typeof(string))
                    return (T)(object)value;
                else if (typeof(T) == typeof(int))
                    return (T)(object)int.Parse(value);
                else if (typeof(T) == typeof(float))
                    return (T)(object)float.Parse(value);
                else if (typeof(T) == typeof(double))
                    return (T)(object)double.Parse(value);
                else if (typeof(T) == typeof(bool))
                    return (T)(object)bool.Parse(value);
                else if (typeof(T).IsEnum)
                    return (T)Enum.Parse(typeof(T), value);
                else
                    return defaultValue;
            }
            catch
            {
                Logger.Log($"[ConfigFile] Failed to parse value '{value}' as {typeof(T).Name}", LogSeverity.Warning);
                return defaultValue;
            }
        }

        /// <summary>
        /// Checks if a section exists.
        /// </summary>
        public bool HasSection(string section)
        {
            return _sections.ContainsKey(section);
        }

        /// <summary>
        /// Checks if a key exists in a section.
        /// </summary>
        public bool HasSectionKey(string section, string key)
        {
            return _sections.ContainsKey(section) && _sections[section].ContainsKey(key);
        }

        /// <summary>
        /// Gets all sections.
        /// </summary>
        public IEnumerable<string> GetSections()
        {
            return _sections.Keys;
        }

        /// <summary>
        /// Gets all keys in a section.
        /// </summary>
        public IEnumerable<string> GetSectionKeys(string section)
        {
            if (!_sections.ContainsKey(section))
                return Enumerable.Empty<string>();

            return _sections[section].Keys;
        }

        /// <summary>
        /// Removes a section.
        /// </summary>
        public void EraseSection(string section)
        {
            _sections.Remove(section);
        }

        /// <summary>
        /// Removes a key from a section.
        /// </summary>
        public void EraseSectionKey(string section, string key)
        {
            if (_sections.ContainsKey(section))
                _sections[section].Remove(key);
        }

        /// <summary>
        /// Clears all data.
        /// </summary>
        public void Clear()
        {
            _sections.Clear();
        }

        /// <summary>
        /// Loads configuration from a file using PathManager + System.IO.
        /// Path can use prefixes: "settings://filename.ini", "save://config.ini", etc.
        /// </summary>
        public Error Load(string path)
        {
            // Resolve path using PathManager
            string resolvedPath = PathManager.ResolvePath(path);

            if (!File.Exists(resolvedPath))
                return Error.FileNotFound;

            try
            {
                string content = File.ReadAllText(resolvedPath);
                return ParseINI(content);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ConfigFile] Failed to load: {resolvedPath} - {ex.Message}", LogSeverity.Error);
                return Error.FileCorrupt;
            }
        }

        /// <summary>
        /// Saves configuration to a file using PathManager + System.IO.
        /// Path can use prefixes: "settings://filename.ini", "save://config.ini", etc.
        /// </summary>
        public Error Save(string path)
        {
            // Resolve path using PathManager
            string resolvedPath = PathManager.ResolvePath(path);

            try
            {
                // Ensure directory exists
                string? directory = Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string content = ToINI();
                File.WriteAllText(resolvedPath, content);
                return Error.Ok;
            }
            catch (Exception ex)
            {
                Logger.Log($"[ConfigFile] Failed to save: {resolvedPath} - {ex.Message}", LogSeverity.Error);
                return Error.Failed;
            }
        }

        /// <summary>
        /// Parses INI-style content.
        /// </summary>
        private Error ParseINI(string content)
        {
            _sections.Clear();

            string currentSection = "";
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                string trimmed = line.Trim();

                // Skip comments and empty lines
                if (trimmed.StartsWith(";") || trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed))
                    continue;

                // Section header
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2);
                    if (!_sections.ContainsKey(currentSection))
                        _sections[currentSection] = new Dictionary<string, string>();
                    continue;
                }

                // Key-value pair
                int equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex > 0)
                {
                    string key = trimmed.Substring(0, equalsIndex).Trim();
                    string value = trimmed.Substring(equalsIndex + 1).Trim();

                    // Remove quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                        value = value.Substring(1, value.Length - 2);

                    SetValue(currentSection, key, value);
                }
            }

            return Error.Ok;
        }

        /// <summary>
        /// Converts to INI-style format.
        /// </summary>
        private string ToINI()
        {
            var lines = new List<string>();

            foreach (var section in _sections.OrderBy(s => s.Key))
            {
                lines.Add($"[{section.Key}]");

                foreach (var kvp in section.Value.OrderBy(k => k.Key))
                {
                    // Add quotes to string values with spaces
                    string value = kvp.Value;
                    if (value.Contains(' ') && !value.StartsWith("\""))
                        value = $"\"{value}\"";

                    lines.Add($"{kvp.Key}={value}");
                }

                lines.Add(""); // Blank line between sections
            }

            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Error codes for file operations.
    /// </summary>
    public enum Error
    {
        Ok = 0,
        Failed = 1,
        FileNotFound = 7,
        FileCorrupt = 17,
    }
}