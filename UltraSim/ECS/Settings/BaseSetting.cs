
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    public abstract partial class BaseSetting : ISetting
    {
        public virtual string Name { get; set; } = "";
        public virtual string Category { get; set; } = "";
        public virtual string Tooltip { get; set; } = "";

        public abstract object GetValue();
        public abstract object GetValueAsString();
        public abstract void SetValue(object Value);

        public abstract void Serialize(ConfigFile config, string section);
        public abstract void Deserialize(ConfigFile config, string section);
    }
}