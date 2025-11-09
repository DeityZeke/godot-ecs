#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;

using UltraSim;
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
        public void SetValue(string section, string key, object? value)
        {
            if (!_sections.TryGetValue(section, out var dict))
            {
                dict = new Dictionary<string, string>();
                _sections[section] = dict;
            }

            dict[key] = value?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Gets a value from the specified section.
        /// Returns defaultValue if the key doesn't exist or parsing fails.
        /// </summary>
        public T GetValue<T>(string section, string key, T defaultValue = default!)
        {
            if (!_sections.TryGetValue(section, out var dict) || !dict.TryGetValue(key, out var raw))
                return defaultValue;

            try
            {
                if (typeof(T) == typeof(string))
                    return (T)(object)raw;
                if (typeof(T) == typeof(int))
                {
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                        return (T)(object)v;
                    return defaultValue;
                }
                if (typeof(T) == typeof(float))
                {
                    if (float.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v))
                        return (T)(object)v;
                    return defaultValue;
                }
                if (typeof(T) == typeof(double))
                {
                    if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v))
                        return (T)(object)v;
                    return defaultValue;
                }
                if (typeof(T) == typeof(bool))
                {
                    if (bool.TryParse(raw, out var v))
                        return (T)(object)v;

                    // allow 0/1
                    if (raw == "0") return (T)(object)false;
                    if (raw == "1") return (T)(object)true;

                    return defaultValue;
                }
                if (typeof(T).IsEnum)
                {
                    if (Enum.TryParse(typeof(T), raw, true, out var enumVal))
                        return (T)enumVal!;
                    return defaultValue;
                }

                return defaultValue;
            }
            catch (Exception ex)
            {
                Logging.Log($"[ConfigFile] Failed to parse value '{raw}' as {typeof(T).Name}: {ex.Message}", LogSeverity.Warning);
                return defaultValue;
            }
        }

        /// <summary>
        /// Checks if a section exists.
        /// </summary>
        public bool HasSection(string section) => _sections.ContainsKey(section);

        /// <summary>
        /// Checks if a key exists in a section.
        /// </summary>
        public bool HasSectionKey(string section, string key)
            => _sections.TryGetValue(section, out var dict) && dict.ContainsKey(key);

        /// <summary>
        /// Gets all sections.
        /// </summary>
        public IEnumerable<string> GetSections() => _sections.Keys;

        /// <summary>
        /// Gets all keys in a section.
        /// </summary>
        public IEnumerable<string> GetSectionKeys(string section)
        {
            if (!_sections.TryGetValue(section, out var dict))
                return Enumerable.Empty<string>();

            return dict.Keys;
        }

        /// <summary>
        /// Removes a section.
        /// </summary>
        public void EraseSection(string section) => _sections.Remove(section);

        /// <summary>
        /// Removes a key from a section.
        /// </summary>
        public void EraseSectionKey(string section, string key)
        {
            if (_sections.TryGetValue(section, out var dict))
                dict.Remove(key);
        }

        /// <summary>
        /// Clears all data.
        /// </summary>
        public void Clear() => _sections.Clear();

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
                Logging.Log($"[ConfigFile] Failed to load: {resolvedPath} - {ex.Message}", LogSeverity.Error);
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
                Logging.Log($"[ConfigFile] Failed to save: {resolvedPath} - {ex.Message}", LogSeverity.Error);
                return Error.Failed;
            }
        }

        /// <summary>
        /// Parses INI-style content.
        /// </summary>
        private Error ParseINI(string content)
        {
            _sections.Clear();

            string currentSection = string.Empty;

            using var reader = new StringReader(content);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();

                // Skip comments and empty lines
                if (trimmed.Length == 0 || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                    continue;

                // Section header
                if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
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

                    // Remove surrounding quotes if present and length >= 2
                    if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
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
            var sb = new StringBuilder();

            // Iterate sections in sorted order to keep deterministic output
            foreach (var sectionKey in _sections.Keys.OrderBy(k => k))
            {
                sb.Append('[').Append(sectionKey).Append(']').AppendLine();

                var dict = _sections[sectionKey];
                foreach (var key in dict.Keys.OrderBy(k => k))
                {
                    var value = dict[key] ?? string.Empty;

                    // Quote value if it contains spaces or leading/trailing whitespace
                    bool needsQuotes = value.IndexOf(' ') >= 0 || value.Length != value.Trim().Length;
                    if (needsQuotes && !(value.StartsWith("\"") && value.EndsWith("\"")))
                        value = $"\"{value}\"";

                    sb.Append(key).Append('=').Append(value).AppendLine();
                }

                sb.AppendLine(); // Blank line between sections
            }

            return sb.ToString();
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
