
using System;

using Godot;

namespace UltraSim.ECS.Settings
{
    /// <summary>
    /// Non-generic wrapper for EnumSettingUI to enable creation from ISetting.
    /// </summary>
    public class EnumSettingUI : ISettingUI
    {
        private readonly ISettingUI _inner;

        public ISetting Setting => _inner.Setting;
        public Control Node => _inner.Node;

        public EnumSettingUI(ISetting setting)
        {
            // Get the enum type from the setting's generic interface
            var settingType = setting.GetType();
            var enumType = settingType.GetGenericArguments()[0];

            // Create the generic EnumSettingUI<T> using reflection
            var genericType = typeof(EnumSettingUI<>).MakeGenericType(enumType);
            _inner = (ISettingUI)Activator.CreateInstance(genericType, setting);
        }

        public void Bind()
        {
            _inner.Bind();
        }
    }
}
