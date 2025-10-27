
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public class BoolSetting : ISetting
    {
        public string Name { get; set; }
        public string Category { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public bool Value { get; set; }
        
        public object GetValue() => Value;
        public object GetValueAsString() => Value.ToString();
        public void SetValue(object value) => Value = Convert.ToBoolean(value);
        
        public Control CreateControl(Action<ISetting> onChanged)
        {
            var container = new HBoxContainer();
            
            var label = new Label 
            { 
                Text = Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Tooltip
            };
            
            var checkBox = new CheckBox 
            { 
                ButtonPressed = Value,
                TooltipText = Tooltip
            };
            
            checkBox.Toggled += (pressed) =>
            {
                Value = pressed;
                onChanged?.Invoke(this);
            };
            
            container.AddChild(label);
            container.AddChild(checkBox);
            
            return container;
        }
        
        public void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value);
        }
        
        public void Deserialize(ConfigFile config, string section)
        {
            Value = (bool)config.GetValue(section, Name, Value);
        }
    }
}
