
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public class EnumSetting<T> : ISetting where T : struct, Enum
    {
        public string Name { get; set; }
        public string Category { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public T Value { get; set; }
        
        public object GetValue() => Value;
        public object GetValueAsString() => Value.ToString();
        public void SetValue(object value) => Value = (T)value;
        
        public Control CreateControl(Action<ISetting> onChanged)
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
        
        public void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value.ToString());
        }
        
        public void Deserialize(ConfigFile config, string section)
        {
            var str = (string)config.GetValue(section, Name, Value.ToString());
            if (Enum.TryParse<T>(str, out var result))
                Value = result;
        }
    }
}
