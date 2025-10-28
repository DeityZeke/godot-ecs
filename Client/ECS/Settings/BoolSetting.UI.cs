
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public partial class BoolSetting : ISettingUI
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
    }
}