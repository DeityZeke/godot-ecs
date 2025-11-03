
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{

    public class FloatSetting : Setting<float>
    {
        public float Min { get; set; } = 0.0f;
        public float Max { get; set; } = 100.0f;
        public float Step { get; set; } = 0.1f;
        public string Format { get; set; } = $"F3";
        public bool IsEditable { get; set; } = false;

        public FloatSetting(string name, float value) : base(name, value)
        {
        }

        public FloatSetting(string name, float value, bool editable) : this(name, value)
        {
            IsEditable = editable;
        }

        public FloatSetting(string name, float value, string toolTip) : this (name, value)
        {
            Tooltip = toolTip;
        }

        public FloatSetting(string name, float value, string toolTip, bool editable) : this(name, value, toolTip)
        {
            IsEditable = editable;
        }

        public FloatSetting(string name, float value, float min, float max, float step, string toolTip)
        : this(name, value)
        {
            Min = min;
            Max = max;
            Step = step;
            Tooltip = toolTip;
        }

        public FloatSetting(string name, float value, float min, float max, float step, string toolTip, bool editable)
        : this(name, value, min, max, step, toolTip)
        {
            IsEditable = editable;
        }

        public override void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value);
        }

        public override void Deserialize(ConfigFile config, string section)
        {
            Value = config.GetValue(section, Name, Value);
        }
    }
}