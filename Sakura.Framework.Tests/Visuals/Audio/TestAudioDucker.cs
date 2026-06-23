// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Audio;

[TestFixture]
[VisualTestOnly("Requires the BASS audio backend or a real audio device")]
public partial class TestAudioDucker : TestScene
{
    [Resolved]
    private IAudioStore<ITrack> trackStore { get; set; } = null!;

    [Resolved]
    private IAudioStore<ISample> sampleStore { get; set; } = null!;

    private IAudioChannel source = null!;
    private IAudioChannel target = null!;
    private AudioDucker ducker = null!;

    private readonly Reactive<double> targetVolume = new Reactive<double>(1.0);

    private Box meter = null!;

    public override void Load()
    {
        base.Load();

        Add(new SpriteText
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Text = "Target amplitude (shrinks while source is loud)",
            Color = Color.White
        });

        meter = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.CentreLeft,
            Color = Color.Lime,
            Size = new Vector2(0, 40)
        };
        Add(meter);
    }

    private void createDucker(float threshold = 0.1f, double duckMultiplier = 0.3, float recoverySpeed = 0.05f)
    {
        AddStep("Set up source + target channels", () =>
        {
            source = sampleStore.Get("long.mp3").GetChannel();
            target = trackStore.Get("loud.mp3").GetChannel();
            target.Looping = true;
            target.Volume.Value = targetVolume.Value;
            target.Play();
        });

        AddStep("Create and add ducker", () =>
        {
            ducker = new AudioDucker(source, target, targetVolume)
            {
                Threshold = threshold,
                DuckMultiplier = duckMultiplier,
                RecoverySpeed = recoverySpeed
            };
            Add(ducker);
        });

        AddAssert("Ducker added to scene", () => Children.Contains(ducker));
    }

    [Test]
    public void TestDuckOnLoudSource()
    {
        createDucker();

        // Before the source plays, nothing should be ducked.
        AddWaitStep("Settle", 200);
        AddAssert("Duck factor starts at 1.0", () => ducker.CurrentDuckFactor >= 0.99);

        AddStep("Play loud source", () => source.Play());

        // While the loud source plays above threshold, the ducker drops its factor toward
        // DuckMultiplier. Asserting on the duck factor (not post-clip amplitude) is robust even
        // when the target track is brick-walled/clipping, where the peak meter stays pinned at 1.0.
        AddUntilStep("Duck factor drops toward DuckMultiplier",
            () => source.IsRunning.Value && ducker.CurrentDuckFactor <= ducker.DuckMultiplier + 0.05);
    }

    [Test]
    public void TestRecoveryAfterSourceStops()
    {
        createDucker(recoverySpeed: 0.2f);

        AddStep("Play loud source", () => source.Play());
        AddUntilStep("Ducking engaged", () => ducker.CurrentDuckFactor <= ducker.DuckMultiplier + 0.05);
        AddStep("Stop source", () => source.Stop());
        // Once the source is quiet, the duck factor eases back toward 1.0.
        AddUntilStep("Duck factor recovers toward 1.0", () => ducker.CurrentDuckFactor > 0.9);
    }

    [Test]
    public void TestBelowThresholdNoDuck()
    {
        // A threshold above the maximum possible peak (1.0) means the source can never count
        // as "loud", so no ducking should ever occur — even for a very loud/clipping source.
        createDucker(threshold: 1.5f);

        AddStep("Play source", () => source.Play());
        AddWaitStep("Observe", 600);
        AddAssert("Source is running", () => source.IsRunning.Value);
        AddAssert("Duck factor stays at 1.0 (no ducking)", () => ducker.CurrentDuckFactor >= 0.99);
    }

    [TestCase(0.3, "moderate duck")]
    [TestCase(0.1, "heavy duck")]
    [TestCase(0.6, "light duck")]
    public void TestDuckMultiplierSweep(double multiplier, string description)
    {
        createDucker(duckMultiplier: multiplier);

        AddStep($"Play source ({description})", () => source.Play());
        AddWaitStep("Listen to ducked target", 800);
        AddAssert("Ducker uses configured multiplier", () => ducker.DuckMultiplier == multiplier);
    }

    [Test]
    public void TestInteractive()
    {
        createDucker();

        AddSliderStep("Threshold", 0.0f, 1.0f, 0.1f, v => ducker.Threshold = v);
        AddSliderStep("Recovery speed", 0.01f, 0.5f, 0.05f, v => ducker.RecoverySpeed = v);
        AddStep("Play source", () => source.Play());
    }

    public override void Update()
    {
        base.Update();

        if (target != null)
            meter.Width = target.AmplitudeLeft * 300f;
    }

    [TearDown]
    public void TearDown()
    {
        AddStep("Clean up", () =>
        {
            if (ducker != null)
                Remove(ducker);

            source?.Stop();
            source?.Dispose();
            target?.Stop();
            target?.Dispose();
            source = null!;
            target = null!;
            ducker = null!;
        });
    }
}
