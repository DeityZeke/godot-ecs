#nullable enable

using System.IO;
using UltraSim.IO;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Base context shared by save and load operations for systems.
    /// </summary>
    public abstract class SystemSerializationContext
    {
        protected SystemSerializationContext(IIOProfile profile, string relativePath)
        {
            Profile = profile;
            RelativePath = relativePath.Replace('\\', Path.DirectorySeparatorChar);
        }

        public IIOProfile Profile { get; }
        public string RelativePath { get; }

        protected string ResolveRelative(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return RelativePath;
            return Path.Combine(RelativePath, fileName);
        }

        public string ResolveFullPath(string? fileName)
        {
            var relative = ResolveRelative(fileName);
            return Profile.GetFullPath(relative);
        }
    }

    public sealed class SystemSaveContext : SystemSerializationContext
    {
        public SystemSaveContext(IIOProfile profile, string relativePath)
            : base(profile, relativePath)
        {
        }

        public IWriter CreateWriter(string fileName)
        {
            var fullPath = ResolveFullPath(fileName);
            Profile.EnsureDirectory(fullPath);
            return Profile.CreateWriter(fullPath);
        }
    }

    public sealed class SystemLoadContext : SystemSerializationContext
    {
        public SystemLoadContext(IIOProfile profile, string relativePath)
            : base(profile, relativePath)
        {
        }

        public bool TryOpenReader(string fileName, out IReader reader)
        {
            var fullPath = ResolveFullPath(fileName);
            if (!Profile.FileExists(fullPath))
            {
                reader = default!;
                return false;
            }

            reader = Profile.CreateReader(fullPath);
            return true;
        }

        public bool FileExists(string fileName)
        {
            var fullPath = ResolveFullPath(fileName);
            return Profile.FileExists(fullPath);
        }
    }
}
