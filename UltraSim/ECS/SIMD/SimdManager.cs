#nullable enable

using System;
using UltraSim.Logging;

namespace UltraSim.ECS.SIMD
{
    /// <summary>
    /// Manages SIMD mode selection and hardware capability detection.
    /// Provides centralized SIMD configuration for Core ECS and Systems.
    /// Uses the existing SimdSupport enum from IEnvironmentInfo.
    /// </summary>
    public static class SimdManager
    {
        private static SimdMode _coreMode = SimdMode.Scalar;
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
                _coreMode = ConvertToSimdMode(maxSupport);
                _systemsMode = ConvertToSimdMode(maxSupport);
            }

            // Initialize SIMD operation delegates
            SimdOperations.InitializeCore(_coreMode);
            SimdOperations.InitializeSystems(_systemsMode, _showcaseEnabled, ConvertToSimdMode(_maxHardwareSupport));

            Logger.Log($"[SIMD] Initialized - Hardware: {maxSupport}, Core: {_coreMode}, Systems: {_systemsMode}, Showcase: {_showcaseEnabled}");
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
                    _coreMode = ConvertToSimdMode(_maxHardwareSupport);
                    _systemsMode = ConvertToSimdMode(_maxHardwareSupport);
                    Logger.Log($"[SIMD] Showcase Mode DISABLED - Using optimal modes per operation");
                }
                else
                {
                    // Showcase mode: start at Scalar
                    _coreMode = SimdMode.Scalar;
                    _systemsMode = SimdMode.Scalar;
                    Logger.Log($"[SIMD] Showcase Mode ENABLED - Manual mode starting at Scalar");
                }

                // Reinitialize SIMD operation delegates
                SimdOperations.InitializeCore(_coreMode);
                SimdOperations.InitializeSystems(_systemsMode, _showcaseEnabled, ConvertToSimdMode(_maxHardwareSupport));
            }
        }

        /// <summary>
        /// Gets the currently active SIMD mode for a category.
        /// </summary>
        public static SimdMode GetMode(SimdCategory category)
        {
            return category switch
            {
                SimdCategory.Core => _coreMode,
                SimdCategory.Systems => _systemsMode,
                _ => SimdMode.Scalar
            };
        }

        /// <summary>
        /// Sets the SIMD mode for a category (only works in showcase mode).
        /// </summary>
        public static bool SetMode(SimdCategory category, SimdMode mode)
        {
            if (!_showcaseEnabled)
                return false; // Only allow manual selection in showcase mode

            // Validate hardware support
            if (!IsModeSupported(mode))
            {
                Logger.Log($"[SIMD] Mode change REJECTED - {mode} not supported by hardware", LogSeverity.Warning);
                return false;
            }

            switch (category)
            {
                case SimdCategory.Core:
                    _coreMode = mode;
                    SimdOperations.InitializeCore(mode); // Reinitialize Core delegates
                    Logger.Log($"[SIMD] CORE mode changed to {mode}");
                    return true;
                case SimdCategory.Systems:
                    _systemsMode = mode;
                    SimdOperations.InitializeSystems(mode, true, ConvertToSimdMode(_maxHardwareSupport)); // Showcase mode = true
                    Logger.Log($"[SIMD] SYSTEMS mode changed to {mode}");
                    return true;
                default:
                    return false;
            }
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
