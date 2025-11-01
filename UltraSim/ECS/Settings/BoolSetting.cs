
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

        public override void Serialize() { }
        public override void Deserialize() { }

    }
}