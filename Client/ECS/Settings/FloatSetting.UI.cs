
#nullable enable

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
        private readonly HSlider? _slider;
        private readonly SpinBox? _spinBox;
        private readonly Label? _valueLabel;
        private bool _updatingUI;

        public FloatSettingUI(ISetting setting)
        {
            Setting = setting;
            var floatSetting = (FloatSetting)setting;

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

            if (floatSetting.IsEditable)
            {
                // Editable: Use SpinBox
                _spinBox = new SpinBox
                {
                    MinValue = floatSetting.Min,
                    MaxValue = floatSetting.Max,
                    Step = floatSetting.Step,
                    Value = Convert.ToSingle(Setting.Value),
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
                    MinValue = floatSetting.Min,
                    MaxValue = floatSetting.Max,
                    Step = floatSetting.Step,
                    Value = Convert.ToSingle(Setting.Value),
                    CustomMinimumSize = new Vector2(150, 0),
                    TooltipText = Setting.Tooltip
                };

                _valueLabel = new Label
                {
                    Text = Convert.ToSingle(Setting.Value).ToString("0.00"),
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
                    Setting.Value = (float)v;
                };

                Setting.ValueChanged += (value) =>
                {
                    _updatingUI = true;
                    try
                    {
                        float newVal = Convert.ToSingle(value);
                        if (Math.Abs(newVal - (float)_spinBox.Value) > 0.0001f)
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
}