
using System;

using Godot;

namespace UltraSim.ECS.Settings // The must have the same namespace as the ones IN UltraSim.ECS or it wont work
{
    public partial class IntSetting : ISettingUI
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
    }
}