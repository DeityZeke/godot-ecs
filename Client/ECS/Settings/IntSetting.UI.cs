
using System;

using Godot;
using UltraSim.ECS.Settings;

namespace Client.ECS.Settings
{
    public class IntSettingUI : ISettingUI
    {
        public ISetting Setting { get; }
        public Control Node { get; }

        private readonly HBoxContainer _container;
        private readonly Label _label;
        private readonly HSlider? _slider;
        private readonly SpinBox? _spinBox;
        private readonly Label? _valueLabel;
        private bool _updatingUI;

        public IntSettingUI(ISetting setting)
        {
            Setting = setting;
            var intSetting = (IntSetting)setting;

            _container = new HBoxContainer();
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

            _container.AddChild(_label);

            if (intSetting.IsEditable)
            {
                // Editable: Use SpinBox
                _spinBox = new SpinBox
                {
                    MinValue = intSetting.Min,
                    MaxValue = intSetting.Max,
                    Step = intSetting.Step,
                    Value = Convert.ToInt32(Setting.Value),
                    CustomMinimumSize = new Vector2(150, 0),
                    TooltipText = Setting.Tooltip,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
                };
                _container.AddChild(_spinBox);
            }
            else
            {
                // Read-only: Use Slider + Label
                _slider = new HSlider
                {
                    MinValue = intSetting.Min,
                    MaxValue = intSetting.Max,
                    Step = intSetting.Step,
                    Value = Convert.ToInt32(Setting.Value),
                    CustomMinimumSize = new Vector2(150, 0),
                    TooltipText = Setting.Tooltip
                };

                _valueLabel = new Label
                {
                    Text = Convert.ToInt32(Setting.Value).ToString(),
                    CustomMinimumSize = new Vector2(60, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };

                _container.AddChild(_slider);
                _container.AddChild(_valueLabel);
            }

            Node = _container;
        }

        public void Bind()
        {
            if (_spinBox != null)
            {
                // Editable mode: SpinBox
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
            else if (_slider != null && _valueLabel != null)
            {
                // Read-only mode: Slider + Label
                _slider.ValueChanged += (v) =>
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
                        if ((int)_slider.Value != newVal)
                            _slider.Value = newVal;
                        _valueLabel.Text = newVal.ToString();
                    }
                    finally { _updatingUI = false; }
                };
            }
        }
    }
}
