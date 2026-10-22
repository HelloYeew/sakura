// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Concurrent;
using Sakura.Framework.Allocation;
using Sakura.Framework.Configurations;
using Sakura.Framework.Development;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Graphics.Performance;

/// <summary>
/// The log as it's written, over the bottom-left of the app, newest at the bottom, like a game's chat: each entry fades
/// in, stays a few seconds (longer for warnings and errors) and fades away. For a platform with no console to read the
/// log on, such as a phone, and for watching it without one.
/// Shown by <see cref="FrameworkSetting.ShowLogOverlay"/> (Ctrl+Shift+L). It takes no input, so it never gets in the
/// way.
/// </summary>
/// <remarks>
/// A debug build shows verbose entries and up, as the developer is the one reading. Otherwise it shows warnings and
/// errors only: the rest is detail meant for the developer, written to the log files.
/// </remarks>
public partial class LogOverlay : Container, IRemoveFromDrawVisualiser
{
    // Few enough to leave the game in view when the log is busy.
    private const int max_entries = 10;

    private const double fade_in_ms = 150;
    private const double fade_out_ms = 800;

    private const float background_alpha = 0.55f;

    private const float edge_margin = 10;

    private const float time_width = 52;
    private const float level_width = 58;
    private const float column_spacing = 6;
    private const float line_padding = 5;
    private const float row_height = 18;

    private static readonly FontUsage font = FontUsage.Default.With(size: 13);
    private static readonly FontUsage level_font = FontUsage.Default.With(size: 11, weight: "Bold");

    private static readonly Color time_color = Color.FromArgb(140, 160, 185);

    private static readonly LogLevel minimum_level = DebugUtils.IsDebugBuild ? LogLevel.Verbose : LogLevel.Warning;

    // Filled on the logger's thread and emptied on the update thread. It keeps the last few while the overlay is
    // hidden, so turning it on shows what led up to that.
    private readonly ConcurrentQueue<LogEntry> pending = new ConcurrentQueue<LogEntry>();

    private Reactive<bool> shown = null!;
    private FlowContainer lines = null!;

    [Resolved]
    private AppHost host { get; set; } = null!;

    public LogOverlay()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        Padding = new MarginPadding(edge_margin);
    }

    public override void Load()
    {
        base.Load();

        Add(lines = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Spacing = new Vector2(0, 1)
        });

        shown = host.FrameworkConfigManager.Get<bool>(FrameworkSetting.ShowLogOverlay);
        shown.ValueChanged += e => Alpha = e.NewValue ? 1 : 0;
        Alpha = shown.Value ? 1 : 0;

        Logger.NewEntry += onNewEntry;
    }

    private void onNewEntry(LogEntry entry)
    {
        if (entry.Level < minimum_level)
            return;

        pending.Enqueue(entry);

        while (pending.Count > max_entries && pending.TryDequeue(out _))
        {
        }
    }

    public override void Update()
    {
        base.Update();

        if (!shown.Value)
            return;

        while (pending.TryDequeue(out var entry))
            add(entry);
    }

    private void add(LogEntry entry)
    {
        // The oldest go first, so a burst of entries doesn't cover the app.
        while (lines.Children.Count >= max_entries)
            lines.Remove(lines.Children[0]);

        var style = styleOf(entry.Level);
        float messageWidth = ChildSize.X - line_padding * 2 - time_width - level_width - column_spacing * 2;

        var line = new Container
        {
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = style.Background,
                    Alpha = background_alpha
                },
                new FlowContainer
                {
                    Direction = FlowDirection.Horizontal,
                    AutoSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = line_padding, Right = line_padding, Top = 1, Bottom = 1 },
                    Spacing = new Vector2(column_spacing, 0),
                    Children = new Drawable[]
                    {
                        cell(time_width, new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                            Font = font,
                            Color = time_color
                        }),
                        cell(level_width, new Container
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Color = style.Accent,
                                    Alpha = 0.2f
                                },
                                new SpriteText
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Text = style.Label,
                                    Font = level_font,
                                    Color = style.Accent
                                }
                            }
                        }),
                        new Container
                        {
                            AutoSizeAxes = Axes.X,
                            Height = row_height,
                            Child = new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = messageOf(entry),
                                Font = font,
                                Color = style.Message,
                                Truncate = true,
                                MaxWidth = Math.Max(0, messageWidth)
                            }
                        }
                    }
                }
            }
        };

        lines.Add(line);
        line.FadeInFromZero(fade_in_ms).Wait(style.Lifetime).FadeOut(fade_out_ms, Easing.InQuad);
        line.Expire();

        // A fixed-width cell, so the columns line up from one line to the next.
        static Container cell(float width, Drawable content) => new Container
        {
            Size = new Vector2(width, row_height),
            Child = content
        };
    }

    /// <summary>
    /// The first line: an exception's stack trace would fill the screen, and is in the log files whole.
    /// </summary>
    private static string messageOf(LogEntry entry)
    {
        string message = entry.Message;
        int newline = message.IndexOf('\n');

        if (newline < 0)
            return message;

        int moreLines = message.AsSpan(newline).Count('\n');
        return $"{message[..newline].TrimEnd('\r')} (+{moreLines} more {(moreLines == 1 ? "line" : "lines")})";
    }

    // Lifetime is how long an entry stays before it fades, in milliseconds: the routine briefly, what needs attention
    // longer.
    private readonly record struct LevelStyle(string Label, Color Accent, Color Message, Color Background, double Lifetime);

    private static LevelStyle styleOf(LogLevel level) => level switch
    {
        LogLevel.Debug => new LevelStyle("DEBUG", Color.FromArgb(150, 150, 150), Color.FromArgb(175, 175, 175), Color.Black, 4000),
        LogLevel.Verbose => new LevelStyle("VERBOSE", Color.FromArgb(100, 180, 255), Color.White, Color.Black, 4000),
        LogLevel.Warning => new LevelStyle("WARNING", Color.FromArgb(255, 196, 64), Color.FromArgb(255, 232, 175), Color.FromArgb(55, 40, 0), 7000),
        LogLevel.Error => new LevelStyle("ERROR", Color.FromArgb(255, 95, 90), Color.FromArgb(255, 208, 204), Color.FromArgb(70, 10, 10), 10_000),
        _ => new LevelStyle("FATAL", Color.FromArgb(255, 70, 170), Color.FromArgb(255, 205, 232), Color.FromArgb(80, 0, 40), 10_000)
    };

    protected override void Dispose(bool isDisposing)
    {
        Logger.NewEntry -= onNewEntry;
        base.Dispose(isDisposing);
    }
}
