
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public class BoolSettingUI : ISettingUI
    {
        public ISetting Setting { get; }
        public Control Node { get; }

        private readonly HBoxContainer container;
        private readonly Label text;
        private readonly CheckBox checkBox;

        private bool _updatingUI;

        public BoolSettingUI(ISetting setting)
        {
            Setting = setting;
            container = new HBoxContainer();

            text = new Label
            {
                Text = Setting.Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Setting.Tooltip
            };

            checkBox = new CheckBox
            {
                ButtonPressed = (bool)Setting.Value,
                TooltipText = Setting.Tooltip
            };

            container.AddChild(text);
            container.AddChild(checkBox);

            Node = container;
        }

        public void Bind()
        {
            checkBox.Toggled += OnToggled;
            Setting.ValueChanged += OnSettingChanged;
        }

        private void OnToggled(bool pressed)
        {
            if (_updatingUI)
                return;

            Setting.Value = pressed;
        }

        private void OnSettingChanged(object value)
        {
            _updatingUI = true;
            try
            {
                checkBox.ButtonPressed = (bool)value;
            }
            finally
            {
                _updatingUI = false;
            }
        }
    }
}