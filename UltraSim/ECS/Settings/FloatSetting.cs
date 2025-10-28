
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    public partial class FloatSetting : BaseSetting, ISetting
    {
        public override string Name { get; set; }
        public override string Category { get; set; } = "";
        public override string Tooltip { get; set; } = "";

        public float Value { get; set; }
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 100f;
        public float Step { get; set; } = 0.1f;
        public string Format { get; set; } = "F2";

        public override object GetValue() => Value;
        public override object GetValueAsString() => Value.ToString();
        public override void SetValue(object value) => Value = Convert.ToSingle(value);

        public override void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value);
        }

        public override void Deserialize(ConfigFile config, string section)
        {
            Value = (float)config.GetValue(section, Name, Value);
        }
    }
}