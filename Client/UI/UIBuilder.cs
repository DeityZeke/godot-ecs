#nullable enable

using System;
using Godot;

namespace UltraSim.UI
{
    /// <summary>
    /// Base class for UI panels with helper methods for quick widget creation.
    /// Inherits from Control for layout support, anchors, and theme inheritance.
    /// Add instances to a CanvasLayer for Z-ordering control.
    ///
    /// Design philosophy:
    /// - Create*() methods: Factory methods that create configured controls without adding to scene tree
    /// - AddContainer*() methods: Create and add controls to specified containers
    /// - Add*() methods: Create and add controls at absolute positions (for overlays/debugging)
    /// </summary>
    public abstract partial class UIBuilder : Control
    {
        #region Factory Methods (Create but don't add)

        /// <summary>
        /// Creates a configured label without adding it to the scene tree.
        /// Use this when you want to manually add it to a container or customize further.
        /// </summary>
        protected Label CreateLabel(
            string text,
            Color? color = null,
            int fontSize = 16,
            HorizontalAlignment hAlign = HorizontalAlignment.Left,
            VerticalAlignment vAlign = VerticalAlignment.Center)
        {
            var label = new Label
            {
                Text = text,
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign
            };

            if (color.HasValue)
                label.AddThemeColorOverride("font_color", color.Value);

            if (fontSize != 16)
                label.AddThemeFontSizeOverride("font_size", fontSize);

            return label;
        }

        /// <summary>
        /// Creates a configured button without adding it to the scene tree.
        /// </summary>
        protected Button CreateButton(
            string text,
            Action? onPressed = null,
            Vector2? size = null,
            Color? bgColor = null)
        {
            var button = new Button
            {
                Text = text,
                Size = size ?? new Vector2(100, 40)
            };

            if (bgColor.HasValue)
                button.AddThemeColorOverride("button_color", bgColor.Value);

            if (onPressed != null)
                button.Pressed += () => onPressed();

            return button;
        }

        /// <summary>
        /// Creates a configured checkbox without adding it to the scene tree.
        /// </summary>
        protected CheckBox CreateCheckBox(
            bool initialValue = false,
            Action<bool>? onToggled = null)
        {
            var checkBox = new CheckBox
            {
                ButtonPressed = initialValue,
                FocusMode = FocusModeEnum.None
            };

            if (onToggled != null)
                checkBox.Toggled += (pressed) => onToggled(pressed);

            return checkBox;
        }

        /// <summary>
        /// Creates a configured LineEdit (text input) without adding it to the scene tree.
        /// </summary>
        protected LineEdit CreateLineEdit(
            string placeholder = "",
            string initialText = "",
            Action<string>? onTextChanged = null,
            Vector2? size = null)
        {
            var lineEdit = new LineEdit
            {
                PlaceholderText = placeholder,
                Text = initialText,
                Size = size ?? new Vector2(200, 30)
            };

            if (onTextChanged != null)
                lineEdit.TextChanged += (newText) => onTextChanged(newText);

            return lineEdit;
        }

        /// <summary>
        /// Creates an empty control (useful for spacers).
        /// </summary>
        protected Control CreateSpacer(float width = 0, float height = 0)
        {
            return new Control
            {
                CustomMinimumSize = new Vector2(width, height)
            };
        }

        #endregion

        #region Container Helpers (Add to containers)

        /// <summary>
        /// Creates and adds a label to the specified GridContainer.
        /// Returns the label for further customization.
        /// </summary>
        protected Label AddContainerLabel(
            GridContainer container,
            string text,
            Color? color = null,
            int fontSize = 16,
            HorizontalAlignment hAlign = HorizontalAlignment.Left,
            VerticalAlignment vAlign = VerticalAlignment.Center)
        {
            var label = CreateLabel(text, color, fontSize, hAlign, vAlign);
            container.AddChild(label);
            return label;
        }

        /// <summary>
        /// Creates and adds a label to the specified VBoxContainer.
        /// </summary>
        protected Label AddContainerLabel(
            VBoxContainer container,
            string text,
            Color? color = null,
            int fontSize = 16,
            HorizontalAlignment hAlign = HorizontalAlignment.Left)
        {
            var label = CreateLabel(text, color, fontSize, hAlign);
            container.AddChild(label);
            return label;
        }

        /// <summary>
        /// Creates and adds a label to the specified HBoxContainer.
        /// </summary>
        protected Label AddContainerLabel(
            HBoxContainer container,
            string text,
            Color? color = null,
            int fontSize = 16,
            VerticalAlignment vAlign = VerticalAlignment.Center)
        {
            var label = CreateLabel(text, color, fontSize, vAlign: vAlign);
            container.AddChild(label);
            return label;
        }

        /// <summary>
        /// Creates and adds a button to the specified container.
        /// </summary>
        protected Button AddContainerButton(
            Container container,
            string text,
            Action? onPressed = null,
            Vector2? size = null)
        {
            var button = CreateButton(text, onPressed, size);
            container.AddChild(button);
            return button;
        }

        /// <summary>
        /// Creates and adds a checkbox to the specified container.
        /// </summary>
        protected CheckBox AddContainerCheckBox(
            Container container,
            bool initialValue = false,
            Action<bool>? onToggled = null)
        {
            var checkBox = CreateCheckBox(initialValue, onToggled);
            container.AddChild(checkBox);
            return checkBox;
        }

        #endregion

        #region Positional Helpers (Absolute positioning)

        /// <summary>
        /// Creates and adds a label at an absolute position.
        /// Useful for overlays, debugging UI, or non-responsive layouts.
        /// </summary>
        protected Label AddLabel(
            string text,
            float x,
            float y,
            Color? color = null,
            int fontSize = 16,
            int z = 0)
        {
            var label = CreateLabel(text, color, fontSize);
            label.Position = new Vector2(x, y);
            label.ZIndex = z;
            AddChild(label);
            return label;
        }

        /// <summary>
        /// Creates and adds a button at an absolute position.
        /// </summary>
        protected Button AddButton(
            string text,
            float x,
            float y,
            Action? onPressed = null,
            Vector2? size = null,
            int z = 0)
        {
            var button = CreateButton(text, onPressed, size);
            button.Position = new Vector2(x, y);
            button.ZIndex = z;
            AddChild(button);
            return button;
        }

        #endregion

        #region Layout Utilities

        /// <summary>
        /// Creates a GridContainer with common configuration.
        /// </summary>
        protected GridContainer CreateGrid(
            int columns,
            int hSeparation = 16,
            int vSeparation = 8)
        {
            var grid = new GridContainer { Columns = columns };
            grid.AddThemeConstantOverride("h_separation", hSeparation);
            grid.AddThemeConstantOverride("v_separation", vSeparation);
            return grid;
        }

        /// <summary>
        /// Creates an HBoxContainer with common configuration.
        /// </summary>
        protected HBoxContainer CreateHBox(int separation = 8)
        {
            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", separation);
            return hbox;
        }

        /// <summary>
        /// Creates a VBoxContainer with common configuration.
        /// </summary>
        protected VBoxContainer CreateVBox(int separation = 8)
        {
            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", separation);
            return vbox;
        }

        /// <summary>
        /// Creates a MarginContainer with uniform margin on all sides.
        /// </summary>
        protected MarginContainer CreateMargin(int margin = 8)
        {
            var marginContainer = new MarginContainer();
            marginContainer.AddThemeConstantOverride("margin_left", margin);
            marginContainer.AddThemeConstantOverride("margin_right", margin);
            marginContainer.AddThemeConstantOverride("margin_top", margin);
            marginContainer.AddThemeConstantOverride("margin_bottom", margin);
            return marginContainer;
        }

        /// <summary>
        /// Creates a MarginContainer with individual margin values.
        /// </summary>
        protected MarginContainer CreateMargin(int left, int top, int right, int bottom)
        {
            var marginContainer = new MarginContainer();
            marginContainer.AddThemeConstantOverride("margin_left", left);
            marginContainer.AddThemeConstantOverride("margin_right", right);
            marginContainer.AddThemeConstantOverride("margin_top", top);
            marginContainer.AddThemeConstantOverride("margin_bottom", bottom);
            return marginContainer;
        }

        /// <summary>
        /// Creates a PanelContainer (styled background panel).
        /// </summary>
        protected PanelContainer CreatePanel()
        {
            return new PanelContainer();
        }

        /// <summary>
        /// Adds a spacer to a GridContainer (fills two columns for proper grid alignment).
        /// </summary>
        protected void AddGridSpacer(GridContainer container, int height = 8)
        {
            var spacer1 = CreateSpacer(0, height);
            var spacer2 = CreateSpacer(0, height);
            container.AddChild(spacer1);
            container.AddChild(spacer2);
        }

        /// <summary>
        /// Adds a single spacer to any container.
        /// </summary>
        protected void AddSpacer(Container container, float width = 0, float height = 0)
        {
            var spacer = CreateSpacer(width, height);
            container.AddChild(spacer);
        }

        /// <summary>
        /// Creates a section header label (larger font, colored).
        /// </summary>
        protected Label CreateHeader(
            string text,
            int fontSize = 18,
            Color? color = null)
        {
            var headerColor = color ?? new Color(0.8f, 0.8f, 1.0f);
            return CreateLabel(text, headerColor, fontSize);
        }

        /// <summary>
        /// Adds a section header to a GridContainer (spans both columns).
        /// </summary>
        protected void AddGridHeader(GridContainer container, string text, int fontSize = 14)
        {
            var header = CreateHeader(text, fontSize);
            container.AddChild(header);

            // Empty cell for second column
            container.AddChild(new Control());
        }

        #endregion
    }
}
