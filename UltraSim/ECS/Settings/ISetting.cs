
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    /// <summary>
    /// Core setting interface - engine-independent.
    /// For UI creation, see ISettingUI extension (Godot-specific).
    /// </summary>
    public interface ISetting
    {
        string Name { get; }
        string Category { get; set; }
        string Tooltip { get; set; }

        object GetValue();
        object GetValueAsString();
        void SetValue(object value);
        
        void Serialize(ConfigFile config, string section);
        void Deserialize(ConfigFile config, string section);
    }
}