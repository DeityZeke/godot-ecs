using System;
using Godot;

namespace UltraSim.Scripts.ECS.Systems.Settings
{
    public class IntSetting : ISetting
    {
        public string Name { get; set; }
        public string Category { get; set; } = "";
        public string Tooltip { get; set; } = "";
        
        public int Value { get; set; }
        public int Min { get; set; } = 0;
        public int Max { get; set; } = 100;
        public int Step { get; set; } = 1;
        
        public object GetValue() => Value;
        public void SetValue(object value) => Value = Convert.ToInt32(value);
        
        public Control CreateControl(Action<ISetting> onChanged)
        {
            var container = new HBoxContainer();
            
            var label = new Label 
            { 
                Text = Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Tooltip
            };
            
            var spinBox = new SpinBox
            {
                MinValue = Min,
                MaxValue = Max,
                Value = Value,
                Step = Step,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            
            spinBox.ValueChanged += (v) =>
            {
                Value = (int)v;
                onChanged?.Invoke(this);
            };
            
            container.AddChild(label);
            container.AddChild(spinBox);
            
            return container;
        }
        
        public void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value);
        }
        
        public void Deserialize(ConfigFile config, string section)
        {
            Value = (int)config.GetValue(section, Name, Value);
        }
    }
}
