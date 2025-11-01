
using Godot;

namespace UltraSim.ECS
{
    /// <summary>
    /// Interface for modular control panel sections.
    /// Each section is a self-contained UI component that can be added to the ECS Control Panel.
    /// </summary>
    public interface IControlPanelSection
    {
        /// <summary>
        /// Display name for this panel section (shown in collapse header).
        /// </summary>
        string Title { get; }

        /// <summary>
        /// Unique identifier for this panel (used for config persistence).
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Whether this panel is currently visible/expanded.
        /// </summary>
        bool IsExpanded { get; set; }

        /// <summary>
        /// Creates the UI for this panel section.
        /// Called once during panel registration.
        /// </summary>
        Control CreateUI();

        /// <summary>
        /// Update loop called every frame when the control panel is visible.
        /// </summary>
        void Update(double delta);

        /// <summary>
        /// Called when the control panel becomes visible.
        /// </summary>
        void OnShow();

        /// <summary>
        /// Called when the control panel is hidden.
        /// </summary>
        void OnHide();
    }
}
