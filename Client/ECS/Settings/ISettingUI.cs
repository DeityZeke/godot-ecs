using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    /// <summary>
    /// Optional UI extension for settings.
    /// Implement this interface in addition to ISetting for Godot UI support.
    /// This interface is Godot-specific and should live in the Client layer.
    /// </summary>
    public interface ISettingUI
    {        
        ISetting Setting { get; }
        /// <summary>
        /// Creates a Godot Control node for this setting.
        /// Only used by ECSControlPanel (Godot-specific).
        /// </summary>
        //Control CreateControl(Action<ISetting> onChanged);        
        Control Node { get; }
        void Bind();
    }
}