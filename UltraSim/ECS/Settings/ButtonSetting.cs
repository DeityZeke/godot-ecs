#nullable enable

using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    /// <summary>
    /// A button setting that triggers an action when clicked.
    /// Unlike other settings, this doesn't have a persistent value.
    /// </summary>
    public class ButtonSetting : ISetting
    {
        public string Name { get; }
        public string Tooltip { get; set; } = "";

        /// <summary>
        /// Action to invoke when the button is clicked.
        /// </summary>
        public event Action? Clicked;

        // Not used for buttons, but required by ISetting
        public event Action<object>? ValueChanged;

        object ISetting.Value
        {
            get => false; // Dummy value
            set { } // No-op
        }

        public ButtonSetting(string name)
        {
            Name = name;
        }

        public ButtonSetting(string name, string tooltip) : this(name)
        {
            Tooltip = tooltip;
        }

        /// <summary>
        /// Invoke the button action.
        /// </summary>
        public void Click()
        {
            Clicked?.Invoke();
        }

        // Buttons don't need serialization
        public void Serialize(ConfigFile config, string section) { }
        public void Deserialize(ConfigFile config, string section) { }

        public override string ToString() => $"[Button: {Name}]";
    }
}
