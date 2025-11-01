
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

        // Optional extended metadata if you expose these via Setting metadata
        private float _min = 0f;
        private float _max = 1f;
        private float _step = 0.01f;
        private string _format = "0.00";

        public FloatSettingUI(ISetting setting)
        {
            Setting = setting;
            _container = new HBoxContainer();

            // Retrieve optional metadata if provided (depends on how your core defines settings)
            if (setting is FloatSetting range)
            {
                _min = range.Min;
                _max = range.Max;
                _step = range.Step;
                _format = range.Format;
            }

            _label = new Label
            {
                Text = Setting.Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Setting.Tooltip
            };

            _slider = new HSlider
            {
                MinValue = _min,
                MaxValue = _max,
                Value = Convert.ToSingle(Setting.Value),
                Step = _step,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = Setting.Tooltip
            };

            _valueLabel = new Label
            {
                Text = Convert.ToSingle(Setting.Value).ToString(_format),
                CustomMinimumSize = new Vector2(60, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            _container.AddChild(_label);
            _container.AddChild(_slider);
            _container.AddChild(_valueLabel);

            Node = _container;
        }

        public void Bind()
        {
            _slider.ValueChanged += OnSliderChanged;
            Setting.ValueChanged += OnSettingChanged;
        }

        private void OnSliderChanged(double newValue)
        {
            if (_updatingUI)
                return;

            Setting.Value = (float)newValue;
        }

        private void OnSettingChanged(object newValue)
        {
            _updatingUI = true;
            try
            {
                float v = Convert.ToSingle(newValue);
                if (Math.Abs(v - (float)_slider.Value) > float.Epsilon)
                    _slider.Value = v;
                _valueLabel.Text = v.ToString(_format);
            }
            finally
            {
                _updatingUI = false;
            }
        }
    }
}