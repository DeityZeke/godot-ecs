
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public class IntSettingUI : ISettingUI
    {
        public ISetting Setting { get; }
        public Control Node { get; }

        private readonly HBoxContainer _container;
        private readonly Label _label;
        private readonly SpinBox _spinBox;
        private bool _updatingUI;

        public IntSettingUI(ISetting setting)
        {
            Setting = setting;
            _container = new HBoxContainer();

            _label = new Label
            {
                Text = Setting.Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Setting.Tooltip
            };

            _spinBox = new SpinBox
            {
                MinValue = 0,
                MaxValue = 100,
                Step = 1,
                Value = Convert.ToInt32(Setting.Value),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = Setting.Tooltip
            };

            _container.AddChild(_label);
            _container.AddChild(_spinBox);
            Node = _container;
        }

        public void Bind()
        {
            _spinBox.ValueChanged += (v) =>
            {
                if (_updatingUI) return;
                Setting.Value = (int)v;
            };

            Setting.ValueChanged += (value) =>
            {
                _updatingUI = true;
                try
                {
                    int newVal = Convert.ToInt32(value);
                    if ((int)_spinBox.Value != newVal)
                        _spinBox.Value = newVal;
                }
                finally { _updatingUI = false; }
            };
        }
    }
}