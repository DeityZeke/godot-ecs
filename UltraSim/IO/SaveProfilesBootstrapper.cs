#nullable enable

namespace UltraSim.IO
{
    /// <summary>
    /// Registers built-in save profiles during runtime configuration.
    /// Invoked via RuntimeContext.InvokeMethod("Configure").
    /// </summary>
    public static class SaveProfilesBootstrapper
    {
        public static void Configure()
        {
            SaveProfileRegistry.Register(new BinarySaveProfileDescriptor());
            SaveProfileRegistry.Register(new ConfigSaveProfileDescriptor());
        }
    }

    internal sealed class BinarySaveProfileDescriptor : ISaveProfileDescriptor
    {
        public string Name => "Binary";
        public string Description => "Binary writer (.bin)";

        public IIOProfile CreateProfile() =>
            new BinaryIOProfile();
    }

    internal sealed class ConfigSaveProfileDescriptor : ISaveProfileDescriptor
    {
        public string Name => "Config";
        public string Description => "Config file (.cfg)";

        public IIOProfile CreateProfile() =>
            new ConfigIOProfile();
    }
}
