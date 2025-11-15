#nullable enable

using System;
using System.Collections.Generic;
using UltraSim;
using UltraSim.IO;
using UltraSim.ECS.Settings;

namespace UltraSim.ECS.Systems
{
    /// <summary>
    /// Coordinates automatic and manual world saves by selecting
    /// a registered save profile and requesting the world to persist.
    /// </summary>
    public sealed class SaveSystem : BaseSystem
    {
        public sealed class Settings : SystemSettings
        {
            public BoolSetting AutoSaveEnabled { get; }
            public FloatSetting AutoSaveIntervalSeconds { get; }
            public ChoiceStringSetting SaveProfile { get; }
            public ButtonSetting ManualSave { get; }

            public Settings()
            {
                AutoSaveEnabled = RegisterBool("Enable Auto-Save", true,
                    tooltip: "Automatically trigger saves on a timer.");

                AutoSaveIntervalSeconds = RegisterFloat("Auto-Save Interval (seconds)", 120f,
                    min: 5f, max: 1800f, step: 5f,
                    tooltip: "Seconds between automatic saves.");

                var defaultProfile = SaveProfileRegistry.Default?.Name ?? "Config";
                SaveProfile = RegisterChoice("Save Profile", defaultProfile,
                    GetProfileNames,
                    tooltip: "Which registered save profile to use when saving.");

                ManualSave = RegisterButton("Save Now", "Immediately trigger a world save.");
            }
        }

        public Settings SystemSettings { get; } = new();
        public override SystemSettings? GetSettings() => SystemSettings;

        public override string Name => "Save System";
        public override int SystemId => typeof(SaveSystem).GetHashCode();
        public override TickRate Rate => TickRate.EveryFrame;
        public override Type[] ReadSet { get; } = Array.Empty<Type>();
        public override Type[] WriteSet { get; } = Array.Empty<Type>();

        private double _timer;

        public override void OnInitialize(World world)
        {
            ResetTimer();
            SystemSettings.ManualSave.Clicked += () => RequestSave(world, SaveTrigger.Manual);
        }

        public override void Update(World world, double delta)
        {
            if (!SystemSettings.AutoSaveEnabled.Value)
                return;

            _timer -= delta;
            if (_timer <= 0)
            {
                RequestSave(world, SaveTrigger.Auto);
            }
        }

        private void RequestSave(World world, SaveTrigger trigger)
        {
            var descriptor = SaveProfileRegistry.Get(SystemSettings.SaveProfile.Value);
            var profile = descriptor?.CreateProfile() ?? DefaultIOProfile.Instance;

            Logging.Log($"[SaveSystem] Triggering {trigger} save using profile '{descriptor?.Name ?? profile.Name}'");
            EventSink.InvokeWorldSaveRequested(profile);
            ResetTimer();
        }

        private void ResetTimer()
        {
            _timer = Math.Max(1.0, SystemSettings.AutoSaveIntervalSeconds.Value);
        }

        private enum SaveTrigger
        {
            Auto,
            Manual
        }

        private static IReadOnlyList<string> GetProfileNames()
        {
            var descriptors = SaveProfileRegistry.Profiles;
            if (descriptors.Count == 0)
                return Array.Empty<string>();

            var names = new List<string>(descriptors.Count);
            foreach (var descriptor in descriptors)
                names.Add(descriptor.Name);
            return names;
        }
    }
}
