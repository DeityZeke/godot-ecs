
using System;
using System.IO;

namespace UltraSim.IO
{
    /// <summary>
    /// Base class for IOProfiles with common functionality.
    /// Subclass to create format-specific profiles (Binary, Config, etc.)
    /// </summary>
    public abstract class BaseIOProfile : IIOProfile
    {
        protected BaseIOProfile(string name, string basePath, int maxThreads = 1)
        {
            Name = name;
            BasePath = basePath;
            MaxThreads = maxThreads;
        }

        public string Name { get; }
        public string BasePath { get; }
        public bool Enabled => true;
        public int MaxThreads { get; }

        public abstract string GetFullPath(string filename);
        public abstract IWriter CreateWriter(string fullPath);
        public abstract IReader CreateReader(string fullPath);

        public bool FileExists(string fullPath) => File.Exists(fullPath);

        public void EnsureDirectory(string fullPath)
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// Binary format profile (.bin) - Fast, compact, default format.
    /// </summary>
    public sealed class BinaryIOProfile : BaseIOProfile
    {
        public static readonly BinaryIOProfile Instance = new();

        public BinaryIOProfile(string basePath = null, int maxThreads = 1)
            : base("Binary", basePath ?? Path.Combine(AppContext.BaseDirectory, "saves"), maxThreads)
        {
        }

        public override string GetFullPath(string filename) =>
            Path.Combine(BasePath, filename + ".bin");

        public override IWriter CreateWriter(string fullPath) =>
            new BinaryFileWriter(fullPath);

        public override IReader CreateReader(string fullPath) =>
            new BinaryFileReader(fullPath);
    }

    /// <summary>
    /// Config format profile (.cfg) - Human-readable INI files for settings.
    /// </summary>
    public sealed class ConfigIOProfile : BaseIOProfile
    {
        public static readonly ConfigIOProfile Instance = new();

        public ConfigIOProfile(string basePath = null, int maxThreads = 1)
            : base("Config", basePath ?? Path.Combine(AppContext.BaseDirectory, "saves"), maxThreads)
        {
        }

        public override string GetFullPath(string filename) =>
            Path.Combine(BasePath, filename + ".cfg");

        public override IWriter CreateWriter(string fullPath) =>
            new ConfigFileWriter(fullPath);

        public override IReader CreateReader(string fullPath) =>
            new ConfigFileReader(fullPath);
    }

    /// <summary>
    /// Default IOProfile - uses binary format.
    /// Kept for backward compatibility. Prefer using BinaryIOProfile.Instance directly.
    /// </summary>
    public sealed class DefaultIOProfile : BaseIOProfile
    {
        public static readonly DefaultIOProfile Instance = new();

        private DefaultIOProfile()
            : base("Default", Path.Combine(AppContext.BaseDirectory, "saves"), 1) { }

        public override string GetFullPath(string filename) =>
            Path.Combine(BasePath, filename + ".bin");

        public override IWriter CreateWriter(string fullPath) =>
            new BinaryFileWriter(fullPath);

        public override IReader CreateReader(string fullPath) =>
            new BinaryFileReader(fullPath);
    }
}
