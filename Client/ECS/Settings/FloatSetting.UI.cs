
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public class FloatSettingUI : ISettingUI
    {
        public ISetting Setting { get; }
        public Control Node { get; }

        private readonly HBoxContainer _container;
        private readonly Label _label;
        private readonly HSlider _slider;
        private readonly Label _valueLabel;
        private bool _updatingUI;

        public FloatSettingUI(ISetting setting)
        {
            Setting = setting;
            //_container = new HBoxContainer();
            //_container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _container = new HBoxContainer
    {
        //Alignment = BoxContainer.AlignmentMode.Center
    };
    _container.AddThemeConstantOverride("separation", 8);
    _container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
    _container.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;

            _label = new Label
            {
                Text = Setting.Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Setting.Tooltip,
        VerticalAlignment = VerticalAlignment.Center
            };

            _slider = new HSlider
            {
                MinValue = 0,
                MaxValue = 1,
                Step = 0.01f,
                Value = Convert.ToSingle(Setting.Value),
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Setting.Tooltip,
        //HSizeFlags = Control.SizeFlags.ShrinkCenter
            };

            _valueLabel = new Label
            {
                Text = Convert.ToSingle(Setting.Value).ToString("0.00"),
                CustomMinimumSize = new Vector2(60, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center
            };

            _container.AddChild(_label);
            _container.AddChild(_slider);
            _container.AddChild(_valueLabel);
            Node = _container;
        }

        public void Bind()
        {
            _slider.ValueChanged += (v) =>
            {
                if (_updatingUI) return;
                Setting.Value = (float)v;
            };

            Setting.ValueChanged += (value) =>
            {
                _updatingUI = true;
                try
                {
                    float newVal = Convert.ToSingle(value);
                    if (Math.Abs(newVal - (float)_slider.Value) > 0.0001f)
                        _slider.Value = newVal;
                    _valueLabel.Text = newVal.ToString("0.00");
                }
                finally { _updatingUI = false; }
            };
        }
    }
}