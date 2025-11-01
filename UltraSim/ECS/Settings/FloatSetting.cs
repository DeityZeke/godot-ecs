
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

        public FloatSetting(string name, float value) : base(name, value)
        {
        }
        
        public FloatSetting(string name, float value, string toolTip) : this (name, value)
        {
            Tooltip = toolTip;
        }

        public FloatSetting(string name, float value, float min, float max, float step, string toolTip)
        : this(name, value)
        {
            Min = min;
            Max = max;
            Step = step;
            Tooltip = toolTip;
        }

        public override void Serialize()
        {
        }

        public override void Deserialize()
        {
        }
    }
}