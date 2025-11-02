
using System;
using System.Collections.Generic;
using System.Linq;

using UltraSim.Configuration;
using UltraSim.Logging;

namespace UltraSim.ECS.Settings
{
    public class SettingsManager
    {
        private Dictionary<string, ISetting> _settings = new(32);

        // Registration
        protected void Register(ISetting setting)
        {
            if (_settings.ContainsKey(setting.Name))
            {
                Logger.Log($"Setting '{setting.Name}' already registered!", LogSeverity.Warning);
                return;
            }
            _settings[setting.Name] = setting;
        }

        // Fluent registration helpers
        protected FloatSetting RegisterFloat(string name, float defaultValue,
            float min = 0f, float max = 100f, float step = 0.1f, string tooltip = "")
        {
            var setting = new FloatSetting(name, defaultValue, min, max, step, tooltip);
            Register(setting);
            return setting;
        }

        protected IntSetting RegisterInt(string name, int defaultValue,
            int min = 0, int max = 100, int step = 1, string tooltip = "")
        {
            var setting = new IntSetting(name, defaultValue, min, max, step, tooltip);
            Register(setting);
            return setting;
        }

        protected BoolSetting RegisterBool(string name, bool defaultValue, string tooltip = "")
        {
            var setting = new BoolSetting(name, defaultValue, tooltip);
            Register(setting);
            return setting;
        }

        protected EnumSetting<T> RegisterEnum<T>(string name, T defaultValue, string tooltip = "")
            where T : struct, Enum
        {
            var setting = new EnumSetting<T>(name, defaultValue, tooltip);
            Register(setting);
            return setting;
        }

        protected StringSetting RegisterString(string name, string defaultValue = "",
            int maxLength = 100, string tooltip = "")
        {
            var setting = new StringSetting(name, defaultValue, maxLength, tooltip);
            Register(setting);
            return setting;
        }

        protected ButtonSetting RegisterButton(string name, string tooltip = "")
        {
            var setting = new ButtonSetting(name, tooltip);
            Register(setting);
            return setting;
        }

        // Generic getters (for dynamic access if needed)
        public T GetValue<T>(string name)
        {
            if (!_settings.TryGetValue(name, out var setting))
            {
                Logger.Log($"Setting '{name}' not found!", LogSeverity.Error);
                return default;
            }
            return (T)((Setting<T>)setting).Value;//setting.GetValue();
        }

        // Convenience accessors
        public float GetFloat(string name) => GetValue<float>(name);
        public int GetInt(string name) => GetValue<int>(name);
        public bool GetBool(string name) => GetValue<bool>(name);
        public string GetString(string name) => GetValue<string>(name);

        // Setters (for dynamic updates if needed)
        public void SetValue<T>(string name, T value)
        {
            if (_settings.TryGetValue(name, out var setting))
                ((Setting<T>)setting).Value = value;//setting.SetValue(value);
        }

        // GUI support
        public List<ISetting> GetAllSettings() => _settings.Values.OrderBy(s => s.Name).ToList();

        // Serialization
        public void Serialize(ConfigFile config, string section)
        {
            foreach (var setting in _settings.Values)
                setting.Serialize(config, section);
        }

        public void Deserialize(ConfigFile config, string section)
        {
            foreach (var setting in _settings.Values)
                setting.Deserialize(config, section);
        }
    }
}