// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Testing;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Visuals.UserInterface;

/// <summary>
/// A run by sound and one by sight, as a game's calibration screen would make them, and what the two make together.
/// Tap along in the test browser to measure this machine.
/// </summary>
public partial class TestTapCalibrationPad : ManualInputManagerTestScene
{
    private TapCalibrationPad pad = null!;
    private TapCalibration? completedWith;
    private double? heard;

    [Test]
    public void TestTapsCountAfterTheWarmUp()
    {
        createPad(CalibrationCue.Sound, tapsNeeded: 1);

        for (int i = 0; i < 4; i++)
            AddStep($"Warm-up tap {i + 1}", () => tapKey(Key.Space));

        AddAssert("Nothing counted yet", () => pad.Calibration.Offset == null && completedWith == null);

        AddStep("Leave", () => tapKey(Key.Escape));
        AddAssert("Leaving isn't a tap", () => pad.Calibration.Offset == null);

        AddStep("Tap", () => tapKey(Key.Space));
        AddAssert("It counted, and that was enough", () => pad.Calibration.Counted == 1 && completedWith == pad.Calibration);

        AddStep("Tap after it's done", () => tapKey(Key.Space));
        AddAssert("Nothing more is taken", () => pad.Calibration.Counted == 1);

        AddStep("Try again", () => pad.Restart());
        AddAssert("From the start", () => pad.Calibration.Offset == null);
    }

    [Test]
    public void TestByEarThenByEye()
    {
        createPad(CalibrationCue.Sound);
        AddStep("Remember the offset when done", () => pad.Completed += c => heard = c.Offset);

        createPad(CalibrationCue.Flash);
        AddStep("Show both when done", () => pad.Completed += c =>
        {
            if (heard is double soundAndTouch && c.Offset is double pictureAndTouch)
                Logger.Verbose($"Heard {soundAndTouch:0} ms late, seen {pictureAndTouch:0} ms late: "
                               + $"the music is {TapCalibration.SoundAfterPicture(soundAndTouch, pictureAndTouch):0} ms behind the picture.");
        });
    }

    private void createPad(CalibrationCue cue, int tapsNeeded = 20)
    {
        AddStep($"Pad by {cue}", () =>
        {
            TestContent.Clear();
            completedWith = null;

            TestContent.Add(pad = new TapCalibrationPad(cue)
            {
                TapsNeeded = tapsNeeded,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre
            });

            pad.Completed += c => completedWith = c;
        });
    }

    private void tapKey(Key key)
    {
        InputManager.PressKey(key);
        InputManager.ReleaseKey(key);
    }
}
