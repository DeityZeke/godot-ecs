
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{

    public class StringSetting : Setting<string>
    {
        public int MaxLength { get; set; }

        public StringSetting(string name, string value) : base(name, value)
        {

        }

        public StringSetting(string name, string value, string toolTip) : this(name, value)
        {
            Tooltip = toolTip;
        }

        public StringSetting(string name, string value, int maxLength, string toolTip) : this(name, value)
        {
            MaxLength = maxLength;
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