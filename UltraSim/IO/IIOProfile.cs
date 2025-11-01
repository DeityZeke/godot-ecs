
using System;

namespace UltraSim.IO
{
    /// <summary>
    /// Defines a complete I/O configuration for saving/loading.
    /// Paths, formats, threading, and file access are all encapsulated.
    /// </summary>
    public interface IIOProfile
    {
        string Name { get; }
        string BasePath { get; }
        bool Enabled { get; }
        int MaxThreads { get; } // 1 = single-threaded, >1 = thread pool limit

        string GetFullPath(string filename);
        IWriter CreateWriter(string fullPath);
        IReader CreateReader(string fullPath);
        bool FileExists(string fullPath);
        void EnsureDirectory(string fullPath);
    }
}
