// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ObjectExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Performance;

public partial class AudioMixerVisualiser : DebugWindow
{
    protected override string Title => "Audio Mixer Visualiser (Ctrl + F9)";
    protected override Vector2 DefaultSize => new Vector2(920, 420);
    protected override Vector2 MinSize => new Vector2(700, 180);
    protected override Color Accent => Color.Yellow;

    public AudioMixerVisualiser(IAudioManager audioManager)
    {
        var scrollContainer = new ScrollableContainer
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        };

        var mainFlow = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Width = 1f,
            Spacing = new Vector2(0, 30),
            Padding = new MarginPadding(10),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        };

        if (audioManager.TrackMixer.IsNotNull())
            mainFlow.Add(new MixerGroupDisplay("Track Mixer", audioManager.TrackMixer));

        if (audioManager.SampleMixer.IsNotNull())
            mainFlow.Add(new MixerGroupDisplay("Sample Mixer", audioManager.SampleMixer));

        scrollContainer.Add(mainFlow);
        Add(scrollContainer);
    }
}

public partial class MixerGroupDisplay : FlowContainer
{
    /// <summary>
    /// How often the mixer's membership is re-read. Channels do not come and go at frame rate, and
    /// the read has to take a lock the audio thread also wants.
    /// </summary>
    private const double membership_interval = 100;

    private readonly IAudioMixer mixer;
    private readonly FlowContainer channelsFlow;

    /// <summary>
    /// The channel each row is showing, in row order, so identity can diff membership. The
    /// count alone is not enough: swapping one channel for another leaves the count unchanged and
    /// would leave every row reporting the wrong channel.
    /// </summary>
    private readonly List<IAudioChannel> displayedChannels = new List<IAudioChannel>();

    private double nextMembershipCheck = double.MinValue;

    public MixerGroupDisplay(string name, IAudioMixer mixer)
    {
        this.mixer = mixer;
        Direction = FlowDirection.Vertical;
        Spacing = new Vector2(0, 5);
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Width = 1f;

        Add(new ChannelLevelDisplay(name, mixer, true));

        Add(channelsFlow = new FlowContainer
        {
            Padding = new MarginPadding
            {
                Left = 20
            },
            Direction = FlowDirection.Vertical,
            Spacing = new Vector2(0, 2),
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Width = 1f
        });
    }

    public override void Update()
    {
        base.Update();

        if (Clock.CurrentTime < nextMembershipCheck)
            return;

        nextMembershipCheck = Clock.CurrentTime + membership_interval;

        // The BASS mixer hands out its live backing list and relies on callers locking the same
        // object; the SDL mixers hand out an immutable snapshot where the lock is merely harmless.
        // Either way the copy is taken under the lock, and the drawables are built outside it, so the
        // audio thread is not kept waiting on UI construction.
        IAudioChannel[] current;

        lock (mixer.ActiveChannels)
        {
            var channels = new List<IAudioChannel>();

            foreach (var channel in mixer.ActiveChannels)
                channels.Add(channel);

            current = channels.ToArray();
        }

        if (!membershipChanged(current))
            return;

        channelsFlow.Clear();
        displayedChannels.Clear();

        foreach (var channel in current)
        {
            channelsFlow.Add(new ChannelLevelDisplay($"Channel [{channel.GetHashCode():X}]", channel, false));
            displayedChannels.Add(channel);
        }
    }

    private bool membershipChanged(IAudioChannel[] current)
    {
        if (current.Length != displayedChannels.Count)
            return true;

        for (int i = 0; i < current.Length; i++)
        {
            if (!ReferenceEquals(current[i], displayedChannels[i]))
                return true;
        }

        return false;
    }
}

public partial class ChannelLevelDisplay : Container
{
    /// <summary>
    /// How often the numbers are reformatted. The bars are animation and stay per-frame; the text is
    /// six formatted strings per channel that nobody can read at 240 Hz.
    /// </summary>
    private const double text_interval = 100;

    /// <summary>
    /// Left edge of the meter, leaving room for the name and stats columns.
    /// </summary>
    private const float meter_left = 460;

    /// <summary>
    /// Width reserved on the right for the two dB readouts.
    /// </summary>
    private const float db_column = 130;

    private readonly IAudioChannel channel;
    private readonly SpriteText statsText;
    private readonly SpriteText dbTextLeft;
    private readonly SpriteText dbTextRight;

    private readonly Box leftVolumeBar;
    private readonly Box rightVolumeBar;

    private readonly Box leftPeakMarker;
    private readonly Box rightPeakMarker;

    private float currentLeft;
    private float currentRight;
    private float peakLeft;
    private float peakRight;

    private double nextTextUpdate = double.MinValue;

    public ChannelLevelDisplay(string name, IAudioChannel channel, bool isMixer)
    {
        this.channel = channel;
        RelativeSizeAxes = Axes.X;
        Width = 1f;
        Height = isMixer ? 45 : 30;

        Add(new SpriteText
        {
            Text = name,
            Font = FontUsage.Default.With(size: isMixer ? 20 : 16, weight: isMixer ? "Bold" : "Regular"),
            Color = isMixer ? Color.Yellow : Color.LightGray,
            Position = new Vector2(0, 0),
            Size = new Vector2(200, Height)
        });

        Add(statsText = new SpriteText
        {
            Font = FontUsage.Default.With(size: 14),
            Color = Color.White,
            Position = new Vector2(210, 0),
            Size = new Vector2(240, Height)
        });

        // a window can be resized, and the old full-screen overlay's hard-coded 300px meter would then either overflow or
        // leave the right half of the row empty.
        var barBackground = new Container
        {
            RelativeSizeAxes = Axes.X,
            Width = 1,
            Height = Height - 10,
            Margin = new MarginPadding { Left = meter_left, Right = db_column, Top = 5 }
        };

        // Dark gray background box
        barBackground.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = Color.DarkGray,
            Alpha = 0.3f
        });

        // The actual green/red volume bars
        barBackground.Add(leftVolumeBar = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.001f, 0.48f),
            Color = Color.Lime
        });
        barBackground.Add(rightVolumeBar = new Box
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(0.001f, 0.48f),
            Color = Color.Lime
        });

        // The floating peak markers
        barBackground.Add(leftPeakMarker = new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            RelativePositionAxes = Axes.X,
            Size = new Vector2(0.01f, 0.48f),
            Color = Color.White,
        });

        barBackground.Add(rightPeakMarker = new Box
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            RelativeSizeAxes = Axes.Both,
            RelativePositionAxes = Axes.X,
            Size = new Vector2(0.01f, 0.48f),
            Color = Color.White
        });

        Add(barBackground);

        Add(dbTextLeft = new SpriteText
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Font = FontUsage.Default.With(size: 12),
            Color = Color.LightGoldenrodYellow,
            Position = new Vector2(-4, 0),
            Size = new Vector2(db_column - 8, Height)
        });

        Add(dbTextRight = new SpriteText
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Font = FontUsage.Default.With(size: 12),
            Color = Color.LightGoldenrodYellow,
            Position = new Vector2(-4, Height / 2f),
            Size = new Vector2(db_column - 8, Height)
        });
    }

    public override void Update()
    {
        base.Update();

        float rawLeft = channel.AmplitudeLeft;
        float rawRight = channel.AmplitudeRight;

        // Calculate actual dB text (-100f is our floor for absolute silence)
        float leftDb = rawLeft > 0.00001f ? 20f * MathF.Log10(rawLeft) : -100f;
        float rightDb = rawRight > 0.00001f ? 20f * MathF.Log10(rawRight) : -100f;

        // Map the dB values to a visual percentage (0.0 to 1.0) for a -60dB to 0dB range
        const float min_db = -60f;
        const float max_db = 0f;

        float targetLeft = Math.Clamp((leftDb - min_db) / (max_db - min_db), 0, 1);
        float targetRight = Math.Clamp((rightDb - min_db) / (max_db - min_db), 0, 1);

        // Smooth the visual bars so they look fluid
        currentLeft += (targetLeft - currentLeft) * 0.2f;
        currentRight += (targetRight - currentRight) * 0.2f;

        // Track and slowly decay the visual peak markers
        peakLeft = Math.Max(targetLeft, peakLeft - 0.005f);
        peakRight = Math.Max(targetRight, peakRight - 0.005f);

        // Update Main Bars (Turn red if clipping near 0 dB)
        leftVolumeBar.Color = currentLeft > 0.95f ? Color.Red : Color.Lime;
        rightVolumeBar.Color = currentRight > 0.95f ? Color.Red : Color.Lime;

        leftVolumeBar.Width = Math.Max(0.001f, Math.Clamp(currentLeft, 0, 1));
        rightVolumeBar.Width = Math.Max(0.001f, Math.Clamp(currentRight, 0, 1));

        // Update Peak Markers (Subtracting 0.01f keeps the marker inside the bounds of the background box)
        leftPeakMarker.X = Math.Max(0f, peakLeft - 0.01f);
        rightPeakMarker.X = Math.Max(0f, peakRight - 0.01f);

        if (Clock.CurrentTime < nextTextUpdate)
            return;

        nextTextUpdate = Clock.CurrentTime + text_interval;

        statsText.Text = $"Vol: {channel.Volume.Value * 100:0}% | Freq: {channel.Frequency.Value}x";
        dbTextLeft.Text = $"L: {formatDb(leftDb)}";
        dbTextRight.Text = $"R: {formatDb(rightDb)}";

        return;

        string formatDb(float dbValue) => dbValue <= -99f ? "-∞ dB" : $"{dbValue,7:0.000} dB";
    }
}
