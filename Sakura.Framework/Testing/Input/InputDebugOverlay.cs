// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Linq;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Testing.Input;

/// <summary>
/// Just use for during input manager migration, will remove later
/// </summary>
public partial class InputDebugOverlay : Container
{
    private const int max_log_entries = 12;

    private readonly Container content;
    protected override Container Content => content;

    private readonly List<string> log = new List<string>();

    private SpriteText mouseLine = null!;
    private SpriteText buttonsLine = null!;
    private SpriteText keysLine = null!;
    private SpriteText focusLine = null!;
    private FlowContainer eventLogFlow = null!;
    private SpriteText nonPositionalQueueText = null!;
    private SpriteText positionalQueueText = null!;
    private SpriteText lastConsumerText = null!;

    private readonly MouseState mouseState = new MouseState();
    private readonly HashSet<Key> pressedKeys = new HashSet<Key>();

    private string lastNonPositionalEvent = "(none)";
    private Drawable lastNonPositionalConsumer;

    // The overlay carries its own dormant InputManager so it can show the live queues built over the
    // observed content subtree. This is the Phase 1 answer to "where did my key go?" — it makes the
    // non-positional and positional queues visible for the hovered point.
    private readonly InputManager inputManager = new InputManager();

    /// <summary>
    /// The manager whose queues this overlay renders. Exposed so tests can inspect the live queues.
    /// </summary>
    public InputManager InputManager => inputManager;

    private SpriteText line(Color color) => new SpriteText
    {
        Text = "",
        Color = color,
        Size = new Vector2(440, 18)
    };

    public InputDebugOverlay()
    {
        // The observed subtree lives in `content`; the readout panel is an internal sibling so it
        // never intercepts the input we are trying to watch.
        AddInternal(content = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1)
        });

        AddInternal(new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Margin = new MarginPadding(5),
            AutoSizeAxes = Axes.Both,
            CornerRadius = 5,
            Masking = true,
            Depth = float.MinValue,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1),
                    Color = Color.Black,
                    Alpha = 0.6f
                },
                new FlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FlowDirection.Vertical,
                    Margin = new MarginPadding(8),
                    Spacing = new Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        new SpriteText
                        {
                            Text = "Input Debug Overlay",
                            Color = Color.Yellow,
                            Size = new Vector2(440, 20)
                        },
                        mouseLine = line(Color.White),
                        buttonsLine = line(Color.White),
                        keysLine = line(Color.White),
                        focusLine = line(Color.White),
                        nonPositionalQueueText = line(Color.Cyan),
                        positionalQueueText = line(Color.Orange),
                        lastConsumerText = line(Color.Magenta),
                        eventLogFlow = new FlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FlowDirection.Vertical,
                            Margin = new MarginPadding
                            {
                                Top = 6
                            },
                            Spacing = new Vector2(0, 2)
                        }
                    }
                }
            }
        });
    }

    private void record(string description)
    {
        log.Add(description);
        if (log.Count > max_log_entries)
            log.RemoveRange(0, log.Count - max_log_entries);
    }

    public override void Update()
    {
        base.Update();

        mouseLine.Text = $"Mouse: {mouseState.Position}";
        buttonsLine.Text = $"Buttons: {describePressedButtons()}";
        keysLine.Text = $"Keys: {(pressedKeys.Count == 0 ? "none" : string.Join(", ", pressedKeys))}";

        var focus = GetContainingFocusManager()?.FocusedDrawable;
        focusLine.Text = $"Focus: {focus?.GetType().Name ?? "none"}";

        // Rebuild and render the live input queues for the observed subtree at the current cursor.
        inputManager.BuildQueues(content, mouseState.Position);
        nonPositionalQueueText.Text = $"Non-positional queue: {describeQueue(inputManager.NonPositionalInputQueue)}";
        positionalQueueText.Text = $"Positional queue: {describeQueue(inputManager.PositionalInputQueue)}";

        // Phase 2: which queue entry consumed the most recent non-positional event. The consumer is
        // captured at observe-time (see the overriding handlers); here we just render it.
        string consumer = lastNonPositionalConsumer == null
            ? (lastNonPositionalEvent == "(none)" ? "none yet" : "unhandled")
            : lastNonPositionalConsumer.GetType().Name;
        lastConsumerText.Text = $"Last non-positional: {lastNonPositionalEvent} -> {consumer}";

        // Rebuild the rolling event log (most recent last).
        eventLogFlow.Clear();
        if (log.Count == 0)
        {
            eventLogFlow.Add(new SpriteText { Text = "(no events yet)", Color = Color.Gray, Size = new Vector2(440, 18) });
        }
        else
        {
            foreach (string entry in log)
                eventLogFlow.Add(new SpriteText { Text = entry, Color = Color.LightGreen, Size = new Vector2(440, 18) });
        }
    }

    private static string describeQueue(IReadOnlyList<Drawable> queue)
    {
        if (queue.Count == 0)
            return "(empty)";

        // Front-to-back; show class names, truncated so the panel stays readable.
        var names = queue.Take(8).Select(d => d.GetType().Name);
        string text = string.Join(" > ", names);
        return queue.Count > 8 ? $"{text} > … (+{queue.Count - 8})" : text;
    }

    private string describePressedButtons()
    {
        string[] pressed = new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle }
            .Where(b => mouseState.IsPressed(b))
            .Select(b => b.ToString())
            .ToArray();
        return pressed.Length == 0 ? "none" : string.Join(", ", pressed);
    }

    #region Observing overrides (record, then defer to base)

    public override bool OnMouseMove(MouseEvent e)
    {
        mouseState.Position = e.MouseState.Position;
        // Moves are frequent; only log entry/exit-worthy detail sparingly to keep the log readable.
        return base.OnMouseMove(e);
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        mouseState.SetPressed(e.Button, true);
        record($"MouseDown {e.Button} @ {e.ScreenSpaceMousePosition}");
        return base.OnMouseDown(e);
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        mouseState.SetPressed(e.Button, false);
        record($"MouseUp {e.Button} @ {e.ScreenSpaceMousePosition}");
        return base.OnMouseUp(e);
    }

    public override bool OnScroll(ScrollEvent e)
    {
        record($"Scroll {e.ScrollDelta} @ {e.ScreenSpaceMousePosition}");
        return base.OnScroll(e);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        pressedKeys.Add(e.Key);
        record($"KeyDown {e.Key}{modifierSuffix(e.Modifiers)}{(e.IsRepeat ? " (repeat)" : "")}");
        return noteNonPositionalConsumer($"KeyDown {e.Key}", base.OnKeyDown(e));
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        pressedKeys.Remove(e.Key);
        record($"KeyUp {e.Key}{modifierSuffix(e.Modifiers)}");
        return noteNonPositionalConsumer($"KeyUp {e.Key}", base.OnKeyUp(e));
    }

    public override bool OnTextInput(TextInputEvent e)
    {
        record($"TextInput \"{e.Text}\"");
        return noteNonPositionalConsumer($"TextInput \"{e.Text}\"", base.OnTextInput(e));
    }

    public override bool OnGamepadButtonDown(GamepadButtonEvent e)
    {
        record($"GamepadDown {e.Button}");
        return noteNonPositionalConsumer($"GamepadDown {e.Button}", base.OnGamepadButtonDown(e));
    }

    public override bool OnGamepadButtonUp(GamepadButtonEvent e)
    {
        record($"GamepadUp {e.Button}");
        return noteNonPositionalConsumer($"GamepadUp {e.Button}", base.OnGamepadButtonUp(e));
    }

    #endregion

    /// <summary>
    /// Records the most recent non-positional event and, when it was handled downstream, the
    /// front-most opted-in entry of the live non-positional queue — the entry that gets first crack
    /// and is therefore the consumer in the queue-driven model. This is side-effect free: it only
    /// reads the already-built queue and does not re-invoke any handler.
    /// </summary>
    private bool noteNonPositionalConsumer(string description, bool handled)
    {
        lastNonPositionalEvent = description;

        if (!handled)
        {
            lastNonPositionalConsumer = null;
            return false;
        }

        inputManager.BuildQueues(content, mouseState.Position);
        var queue = inputManager.NonPositionalInputQueue;
        lastNonPositionalConsumer = queue.Count > 0 ? queue[0] : null;
        return true;
    }

    private static string modifierSuffix(KeyModifiers modifiers)
        => modifiers == KeyModifiers.None ? "" : $" [{modifiers}]";
}
