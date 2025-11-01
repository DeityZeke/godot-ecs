
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    public class IntSetting : Setting<int>
    {
        public int Min { get; set; } = 0;
        public int Max { get; set; } = 100;
        public int Step { get; set; } = 1;

        public IntSetting(string name, int value) : base(name, value)
        {

        }

        public IntSetting(string name, int value, string toolTip) : this(name, value)
        {
            Tooltip = toolTip;
        }

        public IntSetting(string name, int value, int min, int max, int step, string toolTip) : this(name, value)
        {
            Min = min;
            Max = max;
            Step = step;
            Tooltip = toolTip;
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