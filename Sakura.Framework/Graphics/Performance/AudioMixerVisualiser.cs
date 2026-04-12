// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
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

namespace Sakura.Framework.Graphics.Performance;

public class AudioMixerVisualiser : FocusedOverlayContainer, IRemoveFromDrawVisualiser
{
    private readonly Container contentContainer;
    private readonly ScrollableContainer scrollContainer;
    private readonly FlowContainer mainFlow;
    private readonly SpriteText currentTimeText;
    private readonly SpriteText runningTimeText;

    [Resolved]
    private AppHost host { get; set; }

    public AudioMixerVisualiser(IAudioManager audioManager)
    {
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;

        Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = Color.Black,
            Alpha = 0.85f,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });

        Add(new SpriteText
        {
            Text = "Audio Mixer Visualiser (Ctrl + F9)",
            Font = FontUsage.Default.With(size: 30, weight: "Bold"),
            Position = new Vector2(10, 5),
            Color = Color.Yellow,
            RelativeSizeAxes = Axes.X,
            Height = 50,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });

        Add(currentTimeText = new SpriteText
        {
            Text = "",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 50),
            Color = Color.LightYellow,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(runningTimeText = new SpriteText
        {
            Text = "",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(10, 70),
            Color = Color.LightYellow,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(new SpriteText()
        {
            Text =
                $"Sakura Framework v{DebugUtils.GetFrameworkVersion()}",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-10, 50),
            Color = Color.LightYellow,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(new SpriteText()
        {
            Text = $"Running {Logger.AppIdentifier} v{Logger.VersionIdentifier} {(DebugUtils.IsDebugBuild ? "(Debug Build)" : "")}",
            Font = FontUsage.Default.With(size: 16),
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-10, 70),
            Color = Color.LightYellow,
            RelativeSizeAxes = Axes.X,
            Height = 30
        });

        Add(contentContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1, 0.75f),
            Padding = new MarginPadding(20),
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
        });

        // Content Background Dim
        contentContainer.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = Color.Black,
            Alpha = 0.2f,
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre
        });

        contentContainer.Add(scrollContainer = new ScrollableContainer
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });

        scrollContainer.Add(mainFlow = new FlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Width = 1f,
            Spacing = new Vector2(0, 30),
            Padding = new MarginPadding { Top = 10, Left = 10, Right = 10, Bottom = 10 },
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        });

        if (audioManager.TrackMixer != null)
            mainFlow.Add(new MixerGroupDisplay("Track Mixer", audioManager.TrackMixer));

        if (audioManager.SampleMixer != null)
            mainFlow.Add(new MixerGroupDisplay("Sample Mixer", audioManager.SampleMixer));
    }

    public override void Update()
    {
        base.Update();

        if (DrawAlpha <= 0)
            return;

        currentTimeText.Text = $"{DateTime.Now:dd MMMM yyyy HH:mm:ss tt}";
        runningTimeText.Text = $"Has been running for {TimeSpan.FromSeconds(host.AppClock.CurrentTime / 1000):hh\\:mm\\:ss}";
    }

    protected override void PopIn() => this.FadeIn(200, Easing.OutQuint);
    protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);
}

public class MixerGroupDisplay : FlowContainer
{
    private readonly IAudioMixer mixer;
    private readonly FlowContainer channelsFlow;

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
            Padding = new MarginPadding { Left = 20 },
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

        if (DrawAlpha <= 0)
            return;

        lock (mixer.ActiveChannels)
        {
            int currentChannelCount = mixer.ActiveChannels.Count();
            if (channelsFlow.Children.Count != currentChannelCount)
            {
                channelsFlow.Clear();
                foreach (var channel in mixer.ActiveChannels)
                {
                    channelsFlow.Add(new ChannelLevelDisplay($"Channel [{channel.GetHashCode():X}]", channel, false));
                }
            }
        }
    }
}

public class ChannelLevelDisplay : Container
{
    private readonly IAudioChannel channel;
    private readonly SpriteText nameText;
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

    public ChannelLevelDisplay(string name, IAudioChannel channel, bool isMixer)
    {
        this.channel = channel;
        RelativeSizeAxes = Axes.X;
        Width = 1f;
        Height = isMixer ? 45 : 30;

        Add(nameText = new SpriteText
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
            Position = new Vector2(220, 0),
            Size = new Vector2(250, Height)
        });

        var barBackground = new Container
        {
            Position = new Vector2(480, 5),
            Size = new Vector2(300, Height - 10)
        };

        // Dark grey background box
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
            Font = FontUsage.Default.With(size: 12),
            Color = Color.LightGoldenrodYellow,
            Position = new Vector2(790, 0),
            Size = new Vector2(300, Height)
        });

        Add(dbTextRight = new SpriteText
        {
            Font = FontUsage.Default.With(size: 12),
            Color = Color.LightGoldenrodYellow,
            Position = new Vector2(790, Height / 2f),
            Size = new Vector2(300, Height)
        });
    }

    public override void Update()
    {
        base.Update();

        if (DrawAlpha <= 0)
            return;

        statsText.Text = $"Vol: {channel.Volume.Value * 100:0}% | Freq: {channel.Frequency.Value}x";

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

        // Update Text
        dbTextLeft.Text = $"L: {formatDb(leftDb)}";
        dbTextRight.Text = $"R: {formatDb(rightDb)}";

        // Update Main Bars (Turn red if clipping near 0 dB)
        leftVolumeBar.Color = currentLeft > 0.95f ? Color.Red : Color.Lime;
        rightVolumeBar.Color = currentRight > 0.95f ? Color.Red : Color.Lime;

        leftVolumeBar.Width = Math.Max(0.001f, Math.Clamp(currentLeft, 0, 1));
        rightVolumeBar.Width = Math.Max(0.001f, Math.Clamp(currentRight, 0, 1));

        // Update Peak Markers (Subtracting 0.01f keeps the marker inside the bounds of the background box)
        leftPeakMarker.X = Math.Max(0f, peakLeft - 0.01f);
        rightPeakMarker.X = Math.Max(0f, peakRight - 0.01f);
        return;

        string formatDb(float dbValue) => dbValue <= -99f ? "-∞ dB" : $"{dbValue,7:0.000} dB";
    }
}
