
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    public partial class StringSetting : BaseSetting, ISetting
    {
        public override string Name { get; set; }
        public override string Category { get; set; } = "";
        public override string Tooltip { get; set; } = "";
        public string Value { get; set; } = "";
        public int MaxLength { get; set; } = 100;

        public override object GetValue() => Value;

        public override object GetValueAsString() => Value;

        public override void SetValue(object value) => Value = value?.ToString() ?? "";

        public override void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value);
        }

        public override void Deserialize(ConfigFile config, string section)
        {
            Value = (string)config.GetValue(section, Name, Value);
        }
    }
}