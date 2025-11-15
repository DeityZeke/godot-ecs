
using System;
using System.Collections.Generic;
using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{

    public class StringSetting : Setting<string>
    {
        public int MaxLength { get; set; }
        public bool IsEditable { get; set; } = false;

        public StringSetting(string name, string value) : base(name, value)
        {

        }

        public StringSetting(string name, string value, bool editable) : this(name, value)
        {
            IsEditable = editable;
        }

        public StringSetting(string name, string value, string toolTip) : this(name, value)
        {
            Tooltip = toolTip;
        }

        public StringSetting(string name, string value, string toolTip, bool editable) : this(name, value, toolTip)
        {
            IsEditable = editable;
        }

        public StringSetting(string name, string value, int maxLength, string toolTip) : this(name, value)
        {
            MaxLength = maxLength;
            Tooltip = toolTip;
        }

        public StringSetting(string name, string value, int maxLength, string toolTip, bool editable)
            : this(name, value, maxLength, toolTip)
        {
            IsEditable = editable;
        }

        public override void Serialize(ConfigFile config, string section)
        {
            config.SetValue(section, Name, Value);
        }

        public override void Deserialize(ConfigFile config, string section)
        {
            Value = config.GetValue(section, Name, Value);
        }
    }

    public interface IChoiceSetting
    {
        IReadOnlyList<string> GetOptions();
    }

    public sealed class ChoiceStringSetting : StringSetting, IChoiceSetting
    {
        private readonly Func<IReadOnlyList<string>> _optionsProvider;

        public ChoiceStringSetting(string name, string value, string toolTip, Func<IReadOnlyList<string>> optionsProvider)
            : base(name, value, toolTip)
        {
            _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
        }

        public IReadOnlyList<string> GetOptions()
        {
            var options = _optionsProvider.Invoke();
            if (options == null || options.Count == 0)
                return Array.Empty<string>();
            return options;
        }

        public void EnsureValidSelection()
        {
            var options = GetOptions();
            if (options.Count == 0)
                return;

            foreach (var option in options)
            {
                if (string.Equals(option, Value, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            Value = options[0];
        }
    }
}
