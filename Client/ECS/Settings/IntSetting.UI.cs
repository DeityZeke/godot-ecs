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

        // Optional range metadata
        private int _min = 0;
        private int _max = 100;
        private int _step = 1;

        public IntSettingUI(ISetting setting)
        {
            Setting = setting;
            _container = new HBoxContainer();

            // Optionally extract range metadata from core IntSetting (if it has Min/Max/Step)
            if (setting is IntSetting core)
            {
                _min = core.Min;
                _max = core.Max;
                _step = core.Step;
            }

            _label = new Label
            {
                Text = Setting.Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Setting.Tooltip
            };

            _spinBox = new SpinBox
            {
                MinValue = _min,
                MaxValue = _max,
                Value = Convert.ToInt32(Setting.Value),
                Step = _step,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = Setting.Tooltip
            };

            _container.AddChild(_label);
            _container.AddChild(_spinBox);
            Node = _container;
        }

        public void Bind()
        {
            _spinBox.ValueChanged += OnValueChanged;
            Setting.ValueChanged += OnSettingChanged;
        }

        private void OnValueChanged(double newValue)
        {
            if (_updatingUI)
                return;

            Setting.Value = (int)newValue;
        }

        private void OnSettingChanged(object value)
        {
            _updatingUI = true;
            try
            {
                int intVal = Convert.ToInt32(value);
                if (Math.Abs(intVal - (int)_spinBox.Value) > 0)
                    _spinBox.Value = intVal;
            }
            finally
            {
                _updatingUI = false;
            }
        }
    }
}
