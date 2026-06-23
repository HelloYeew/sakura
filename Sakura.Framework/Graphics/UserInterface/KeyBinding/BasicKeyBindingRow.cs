// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input.Bindings;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.UserInterface.KeyBinding;

/// <summary>
/// Default visual implementation of <see cref="KeyBindingRow{T}"/>: the action name on the left and,
/// on the right, one capture button per slot. Clicking a slot starts capture; a small clear button
/// next to each slot unbinds it. The active slot highlights while capturing.
/// </summary>
/// <typeparam name="T">The action enum type.</typeparam>
public partial class BasicKeyBindingRow<T> : KeyBindingRow<T> where T : struct, Enum
{
    private readonly Box background;
    private readonly SpriteText actionLabel;
    private readonly List<SpriteText> slotLabels = new List<SpriteText>();
    private readonly List<Box> slotBackgrounds = new List<Box>();
    private readonly List<Drawable> captureButtons = new List<Drawable>();
    private readonly List<Drawable> clearButtons = new List<Drawable>();

    /// <summary>
    /// The clickable capture button for a slot. Useful for positioning input relative to a slot.
    /// </summary>
    public Drawable CaptureButton(int slotIndex) => captureButtons[slotIndex];

    /// <summary>
    /// The clickable clear (unbind) button for a slot.
    /// </summary>
    public Drawable ClearButton(int slotIndex) => clearButtons[slotIndex];

    private const float slot_width = 140;
    private const float clear_width = 22;
    private const float gap = 6;

    /// <summary>
    /// Slot background colour when idle.
    /// </summary>
    public Color IdleColor { get; init; } = Color.DarkSlateGray;

    /// <summary>
    /// Slot background colour while capturing.
    /// </summary>
    public Color CapturingColor { get; init; } = Color.DarkSlateBlue;

    public BasicKeyBindingRow(T action, IEnumerable<KeyCombination> current, int slotCount)
        : base(action, current, slotCount)
    {
        background = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = Color.Black,
            Alpha = 0.3f,
        };

        actionLabel = new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Margin = new MarginPadding { Left = 10 },
        };

        var children = new List<Drawable> { background, actionLabel };

        float right = 10;

        for (int i = 0; i < SlotCount; i++)
        {
            int slotIndex = i;

            // Clear button (rightmost of each slot group).
            var clear = new SlotClickable
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Size = new Vector2(clear_width, 24),
                Margin = new MarginPadding { Right = right },
                Action = () => ClearSlot(slotIndex),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(1),
                        Color = Color.DarkRed,
                        Alpha = 0.6f
                    },
                    new SpriteText
                    {
                        Text = "x",
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre
                    },
                },
            };
            children.Add(clear);
            clearButtons.Add(clear);
            right += clear_width + 2;

            // Capture button.
            var slotBg = new Box { RelativeSizeAxes = Axes.Both, Size = new Vector2(1), Color = IdleColor };
            var slotLabel = new SpriteText { Anchor = Anchor.Centre, Origin = Anchor.Centre };

            var capture = new SlotClickable
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Size = new Vector2(slot_width, 24),
                Margin = new MarginPadding { Right = right },
                Action = () => BeginCapture(slotIndex),
                Children = new Drawable[] { slotBg, slotLabel },
            };
            children.Add(capture);
            captureButtons.Add(capture);
            right += slot_width + gap;

            slotBackgrounds.Add(slotBg);
            slotLabels.Add(slotLabel);
        }

        Children = children.ToArray();
    }

    protected override void UpdateActionText(string text) => actionLabel.Text = text;

    protected override void UpdateSlotText(int slotIndex, string text) => slotLabels[slotIndex].Text = text;

    protected override void OnCaptureStarted(int slotIndex) => slotBackgrounds[slotIndex].Color = CapturingColor;

    protected override void OnCaptureEnded(int slotIndex) => slotBackgrounds[slotIndex].Color = IdleColor;

    /// <summary>
    /// A clickable region used for the slot capture and clear buttons. Unlike a plain
    /// <see cref="ClickableContainer"/>, it does not steal focus so the row retains focus and keeps
    /// receiving capture input.
    /// </summary>
    private partial class SlotClickable : ClickableContainer
    {
        public override bool AcceptsFocus => false;
    }
}
