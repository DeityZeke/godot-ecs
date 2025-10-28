
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public partial class StringSetting : ISettingUI
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
    }
}