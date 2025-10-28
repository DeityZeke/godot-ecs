
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public partial class FloatSetting : ISettingUI
    {        
        public override Control CreateControl(Action<ISetting> onChanged)
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
    }
}