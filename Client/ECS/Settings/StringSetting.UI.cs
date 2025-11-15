#nullable enable

using System;

using Godot;
using UltraSim.ECS.Settings;

namespace Client.ECS.Settings
{
    public class StringSettingUI : ISettingUI
    {
        public ISetting Setting { get; }
        public Control Node { get; }

        private readonly HBoxContainer _container;
        private readonly Label _label;
        private readonly LineEdit? _lineEdit;
        private readonly OptionButton? _optionButton;
        private readonly IChoiceSetting? _choiceSetting;
        private bool _updatingUI;

        public StringSettingUI(ISetting setting)
        {
            Setting = setting;
            _container = new HBoxContainer();
            _container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            _label = new Label
            {
                Text = Setting.Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Setting.Tooltip
            };

            _container.AddChild(_label);
            if (setting is IChoiceSetting choiceSetting)
            {
                _choiceSetting = choiceSetting;
                _optionButton = new OptionButton
                {
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    TooltipText = Setting.Tooltip,
                    FocusMode = Control.FocusModeEnum.All
                };
                PopulateOptions();
                _optionButton.ItemSelected += OnOptionSelected;
                _container.AddChild(_optionButton);
            }
            else if (((StringSetting)Setting).IsEditable)
            {
                _lineEdit = new LineEdit
                {
                    Text = Setting.Value?.ToString() ?? string.Empty,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    TooltipText = Setting.Tooltip
                };
                _container.AddChild(_lineEdit);
            }
            Node = _container;
        }

        public void Bind()
        {
            if (_lineEdit != null)
            {
                _lineEdit.TextChanged += (text) =>
                {
                    if (_updatingUI) return;
                    Setting.Value = text;
                };
            }

            Setting.ValueChanged += (value) =>
            {
                _updatingUI = true;
                try
                {
                    string newVal = value?.ToString() ?? string.Empty;
                    if (_lineEdit != null && _lineEdit.Text != newVal)
                        _lineEdit.Text = newVal;
                    if (_optionButton != null && _choiceSetting != null)
                        UpdateOptionSelection(newVal);
                }
                finally { _updatingUI = false; }
            };
        }

        private void PopulateOptions()
        {
            if (_optionButton == null || _choiceSetting == null)
                return;

            _optionButton.Clear();
            var options = _choiceSetting.GetOptions();
            var currentValue = Setting.Value?.ToString() ?? string.Empty;
            int selectedIndex = -1;

            for (int i = 0; i < options.Count; i++)
            {
                _optionButton.AddItem(options[i]);
                if (selectedIndex == -1 && string.Equals(options[i], currentValue, StringComparison.OrdinalIgnoreCase))
                    selectedIndex = i;
            }

            if (selectedIndex >= 0)
            {
                _optionButton.Select(selectedIndex);
            }
            else if (options.Count > 0)
            {
                _optionButton.Select(0);
                Setting.Value = options[0];
            }
        }

        private void UpdateOptionSelection(string value)
        {
            if (_optionButton == null || _choiceSetting == null)
                return;

            var options = _choiceSetting.GetOptions();
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    if (_optionButton.Selected != i)
                        _optionButton.Select(i);
                    return;
                }
            }

            if (options.Count > 0 && _optionButton.Selected != 0)
            {
                _optionButton.Select(0);
            }
        }

        private void OnOptionSelected(long index)
        {
            if (_choiceSetting == null)
                return;

            var options = _choiceSetting.GetOptions();
            int idx = (int)index;
            if (idx >= 0 && idx < options.Count)
            {
                _updatingUI = true;
                try
                {
                    Setting.Value = options[idx];
                }
                finally
                {
                    _updatingUI = false;
                }
            }
        }
    }
}
