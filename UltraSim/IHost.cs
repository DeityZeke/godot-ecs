
#nullable enable

using System;

using UltraSim.Logging;
using UltraSim.IO;

namespace UltraSim
{
    /// <summary>
    /// Defines the interface any engine or runtime host must implement to integrate UltraSim.
    /// Extends IEnvironmentInfo to provide hardware and build information.
    /// </summary>
    public interface IHost : IEnvironmentInfo
    {
        /// <summary>
        /// Returns a handle to the root object of the engine's scene tree or context.
        /// May return null for headless/test scenarios.
        /// </summary>
        object? GetRootHandle();

        /// <summary>
        /// Receives a log entry from the UltraSim runtime.
        /// The host decides how to display, store, or forward it.
        /// </summary>
        void Log(LogEntry entry);

        /// <summary>
        /// Returns the I/O profile to use for save/load operations.
        /// Return null to use the default profile.
        /// </summary>
        IIOProfile? GetIOProfile() => null;
    }
}