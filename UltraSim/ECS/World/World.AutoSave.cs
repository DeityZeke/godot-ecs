#nullable enable

using System;
using UltraSim.Logging;

namespace UltraSim.ECS
{
    /// <summary>
    /// World auto-save functionality - handles automatic periodic saves.
    /// </summary>
    public sealed partial class World
    {
        #region Auto-Save State

        /// <summary>
        /// Whether auto-save is currently enabled.
        /// </summary>
        public bool AutoSaveEnabled { get; set; } = false;

        /// <summary>
        /// Time in seconds between auto-saves.
        /// </summary>
        public float AutoSaveInterval { get; set; } = 60f; // Default: 1 minute

        /// <summary>
        /// Last save timestamp (UTC). Null if never saved.
        /// </summary>
        public DateTimeOffset? LastSaveTime { get; set; } = null;

        private float _autoSaveTimer = 0f;
        private int _autoSaveCounter = 0;

        #endregion

        #region Auto-Save Update

        /// <summary>
        /// Updates the auto-save timer. Called every frame from Tick().
        /// </summary>
        private void UpdateAutoSave(float delta)
        {
            if (!AutoSaveEnabled)
                return;

            _autoSaveTimer += delta;

            if (_autoSaveTimer >= AutoSaveInterval)
            {
                PerformAutoSave();
                _autoSaveTimer = 0f;
            }
        }

        /// <summary>
        /// Performs an auto-save with a unique filename.
        /// Keeps 3 rotating auto-saves to prevent losing progress if corruption occurs.
        /// </summary>
        private void PerformAutoSave()
        {
            _autoSaveCounter++;
            string filename = $"autosave_{_autoSaveCounter % 3}.sav"; // Rotate: autosave_0, autosave_1, autosave_2

            Logger.Log($"\nðŸ• AUTO-SAVE #{_autoSaveCounter}", LogSeverity.Debug);
            Save(filename);
        }

        #endregion

        #region Auto-Save Control

        /// <summary>
        /// Enables auto-save with specified interval.
        /// </summary>
        /// <param name="intervalSeconds">Time in seconds between auto-saves (default: 60s)</param>
        public void EnableAutoSave(float intervalSeconds = 60f)
        {
            AutoSaveEnabled = true;
            AutoSaveInterval = intervalSeconds;
            _autoSaveTimer = 0f;

            Logger.Log($"[World] âœ… Auto-save enabled (interval: {intervalSeconds}s)");
        }

        /// <summary>
        /// Disables auto-save.
        /// </summary>
        public void DisableAutoSave()
        {
            AutoSaveEnabled = false;
            Logger.Log("[World] â¸ï¸ Auto-save disabled");
        }

        /// <summary>
        /// Changes the auto-save interval without disabling auto-save.
        /// </summary>
        /// <param name="intervalSeconds">New interval in seconds</param>
        public void SetAutoSaveInterval(float intervalSeconds)
        {
            AutoSaveInterval = intervalSeconds;
            _autoSaveTimer = 0f; // Reset timer

            if (AutoSaveEnabled)
            {
                Logger.Log($"[World] Auto-save interval changed to {intervalSeconds}s");
            }
        }

        /// <summary>
        /// Gets the time remaining until the next auto-save (in seconds).
        /// Returns null if auto-save is disabled.
        /// </summary>
        public float? GetTimeUntilNextSave()
        {
            if (!AutoSaveEnabled)
                return null;

            return AutoSaveInterval - _autoSaveTimer;
        }

        /// <summary>
        /// Gets the estimated timestamp of the next auto-save.
        /// Returns null if auto-save is disabled.
        /// </summary>
        public DateTimeOffset? GetNextSaveTime()
        {
            if (!AutoSaveEnabled)
                return null;

            float timeRemaining = AutoSaveInterval - _autoSaveTimer;
            return DateTimeOffset.UtcNow.AddSeconds(timeRemaining);
        }

        #endregion
    }
}