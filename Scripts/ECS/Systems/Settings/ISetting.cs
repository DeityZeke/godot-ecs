using System;
using Godot;

namespace Scripts.ECS.Core.Settings
{
    public interface ISetting
    {
        string Name { get; }
        string Category { get; set; }
        string Tooltip { get; set; }
        
        object GetValue();
        void SetValue(object value);
        
        Control CreateControl(Action<ISetting> onChanged);
        
        void Serialize(ConfigFile config, string section);
        void Deserialize(ConfigFile config, string section);
    }
}
