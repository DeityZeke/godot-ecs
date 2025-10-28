
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    public partial class IntSetting : BaseSetting, ISetting
    {
        public override string Name { get; set; }
        public override string Category { get; set; } = "";
        public override string Tooltip { get; set; } = "";

        public int Value { get; set; }
        public int Min { get; set; } = 0;
        public int Max { get; set; } = 100;
        public int Step { get; set; } = 1;

        public override object GetValue() => Value;
        public override object GetValueAsString() => Value.ToString();
        public override void SetValue(object value) => Value = Convert.ToInt32(value);

        public override void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value);
        }

        public override void Deserialize(ConfigFile config, string section)
        {
            Value = (int)config.GetValue(section, Name, Value);
        }
    }
}