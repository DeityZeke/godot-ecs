
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    public partial class EnumSetting<T> : BaseSetting, ISetting where T : struct, Enum
    {
        public override string Name { get; set; }
        public override string Category { get; set; } = "";
        public override string Tooltip { get; set; } = "";
        public T Value { get; set; }

        public override object GetValue() => Value;
        public override object GetValueAsString() => Value.ToString();
        public override void SetValue(object value) => Value = (T)value;

        public override void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value.ToString());
        }

        public override void Deserialize(ConfigFile config, string section)
        {
            var str = (string)config.GetValue(section, Name, Value.ToString());
            if (Enum.TryParse<T>(str, out var result))
                Value = result;
        }
    }
}