
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public class EnumSettingUI<T> : ISettingUI where T : struct, Enum
    {
        public ISetting Setting { get; }
        public Control Node { get; }

        private readonly HBoxContainer _container;
        private readonly Label _label;
        private readonly OptionButton _optionButton;
        private readonly T[] _values;

        private bool _updatingUI;

        public EnumSettingUI(ISetting setting)
        {
            Setting = setting;
            _values = Enum.GetValues<T>();

            _container = new HBoxContainer();

            _label = new Label
            {
                Text = Setting.Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Setting.Tooltip
            };

            _optionButton = new OptionButton
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = Setting.Tooltip
            };

            // Populate options
            int selectedIdx = 0;
            for (int i = 0; i < _values.Length; i++)
            {
                _optionButton.AddItem(_values[i].ToString());
                if (_values[i].Equals(Setting.Value))
                    selectedIdx = i;
            }
            _optionButton.Selected = selectedIdx;

            _container.AddChild(_label);
            _container.AddChild(_optionButton);

            Node = _container;
        }

        public void Bind()
        {
            _optionButton.ItemSelected += OnItemSelected;
            Setting.ValueChanged += OnSettingChanged;
        }

        private void OnItemSelected(long index)
        {
            if (_updatingUI)
                return;

            if (index >= 0 && index < _values.Length)
                Setting.Value = _values[index];
        }

        private void OnSettingChanged(object value)
        {
            _updatingUI = true;
            try
            {
                int selectedIdx = Array.IndexOf(_values, (T)value);
                if (selectedIdx >= 0 && selectedIdx != _optionButton.Selected)
                    _optionButton.Select(selectedIdx);
            }
            finally
            {
                _updatingUI = false;
            }
        }
    }
}