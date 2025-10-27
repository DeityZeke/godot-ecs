
#nullable enable

using System;

using UltraSim.Logging;

namespace UltraSim
{
    /// <summary>
    /// Defines the minimal interface any engine or runtime host must implement
    /// in order to integrate UltraSim. Provides access to root handles, logging, etc.
    /// </summary>
    public interface IHost
    {
        /// <summary>
        /// Returns a handle to the root object of the engine’s scene tree or context.
        /// </summary>
        object? GetRootHandle();

        /// <summary>
        /// Receives a log entry from the UltraSim runtime.
        /// The host decides how to display, store, or forward it.
        /// </summary>
        void Log(LogEntry entry);
    }
}