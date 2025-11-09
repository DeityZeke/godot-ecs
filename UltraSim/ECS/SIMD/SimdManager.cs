#nullable enable

using System;
using UltraSim;

namespace UltraSim.ECS.SIMD
{
    /// <summary>
    /// Manages SIMD mode selection and hardware capability detection.
    /// Provides centralized SIMD configuration for Core ECS and Systems.
    /// Uses the shared SimdSupport enum captured via HostEnvironment.
    /// </summary>
    public static class SimdManager
    {
        private static SimdMode _systemsMode = SimdMode.Scalar;
        private static bool _showcaseEnabled = false;
        private static SimdSupport _maxHardwareSupport = SimdSupport.Scalar;

        /// <summary>
        /// Initializes the SIMD manager with detected hardware capabilities.
        /// Should be called once at startup with the environment info.
        /// </summary>
        public static void Initialize(SimdSupport maxSupport)
        {
            _maxHardwareSupport = maxSupport;

            // If not in showcase mode, use max hardware capability
            if (!_showcaseEnabled)
            {
                _systemsMode = ConvertToSimdMode(maxSupport);
            }

            // Initialize SIMD operation delegates
            SimdOperations.InitializeSystems(_systemsMode, _showcaseEnabled, ConvertToSimdMode(_maxHardwareSupport));

            Logging.Log($"[SIMD] Initialized - Hardware: {maxSupport}, Systems: {_systemsMode}, Showcase: {_showcaseEnabled}");
        }

        /// <summary>
        /// Whether showcase mode is enabled (manual SIMD selection).
        /// When disabled, uses optimal per-operation mode with hardware fallback.
        /// </summary>
        public static bool ShowcaseEnabled
        {
            get => _showcaseEnabled;
            set
            {
                _showcaseEnabled = value;
                if (!_showcaseEnabled)
                {
                    // Auto mode: use optimal per-operation settings
                    _systemsMode = ConvertToSimdMode(_maxHardwareSupport);
                    Logging.Log($"[SIMD] Showcase Mode DISABLED - Using optimal modes per operation");
                }
                else
                {
                    // Showcase mode: start at Scalar
                    _systemsMode = SimdMode.Scalar;
                    Logging.Log($"[SIMD] Showcase Mode ENABLED - Manual mode starting at Scalar");
                }

                // Reinitialize SIMD operation delegates
                SimdOperations.InitializeSystems(_systemsMode, _showcaseEnabled, ConvertToSimdMode(_maxHardwareSupport));
            }
        }

        /// <summary>
        /// Gets the currently active SIMD mode for Systems.
        /// </summary>
        public static SimdMode GetMode(SimdCategory category)
        {
            // Only Systems category exists now
            return _systemsMode;
        }

        /// <summary>
        /// Sets the SIMD mode for Systems (only works in showcase mode).
        /// </summary>
        public static bool SetMode(SimdCategory category, SimdMode mode)
        {
            if (!_showcaseEnabled)
                return false; // Only allow manual selection in showcase mode

            // Validate hardware support
            if (!IsModeSupported(mode))
            {
                Logging.Log($"[SIMD] Mode change REJECTED - {mode} not supported by hardware", LogSeverity.Warning);
                return false;
            }

            _systemsMode = mode;
            SimdOperations.InitializeSystems(mode, true, ConvertToSimdMode(_maxHardwareSupport)); // Showcase mode = true
            Logging.Log($"[SIMD] SYSTEMS mode changed to {mode}");
            return true;
        }

        /// <summary>
        /// Checks if a SIMD mode is supported by the hardware.
        /// </summary>
        public static bool IsModeSupported(SimdMode mode)
        {
            return mode switch
            {
                SimdMode.Scalar => true,
                SimdMode.Simd128 => _maxHardwareSupport >= SimdSupport.SSE,
                SimdMode.Simd256 => _maxHardwareSupport >= SimdSupport.AVX,
                SimdMode.Simd512 => _maxHardwareSupport >= SimdSupport.AVX512,
                _ => false
            };
        }

        /// <summary>
        /// Gets a human-readable description of hardware capabilities.
        /// </summary>
        public static string GetHardwareInfo()
        {
            return $"Max SIMD: {_maxHardwareSupport}";
        }

        /// <summary>
        /// Converts SimdSupport enum to SimdMode enum.
        /// </summary>
        private static SimdMode ConvertToSimdMode(SimdSupport support)
        {
            return support switch
            {
                SimdSupport.AVX512 => SimdMode.Simd512,
                SimdSupport.AVX2 or SimdSupport.AVX => SimdMode.Simd256,
                SimdSupport.SSE3 or SimdSupport.SSE2 or SimdSupport.SSE => SimdMode.Simd128,
                _ => SimdMode.Scalar
            };
        }
    }
}
