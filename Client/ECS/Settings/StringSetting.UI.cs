
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    public class StringSettingUI : ISettingUI
    {
        public ISetting Setting { get; }
        public Control Node { get; }

        private readonly HBoxContainer _container;
        private readonly Label _label;
        private readonly LineEdit _lineEdit;

        private bool _updatingUI;

        private int _maxLength = 256;

        public StringSettingUI(ISetting setting)
        {
            Setting = setting;
            _container = new HBoxContainer();

            if (setting is StringSetting meta)
                _maxLength = meta.MaxLength;

            _label = new Label
            {
                Text = Setting.Name,
                CustomMinimumSize = new Vector2(150, 0),
                TooltipText = Setting.Tooltip
            };

            _lineEdit = new LineEdit
            {
                Text = Setting.Value?.ToString() ?? string.Empty,
                MaxLength = _maxLength,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = Setting.Tooltip
            };

            _container.AddChild(_label);
            _container.AddChild(_lineEdit);
            Node = _container;
        }

        public void Bind()
        {
            _lineEdit.TextChanged += OnTextChanged;
            Setting.ValueChanged += OnSettingChanged;
        }

        private void OnTextChanged(string newText)
        {
            if (_updatingUI)
                return;

            Setting.Value = newText;
        }

        private void OnSettingChanged(object value)
        {
            _updatingUI = true;
            try
            {
                string newVal = value?.ToString() ?? string.Empty;
                if (_lineEdit.Text != newVal)
                    _lineEdit.Text = newVal;
            }
            finally
            {
                _updatingUI = false;
            }
        }
    }
}