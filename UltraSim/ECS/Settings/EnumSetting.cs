
using System;
using Godot;

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

        public override void Serialize()
        {
        }

        public override void Deserialize()
        {
        }
    }
}