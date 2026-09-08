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
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Performance;

public partial class AudioMixerVisualiser : DebugWindow
{
    private const double refresh_interval = 100;

    private const float header_height = 62;
    private const float header_line_height = 18;
    private const float headline_font_size = 14;

    private const float detail_font_size = 12;

    private const string audio_group = "Audio";

    protected override string Title => "Audio Mixer Visualiser (Ctrl + F9)";
    protected override Vector2 DefaultSize => new Vector2(920, 460);
    protected override Vector2 MinSize => new Vector2(700, 220);
    protected override Color Accent => Color.Yellow;

    private static readonly (string Label, string Statistic)[] device_fields =
    {
        ("Device buffer", "SDL Device Buffer (frames)"),
        ("Queued", "SDL Queued"),
        ("Callback", "SDL Callback"),
        ("Mix", "SDL Mix Block"),
        ("Voices", "SDL Active Voices"),
        ("Playback buffer", "BASS Playback Buffer"),
        ("CPU", "BASS CPU Usage")
    };

    private static readonly (string Label, string Statistic)[] fault_fields =
    {
        ("Underruns", "SDL Underruns"),
        ("Starvations", "SDL Voice Starvations"),
        ("Put failures", "SDL Put Failures"),
        ("Starved", "SDL Starved"),
        ("Longest gap", "SDL Longest Mix Gap")
    };

    private readonly IAudioManager audioManager;

    private readonly MixerGroupDisplay? trackGroup;
    private readonly MixerGroupDisplay? sampleGroup;

    private readonly SpriteText engineText;
    private readonly SpriteText deviceText;
    private readonly SpriteText faultText;

    private readonly Dictionary<string, IGlobalStatistic> audioStatistics = new Dictionary<string, IGlobalStatistic>();

    private readonly List<string> lineParts = new List<string>();

    private double nextRefreshTime = double.MinValue;

    public string EngineSummary => engineText.Text;
    public string DeviceSummary => deviceText.Text;
    public string FaultSummary => faultText.Text;

    public AudioMixerVisualiser(IAudioManager audioManager)
    {
        this.audioManager = audioManager;

        var header = new Container
        {
            RelativeSizeAxes = Axes.X,
            Width = 1,
            Height = header_height,
            Padding = new MarginPadding { Left = 10, Right = 10, Top = 8 }
        };

        header.Add(engineText = headerLine(0, Color.Yellow, headline_font_size));
        header.Add(deviceText = headerLine(header_line_height, Color.LightGray, detail_font_size));
        header.Add(faultText = headerLine(header_line_height * 2, Color.Gray, detail_font_size));

        Add(header);

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
            mainFlow.Add(trackGroup = new MixerGroupDisplay("Track Mixer", audioManager.TrackMixer));

        if (audioManager.SampleMixer.IsNotNull())
            mainFlow.Add(sampleGroup = new MixerGroupDisplay("Sample Mixer", audioManager.SampleMixer));

        scrollContainer.Add(mainFlow);

        // padding rather than a height so the mixers keep filling the window as it is resized.
        Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Padding = new MarginPadding
            {
                Top = header_height
            },
            Child = scrollContainer
        });
    }

    private static SpriteText headerLine(float y, Color color, float size) => new SpriteText
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.TopLeft,
        Font = FontUsage.Default.With(size: size),
        Color = color,
        Position = new Vector2(0, y),
        Height = header_line_height
    };

    public override void Update()
    {
        base.Update();

        if (Clock.CurrentTime < nextRefreshTime)
            return;

        nextRefreshTime = Clock.CurrentTime + refresh_interval;

        updateHeader();
    }

    private void updateHeader()
    {
        audioStatistics.Clear();

        foreach (var stat in GlobalStatistics.GetStatistics())
        {
            if (stat.Group == audio_group)
                audioStatistics[stat.Name] = stat;
        }

        engineText.Text = $"Engine: {engineName()}"
                          + $"   Master {audioManager.MasterVolume.Value * 100:0}%"
                          + $"   Track {audioManager.TrackVolume.Value * 100:0}%"
                          + $"   Sample {audioManager.SampleVolume.Value * 100:0}%"
                          + $"   Channels: {trackGroup?.ChannelCount ?? 0} track, {sampleGroup?.ChannelCount ?? 0} sample";

        lineParts.Clear();

        double latency = audioManager.OutputLatencyMs;

        // 0 is the interface's "this backend does not measure it", not a real zero-latency device.
        lineParts.Add(latency > 0 ? $"Latency: {latency:N2} ms" : "Latency: not reported");

        foreach (var (label, name) in device_fields)
        {
            if (audioStatistics.TryGetValue(name, out var stat))
                lineParts.Add($"{label}: {stat.DisplayValue}");
        }

        deviceText.Text = string.Join("   ", lineParts);

        lineParts.Clear();

        bool anyFault = false;

        foreach ((string label, string name) in fault_fields)
        {
            if (!audioStatistics.TryGetValue(name, out var stat))
                continue;

            lineParts.Add($"{label}: {stat.DisplayValue}");
            anyFault |= stat.NumericValue > 0;
        }

        faultText.Text = lineParts.Count > 0
            ? string.Join("   ", lineParts)
            : "This engine publishes no fault counters.";

        faultText.Color = anyFault ? Color.Orange : Color.Gray;
    }

    private string engineName()
    {
        string name = audioManager.GetType().Name;
        string trimmed = name.Replace("AudioManager", string.Empty);

        return trimmed.Length > 0 ? trimmed : name;
    }
}

/// <summary>
/// The level, peak hold, and clip latch of one meter, advanced in wall-clock time.
/// </summary>
public class AudioMeterBallistics
{
    /// <summary>
    /// Time constant for a rising level: the bar covers 1 - 1/e of the distance to a louder reading in
    /// this long. Short, because a meter that lags a transient understates it.
    /// </summary>
    public const float ATTACK_MS = 45f;

    /// <summary>
    /// Time constant for a falling level. Longer than <see cref="ATTACK_MS"/>, so a level that drops
    /// out stays readable rather than vanishing between two glances.
    /// </summary>
    public const float RELEASE_MS = 250f;

    /// <summary>
    /// How long the peak marker sits at a new maximum before it starts falling.
    /// </summary>
    public const float PEAK_HOLD_MS = 1000f;

    /// <summary>
    /// How much of full scale the peak marker gives up per second once the hold has expired.
    /// </summary>
    public const float PEAK_FALL_PER_SECOND = 0.5f;

    /// <summary>
    /// How long <see cref="Clipped"/> stays latched after full scale is seen. A latch with no reset
    /// control would be useless after the first clip of a long session, so it expires instead.
    /// </summary>
    public const float CLIP_HOLD_MS = 2000f;

    /// <summary>
    /// The amplitude that counts as a full scale. Amplitudes are sample peaks in 0..1, so anything at or
    /// above this has no headroom left, whether the device went on to clamp it.
    /// </summary>
    public const float CLIP_THRESHOLD = 0.999f;

    /// <summary>
    /// The longest step the meter will advance by in one call. A hitch, or the first frame after the
    /// window opens, otherwise arrives as one enormous step and snaps the bar to the current level —
    /// clamping makes a stall look like a slow frame rather than a cut.
    /// </summary>
    private const double max_step_ms = 100;

    /// <summary>
    /// The smoothed level to draw the bar at, 0..1.
    /// </summary>
    public float Level { get; private set; }

    /// <summary>
    /// The held peak to draw the marker at, 0..1.
    /// </summary>
    public float Peak { get; private set; }

    /// <summary>
    /// Whether full scale has been seen within the last <see cref="CLIP_HOLD_MS"/>.
    /// </summary>
    public bool Clipped => clipHoldRemaining > 0;

    private double peakHoldRemaining;
    private double clipHoldRemaining;

    /// <summary>
    /// Advances the meter by one frame.
    /// </summary>
    /// <param name="target">Where the bar is heading, as a fraction of the meter's scale.</param>
    /// <param name="amplitude">The raw sample peak this frame, used only to decide clipping.</param>
    /// <param name="elapsedMs">Wall-clock time since the previous call.</param>
    public void Advance(float target, float amplitude, double elapsedMs)
    {
        double step = Math.Clamp(elapsedMs, 0, max_step_ms);

        target = Math.Clamp(target, 0, 1);

        float tau = target > Level ? ATTACK_MS : RELEASE_MS;

        // 1 - e^(-dt/tau) is the fraction of the remaining distance one step of dt covers, which is the
        // same curve for any step size — the property the old per-frame constant did not have.
        Level += (target - Level) * (1 - MathF.Exp(-(float)step / tau));

        if (target >= Peak)
        {
            Peak = target;
            peakHoldRemaining = PEAK_HOLD_MS;
        }
        else
        {
            peakHoldRemaining = Math.Max(0, peakHoldRemaining - step);

            if (peakHoldRemaining <= 0)
                Peak = Math.Max(target, Peak - (float)(step / 1000 * PEAK_FALL_PER_SECOND));
        }

        clipHoldRemaining = amplitude >= CLIP_THRESHOLD
            ? CLIP_HOLD_MS
            : Math.Max(0, clipHoldRemaining - step);
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

    /// <summary>
    /// How many channels are routed into this mixer as of the last membership tick.
    /// </summary>
    public int ChannelCount => displayedChannels.Count;

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

    /// <summary>
    /// Width of the clip light at the right-hand end of the meter.
    /// </summary>
    private const float clip_light_width = 6;

    /// <summary>
    /// Floor for the dB readout, standing in for absolute silence.
    /// </summary>
    private const float silence_db = -100f;

    /// <summary>
    /// The dB range the meter's width covers.
    /// </summary>
    private const float min_db = -60f;

    private const float max_db = 0f;

    private readonly IAudioChannel channel;
    private readonly SpriteText statsText;
    private readonly SpriteText dbTextLeft;
    private readonly SpriteText dbTextRight;

    private readonly Box leftVolumeBar;
    private readonly Box rightVolumeBar;

    private readonly Box leftPeakMarker;
    private readonly Box rightPeakMarker;

    private readonly Box clipLight;

    private readonly AudioMeterBallistics left = new AudioMeterBallistics();
    private readonly AudioMeterBallistics right = new AudioMeterBallistics();

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

        barBackground.Add(clipLight = new Box
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            RelativeSizeAxes = Axes.Y,
            Width = clip_light_width,
            Height = 1,
            Color = Color.DarkRed,
            Alpha = 0.4f
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

        float leftDb = toDecibels(rawLeft);
        float rightDb = toDecibels(rawRight);

        double elapsed = Clock.ElapsedFrameTime;

        left.Advance(toMeterScale(leftDb), rawLeft, elapsed);
        right.Advance(toMeterScale(rightDb), rawRight, elapsed);

        // The bar turns red off the latch rather than off its own width, so a transient that clipped
        // for one frame is still reported after the bar has fallen back.
        leftVolumeBar.Color = left.Clipped ? Color.Red : Color.Lime;
        rightVolumeBar.Color = right.Clipped ? Color.Red : Color.Lime;

        leftVolumeBar.Width = Math.Max(0.001f, left.Level);
        rightVolumeBar.Width = Math.Max(0.001f, right.Level);

        // Subtracting the marker's own width keeps it inside the bounds of the background box.
        leftPeakMarker.X = Math.Max(0f, left.Peak - 0.01f);
        rightPeakMarker.X = Math.Max(0f, right.Peak - 0.01f);

        bool clipped = left.Clipped || right.Clipped;

        clipLight.Color = clipped ? Color.Red : Color.DarkRed;
        clipLight.Alpha = clipped ? 1f : 0.4f;

        if (Clock.CurrentTime < nextTextUpdate)
            return;

        nextTextUpdate = Clock.CurrentTime + text_interval;

        statsText.Text = $"Vol: {channel.Volume.Value * 100:0}% | Freq: {channel.Frequency.Value}x";
        dbTextLeft.Text = $"L: {formatDb(leftDb)}";
        dbTextRight.Text = $"R: {formatDb(rightDb)}";

        return;

        string formatDb(float dbValue) => dbValue <= -99f ? "-∞ dB" : $"{dbValue,7:0.000} dB";
    }

    private static float toDecibels(float amplitude) => amplitude > 0.00001f ? 20f * MathF.Log10(amplitude) : silence_db;

    private static float toMeterScale(float decibels) => Math.Clamp((decibels - min_db) / (max_db - min_db), 0, 1);
}
