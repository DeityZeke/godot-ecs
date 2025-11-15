
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{

    public class BoolSetting : Setting<bool>
    {
        public BoolSetting(string name, bool defaultValue) : base (name, defaultValue)
        {
            
        }

        public BoolSetting(string name, bool defaultValue, string toolTip) : this(name, defaultValue)
        {
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
