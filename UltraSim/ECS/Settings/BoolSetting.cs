
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    public partial class BoolSetting : BaseSetting, ISetting
    {
        public override string Name { get; set; }
        public override string Category { get; set; } = "";
        public override string Tooltip { get; set; } = "";
        public bool Value { get; set; }

        public override object GetValue() => Value;
        public override object GetValueAsString() => Value.ToString();
        public override void SetValue(object value) => Value = Convert.ToBoolean(value);

        public override void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value);
        }

        public override void Deserialize(ConfigFile config, string section)
        {
            Value = (bool)config.GetValue(section, Name, Value);
        }
    }
}