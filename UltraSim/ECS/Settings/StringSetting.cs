using System;
using Godot;

namespace UltraSim.Scripts.ECS.Systems.Settings
{
    public class StringSetting : ISetting
    {
        public string Name { get; set; }
        public string Category { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public string Value { get; set; } = "";
        public int MaxLength { get; set; } = 100;

        public object GetValue() => Value;

        public object GetValueAsString() => Value;

        public void SetValue(object value) => Value = value?.ToString() ?? "";
        
        public Control CreateControl(Action<ISetting> onChanged)
        {
            var container = new HBoxContainer();
            
            var label = new Label 
            { 
                Text = Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Tooltip
            };
            
            var lineEdit = new LineEdit
            {
                Text = Value,
                MaxLength = MaxLength,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            
            lineEdit.TextChanged += (newText) =>
            {
                Value = newText;
                onChanged?.Invoke(this);
            };
            
            container.AddChild(label);
            container.AddChild(lineEdit);
            
            return container;
        }
        
        public void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value);
        }
        
        public void Deserialize(ConfigFile config, string section)
        {
            Value = (string)config.GetValue(section, Name, Value);
        }
    }
}
