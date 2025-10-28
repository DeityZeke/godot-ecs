
using System;
using System.Linq;
using System.Collections.Generic;

using Godot;

namespace UltraSim.ECS.Settings
{

    public abstract partial class BaseSetting : ISettingUI
    {
        public abstract Control CreateControl(Action<ISetting> setting);
    }

    public abstract partial class SettingManager
    {
    }
}