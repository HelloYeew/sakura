// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.UserInterface;

/// <summary>
/// A pad to tap along to a beat on, which measures how late the taps land (<see cref="Calibration"/>). The beat is
/// heard or seen, never both, so a run measures one way the delay reaches the player: a game runs it once with
/// <see cref="CalibrationCue.Sound"/> and once with <see cref="CalibrationCue.Flash"/> for both its offsets (see
/// <see cref="TapCalibration"/>). Any key but the ones that leave, any mouse button, and a finger anywhere on it is a
/// tap.
/// </summary>
/// <remarks>
/// The beat is a looping click track timed through a <see cref="GameplayClock"/>, and a tap is placed with
/// <see cref="GameplayClock.GetTimeAt"/>, which is how a rhythm game times its music and judges its taps: so what this
/// measures is what the game's judgement sees. The flashes run on that clock with the track silent, so the two runs
/// differ in what reaches the player and nothing else.
/// </remarks>
public partial class TapCalibrationPad : Container
{
    /// <summary>
    /// 100 beats a minute: slow enough that a delay of up to 300 ms either way is still nearest its own beat.
    /// </summary>
    public const double DEFAULT_BEAT_INTERVAL = 600;

    private const int track_beats = 32;
    private const float pad_size = 220;
    private const double flash_fade_ms = 60;

    [Resolved]
    private IAudioManager audioManager { get; set; } = null!;

    private readonly Circle pad;
    private readonly Circle flash;
    private readonly SpriteText status;

    private ITrack? track;
    private IAudioChannel? channel;
    private GameplayClock? clock;
    private bool completed;

    /// <summary>
    /// How the beat is given.
    /// </summary>
    public CalibrationCue Cue { get; }

    /// <summary>
    /// Milliseconds between beats, to the nearest sample of the click track.
    /// </summary>
    public double BeatInterval { get; }

    /// <summary>
    /// Taps that have to count before the run is done.
    /// </summary>
    public int TapsNeeded { get; init; } = 20;

    /// <summary>
    /// What the taps so far measure.
    /// </summary>
    public TapCalibration Calibration { get; private set; }

    /// <summary>
    /// Raised once <see cref="TapsNeeded"/> taps have counted, with what they measure. The beat stops there.
    /// </summary>
    public event Action<TapCalibration>? Completed;

    /// <param name="cue">How the beat is given.</param>
    /// <param name="beatInterval">Milliseconds between beats; half of it is the most delay either way it can measure.</param>
    public TapCalibrationPad(CalibrationCue cue, double beatInterval = DEFAULT_BEAT_INTERVAL)
    {
        Cue = cue;
        BeatInterval = ClickTrack.IntervalFor(beatInterval);
        Calibration = new TapCalibration(BeatInterval);

        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);

        Children = new Drawable[]
        {
            pad = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(pad_size),
                Color = Color.FromArgb(255, 45, 50, 60)
            },
            flash = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(pad_size),
                Color = Color.White,
                Alpha = 0
            },
            status = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                Y = pad_size / 2 + 24,
                Font = FontUsage.Default.With(size: 20),
                Color = Color.White
            }
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        track = audioManager.CreateTrack(ClickTrack.Create(BeatInterval, track_beats));
        channel = track.GetChannel();
        channel.Looping = true;

        if (Cue == CalibrationCue.Flash)
            channel.Volume.Value = 0;

        clock = new GameplayClock(new TrackClock(channel));
        clock.Start();

        updateStatus();
    }

    /// <summary>
    /// Forgets the taps so far and starts the beat again, for another try.
    /// </summary>
    public void Restart()
    {
        Calibration = new TapCalibration(BeatInterval);
        completed = false;
        clock?.Start();
        updateStatus();
    }

    public override void Update()
    {
        base.Update();

        if (clock == null)
            return;

        clock.ProcessFrame();

        if (Cue == CalibrationCue.Flash)
        {
            // From the clock rather than a transform, so a flash shows exactly where the beat is.
            double sinceBeat = (clock.CurrentTime % BeatInterval + BeatInterval) % BeatInterval;
            flash.Alpha = completed ? 0 : (float)Math.Exp(-sinceBeat / flash_fade_ms);
        }
    }

    public override bool OnTouchDown(TouchEvent e)
    {
        tap(e.Timestamp);
        return true;
    }

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        tap(e.Timestamp);
        return true;
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        // Leaving isn't a tap: those stay with the game.
        if (e.IsRepeat || e.Key is Key.Escape or Key.SystemBack)
            return false;

        tap(e.Timestamp);
        return true;
    }

    private void tap(double timestamp)
    {
        if (clock == null || completed)
            return;

        Calibration.Add(double.IsNaN(timestamp) ? clock.CurrentTime : clock.GetTimeAt(timestamp));

        // A tap is shown where it lands, never the beat: in a run by sound, that would be a beat to see.
        pad.ClearTransforms();
        pad.Scale = new Vector2(0.94f);
        pad.ScaleTo(1, 150, Easing.OutQuad);

        if (Calibration.Counted >= TapsNeeded)
        {
            completed = true;
            clock.Stop();
            Completed?.Invoke(Calibration);
        }

        updateStatus();
    }

    private void updateStatus()
    {
        string instruction = Cue == CalibrationCue.Sound ? "Tap along to the clicks" : "Tap along to the flashes";

        if (completed && Calibration.Offset is double offset)
            status.Text = $"{describe(offset)} (± {Calibration.Spread ?? 0:0} ms)";
        else
            status.Text = $"{instruction}  {Math.Min(Calibration.Counted, TapsNeeded)} / {TapsNeeded}";
    }

    private static string describe(double offset) => Math.Abs(offset) < 0.5
        ? "On the beat"
        : $"{Math.Abs(offset):0} ms {(offset > 0 ? "late" : "early")}";

    protected override void Dispose(bool isDisposing)
    {
        channel?.Stop();
        channel?.Dispose();
        (track as IDisposable)?.Dispose();

        base.Dispose(isDisposing);
    }
}
