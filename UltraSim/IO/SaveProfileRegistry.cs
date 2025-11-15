#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraSim.IO
{
    /// <summary>
    /// Describes a save profile option exposed to save systems.
    /// </summary>
    public interface ISaveProfileDescriptor
    {
        string Name { get; }
        string Description { get; }
        IIOProfile CreateProfile();
    }

    /// <summary>
    /// Global registry for save profile descriptors.
    /// Systems can query available profiles to populate UI selections.
    /// </summary>
    public static class SaveProfileRegistry
    {
        private static readonly Dictionary<string, ISaveProfileDescriptor> _profiles =
            new(StringComparer.OrdinalIgnoreCase);

        private static ISaveProfileDescriptor? _default;

        public static IReadOnlyList<ISaveProfileDescriptor> Profiles =>
            _profiles.Values.ToList();

        public static ISaveProfileDescriptor? Default => _default;

        public static void Register(ISaveProfileDescriptor descriptor)
        {
            if (_profiles.ContainsKey(descriptor.Name))
                return;

            _profiles[descriptor.Name] = descriptor;
            _default ??= descriptor;
        }

        public static ISaveProfileDescriptor? Get(string name)
        {
            if (_profiles.TryGetValue(name, out var descriptor))
                return descriptor;
            return _default;
        }
    }
}
