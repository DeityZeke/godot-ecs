using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Scripts.ECS.Core.Settings
{
    public abstract class BaseSettings
    {
        private Dictionary<string, ISetting> _settings = new();
        
        // Registration
        protected void Register(ISetting setting)
        {
            if (_settings.ContainsKey(setting.Name))
            {
                GD.PushWarning($"Setting '{setting.Name}' already registered!");
                return;
            }
            _settings[setting.Name] = setting;
        }
        
        // Fluent registration helpers
        protected FloatSetting RegisterFloat(string name, float defaultValue, 
            float min = 0f, float max = 100f, float step = 0.1f, string tooltip = "")
        {
            var setting = new FloatSetting
            {
                Name = name,
                Value = defaultValue,
                Min = min,
                Max = max,
                Step = step,
                Tooltip = tooltip
            };
            Register(setting);
            return setting;
        }
        
        protected IntSetting RegisterInt(string name, int defaultValue, 
            int min = 0, int max = 100, int step = 1, string tooltip = "")
        {
            var setting = new IntSetting
            {
                Name = name,
                Value = defaultValue,
                Min = min,
                Max = max,
                Step = step,
                Tooltip = tooltip
            };
            Register(setting);
            return setting;
        }
        
        protected BoolSetting RegisterBool(string name, bool defaultValue, string tooltip = "")
        {
            var setting = new BoolSetting
            {
                Name = name,
                Value = defaultValue,
                Tooltip = tooltip
            };
            Register(setting);
            return setting;
        }
        
        protected EnumSetting<T> RegisterEnum<T>(string name, T defaultValue, string tooltip = "") 
            where T : struct, Enum
        {
            var setting = new EnumSetting<T>
            {
                Name = name,
                Value = defaultValue,
                Tooltip = tooltip
            };
            Register(setting);
            return setting;
        }
        
        protected StringSetting RegisterString(string name, string defaultValue = "", 
            int maxLength = 100, string tooltip = "")
        {
            var setting = new StringSetting
            {
                Name = name,
                Value = defaultValue,
                MaxLength = maxLength,
                Tooltip = tooltip
            };
            Register(setting);
            return setting;
        }
        
        // Generic getters (for dynamic access if needed)
        public T GetValue<T>(string name)
        {
            if (!_settings.TryGetValue(name, out var setting))
            {
                GD.PushError($"Setting '{name}' not found!");
                return default;
            }
            return (T)setting.GetValue();
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
                setting.SetValue(value);
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
