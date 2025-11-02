
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public class ButtonSettingUI : ISettingUI
    {
        public ISetting Setting { get; }
        public Control Node { get; }

        private readonly HBoxContainer _container;
        private readonly Label _label;
        private readonly Button _button;

        public ButtonSettingUI(ISetting setting)
        {
            Setting = setting;
            _container = new HBoxContainer();
            _container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            _label = new Label
            {
                Text = Setting.Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Setting.Tooltip,
                VerticalAlignment = VerticalAlignment.Center
            };

            _button = new Button
            {
                Text = "Execute",
                CustomMinimumSize = new Vector2(100, 0),
                TooltipText = Setting.Tooltip
            };

            _container.AddChild(_label);
            _container.AddChild(_button);
            Node = _container;
        }

        public void Bind()
        {
            if (Setting is ButtonSetting buttonSetting)
            {
                _button.Pressed += () => buttonSetting.Click();
            }
        }
    }
}
