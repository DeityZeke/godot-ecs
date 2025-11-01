
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
        private bool _updatingUI;

        public EnumSettingUI(ISetting setting)
        {
            Setting = setting;
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

            var values = Enum.GetValues<T>();
            int selected = 0;
            for (int i = 0; i < values.Length; i++)
            {
                _optionButton.AddItem(values[i].ToString());
                if (values[i].Equals(Setting.Value))
                    selected = i;
            }
            _optionButton.Selected = selected;

            _container.AddChild(_label);
            _container.AddChild(_optionButton);
            Node = _container;
        }

        public void Bind()
        {
            var values = Enum.GetValues<T>();

            _optionButton.ItemSelected += (idx) =>
            {
                if (_updatingUI) return;
                Setting.Value = values[idx];
            };

            Setting.ValueChanged += (value) =>
            {
                _updatingUI = true;
                try
                {
                    int newIdx = Array.IndexOf(values, (T)value);
                    if (_optionButton.Selected != newIdx)
                        _optionButton.Selected = newIdx;
                }
                finally { _updatingUI = false; }
            };
        }
    }
}