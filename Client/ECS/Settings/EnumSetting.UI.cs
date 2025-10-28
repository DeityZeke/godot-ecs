
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public partial class EnumSetting<T> : ISettingUI where T : struct, Enum
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
            
            var optionButton = new OptionButton 
            { 
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill 
            };
            
            var values = Enum.GetValues<T>();
            int selectedIdx = 0;
            
            for (int i = 0; i < values.Length; i++)
            {
                optionButton.AddItem(values[i].ToString());
                if (values[i].Equals(Value))
                    selectedIdx = i;
            }
            
            optionButton.Selected = selectedIdx;
            
            optionButton.ItemSelected += (idx) =>
            {
                Value = values[idx];
                onChanged?.Invoke(this);
            };
            
            container.AddChild(label);
            container.AddChild(optionButton);
            
            return container;
        }
    }
}