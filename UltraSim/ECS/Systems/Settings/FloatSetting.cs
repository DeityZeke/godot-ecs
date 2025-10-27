using System;
using Godot;

namespace UltraSim.Scripts.ECS.Systems.Settings
{
    public class FloatSetting : ISetting
    {
        public string Name { get; set; }
        public string Category { get; set; } = "";
        public string Tooltip { get; set; } = "";
        
        public float Value { get; set; }
        public float Min { get; set; } = 0f;
        public float Max { get; set; } = 100f;
        public float Step { get; set; } = 0.1f;
        public string Format { get; set; } = "F2";
        
        public object GetValue() => Value;
        public object GetValueAsString() => Value.ToString();
        public void SetValue(object value) => Value = Convert.ToSingle(value);
        
        public Control CreateControl(Action<ISetting> onChanged)
        {
            var container = new HBoxContainer();
            
            var label = new Label 
            { 
                Text = Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Tooltip
            };
            
            var slider = new HSlider
            {
                MinValue = Min,
                MaxValue = Max,
                Value = Value,
                Step = Step,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            
            var valueLabel = new Label 
            { 
                Text = Value.ToString(Format),
                CustomMinimumSize = new Vector2(60, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            
            slider.ValueChanged += (v) =>
            {
                Value = (float)v;
                valueLabel.Text = Value.ToString(Format);
                onChanged?.Invoke(this);
            };
            
            container.AddChild(label);
            container.AddChild(slider);
            container.AddChild(valueLabel);
            
            return container;
        }
        
        public void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value);
        }
        
        public void Deserialize(ConfigFile config, string section)
        {
            Value = (float)config.GetValue(section, Name, Value);
        }
    }
}
