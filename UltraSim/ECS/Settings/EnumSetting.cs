
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    public class EnumSetting<T> : Setting<T> where T : struct, Enum
    {
        public EnumSetting(string name, T value) : base(name, value)
        {

        }

        public EnumSetting(string name, T value, string toolTip) : this(name, value)
        {
            Tooltip = toolTip;
        }

        public override void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value.ToString());
        }

        public override void Deserialize(ConfigFile config, string section)
        {
            string enumStr = config.GetValue(section, Name, Value.ToString());
            if (Enum.TryParse<T>(enumStr, out T result))
            {
                Value = result;
            }
        }
    }
}