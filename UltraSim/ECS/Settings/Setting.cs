
#nullable enable

using System;
using System.Collections.Generic;

namespace UltraSim.ECS.Settings
{

    public abstract class Setting<T> : ISetting<T>
    {
        public string Name { get; }
        private T _value;

        public event Action<object>? ValueChanged;

        public T Value
        {
            get => _value!;
            set
            {
                if (!EqualityComparer<T>.Default.Equals(_value, (T)value))
                {
                    _value = (T)value;
                    ValueChanged?.Invoke((T)_value!);
                }
            }
        }

        object ISetting.Value
        {
            get => Value!;
            set => Value = (T)value;
        }

        public T Get() => _value;
        public void Set(T value) => Value = value;

        protected Setting(string name, T defaultValue)
        {
            Name = name;
            _value = defaultValue;
        }

        public string Tooltip { get; set; } = "";

        public override string ToString() => _value == null ? "NullValue" : _value.ToString()!;

        public abstract void Serialize();
        public abstract void Deserialize();
    }
}