
using System;
using System.Collections.Generic;
using System.Linq;

using UltraSim.Configuration;
using UltraSim.Logging;

namespace UltraSim.ECS.Settings
{
    public abstract partial class BaseSetting : ISetting
    {
        public virtual string Name { get; set; } = "";
        public virtual string Category { get; set; } = "";
        public virtual string Tooltip { get; set; } = "";

        //public virtual object GetValue() { return null; }
        public abstract object GetValue();
        //protected virtual string GetValueAsString() { return GetValue().ToString() ?? $"null"; }
        public abstract object GetValueAsString();
        //public virtual void SetValue(object Value) { }
        public abstract void SetValue(object Value);

        public abstract void Serialize(ConfigFile config, string section);
        public abstract void Deserialize(ConfigFile config, string section);
    }

    //public abstract partial class BaseSettings
    public abstract partial class SettingsManager
    {
        //private Dictionary<string, ISetting> _settings = new();
        private Dictionary<string, BaseSetting> _settings = new(32);

        // Registration
        //protected void Register(ISetting setting)
        protected void Register(BaseSetting setting)
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
                //GD.PushError($"Setting '{name}' not found!");
                Logger.Log($"Setting '{name}' not found!", LogSeverity.Error);
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
        public List<BaseSetting> GetAllSettings() => _settings.Values.OrderBy(s => s.Name).ToList();

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