
using System;

using UltraSim.Configuration;

namespace UltraSim.ECS.Settings
{
    public interface ISetting<T> : ISetting
    {
        new T Value { get; set; }
    }

    public interface ISetting
    {
        string Name { get; }
        string Tooltip { get; set; }

        object Value { get; set; }

        event Action<object> ValueChanged;

        string ToString();

        void Serialize(ConfigFile config, string section);
        void Deserialize(ConfigFile config, string section);
    }
}