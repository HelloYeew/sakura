// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// A game runs as fast as it can and lets its player choose the frame limiter; an application only runs smoothly;
/// and on a phone neither draws frames the panel can't show or polls faster than anything changes.
/// </summary>
[TestFixture]
public class FramePacingTest
{
    [TestCase(AppType.Game, false, FrameSync.Limit2x)]
    [TestCase(AppType.Application, false, FrameSync.VSync)]
    [TestCase(AppType.Game, true, FrameSync.VSync)]
    [TestCase(AppType.Application, true, FrameSync.VSync)]
    public void DefaultFrameLimiter(AppType appType, bool mobile, FrameSync expected)
        => Assert.That(new FramePacing(appType, mobile).DefaultFrameLimiter, Is.EqualTo(expected));

    [TestCase(144, 138.4615)]
    [TestCase(240, 225)]
    [TestCase(120, 116.1290)]
    [TestCase(60, 59.0164)]
    public void VariableRefreshDrawsJustUnderTheTopRate(double displayHz, double expected)
        => Assert.That(FramePacing.VariableRefreshHz(displayHz), Is.EqualTo(expected).Within(1e-4));

    [TestCase(60)]
    [TestCase(144)]
    [TestCase(360)]
    public void VariableRefreshKeepsTheSameMarginAtEveryRate(double displayHz)
        => Assert.That(1 / FramePacing.VariableRefreshHz(displayHz) - 1 / displayHz, Is.EqualTo(FramePacing.VARIABLE_REFRESH_MARGIN_SECONDS).Within(1e-12));

    [TestCase(VariableRefreshSupport.Available, FrameSync.VariableRefresh)]
    [TestCase(VariableRefreshSupport.Unknown, FrameSync.VariableRefresh)]
    [TestCase(VariableRefreshSupport.Unavailable, FrameSync.VSync)]
    [TestCase(VariableRefreshSupport.Stepped, FrameSync.VSync)]
    public void VariableRefreshFollowsVSyncWhereTheDisplayWontVaryOrVariesInSteps(VariableRefreshSupport support, FrameSync expected)
        => Assert.That(FramePacing.EffectiveFrameLimiter(FrameSync.VariableRefresh, support), Is.EqualTo(expected));

    [Test]
    public void OnlyVariableRefreshOnASteppedDisplayTurnsTheLateLatchOn([Values] FrameSync chosen, [Values] VariableRefreshSupport support)
        => Assert.That(FramePacing.LatchesLate(chosen, support), Is.EqualTo(chosen == FrameSync.VariableRefresh && support == VariableRefreshSupport.Stepped));

    [TestCase(1.0 / 240, ExpectedResult = true)]
    [TestCase(0.005, ExpectedResult = true)]
    [TestCase(0.0001, ExpectedResult = false)]
    [TestCase(0, ExpectedResult = false)]
    public bool ARefreshIsSteppedWhereTheLimitersPeriodFallsBetweenTwoIntervals(double stepSeconds) => FramePacing.RefreshStepped(stepSeconds);

    [Test]
    public void AHeadlessWindowsDisplayHasNoRefreshStep() => Assert.That(((IWindow)new HeadlessWindow()).DisplayRefreshStep, Is.Zero);

    private static readonly FrameSync[] other_limiters = Enum.GetValues<FrameSync>().Where(limiter => limiter != FrameSync.VariableRefresh).ToArray();

    [Test]
    public void OtherLimitersAreFollowedAsChosen([ValueSource(nameof(other_limiters))] FrameSync chosen, [Values] VariableRefreshSupport support)
        => Assert.That(FramePacing.EffectiveFrameLimiter(chosen, support), Is.EqualTo(chosen));

    [Test]
    public void AHeadlessWindowsDisplayNeverVaries() => Assert.That(((IWindow)new HeadlessWindow()).VariableRefresh, Is.EqualTo(VariableRefreshSupport.Unavailable));

    [TestCase(AppType.Game, 120, 240)]
    [TestCase(AppType.Game, 600, 1000)]
    [TestCase(AppType.Game, 0, 1000)]
    [TestCase(AppType.Application, 120, 120)]
    public void DesktopGameUpdatesTwicePerDraw(AppType appType, double drawHz, double expected)
        => Assert.That(new FramePacing(appType, false).UpdateHz(drawHz, limitUnlimited: true), Is.EqualTo(expected));

    [Test]
    public void DesktopUnlimitedCanStayUnlimited()
        => Assert.That(new FramePacing(AppType.Game, false).UpdateHz(0, limitUnlimited: false), Is.Zero);

    [TestCase(AppType.Game, 60, 60)]
    [TestCase(AppType.Game, 120, 120)]
    [TestCase(AppType.Game, 240, 240)]
    [TestCase(AppType.Game, 480, FramePacing.MOBILE_MAX_UPDATE_HZ)]
    [TestCase(AppType.Game, 0, FramePacing.MOBILE_MAX_UPDATE_HZ)]
    [TestCase(AppType.Application, 90, 90)]
    public void PhoneUpdatesFollowDraws(AppType appType, double drawHz, double expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(new FramePacing(appType, true).UpdateHz(drawHz, limitUnlimited: true), Is.EqualTo(expected));
            Assert.That(new FramePacing(appType, true).UpdateHz(drawHz, limitUnlimited: false), Is.EqualTo(expected), "a phone always stops somewhere");
        });
    }

    [TestCase(AppType.Game, false, true, FramePacing.DESKTOP_MAX_HZ)]
    [TestCase(AppType.Game, false, false, FramePacing.DESKTOP_MAX_HZ)]
    [TestCase(AppType.Application, false, true, 120)]
    [TestCase(AppType.Game, true, true, FramePacing.MOBILE_GAME_AUDIO_HZ)]
    [TestCase(AppType.Application, true, true, 120)]
    public void AudioRate(AppType appType, bool mobile, bool playbackWaitsForUpdate, double expected)
        => Assert.That(new FramePacing(appType, mobile).AudioHz(120, playbackWaitsForUpdate), Is.EqualTo(expected));

    [Test]
    public void PhoneGameAudioFollowsTheDisplayWhenPlaybackDoesNotWait()
        => Assert.That(new FramePacing(AppType.Game, true).AudioHz(120, playbackWaitsForUpdate: false), Is.EqualTo(120));

    /// <summary>
    /// A timed present is only ever asked for under the two limiters that pace frames by the display, and the paced one
    /// under VSync only when it's asked for.
    /// </summary>
    [TestCase(FrameSync.VariableRefresh, false, 1 / 120.0)]
    [TestCase(FrameSync.VariableRefresh, true, 1 / 120.0)]
    [TestCase(FrameSync.VSync, false, 0)]
    [TestCase(FrameSync.VSync, true, 0.5 / 120)]
    [TestCase(FrameSync.Limit2x, true, 0)]
    [TestCase(FrameSync.Unlimited, true, 0)]
    public void MinimumPresentInterval(FrameSync limiter, bool pacedPresent, double expected)
        => Assert.That(FramePacing.MinimumPresentInterval(limiter, 120, pacedPresent), Is.EqualTo(expected).Within(1e-12));

    [TestCase(FrameSync.VSync, true, false, true)]
    [TestCase(FrameSync.VSync, false, false, false, Description = "unfocused frames come by the 60 Hz timer")]
    [TestCase(FrameSync.VSync, true, true, false, Description = "a headless renderer never blocks")]
    [TestCase(FrameSync.Limit2x, true, false, false)]
    [TestCase(FrameSync.VariableRefresh, true, false, false, Description = "VRR presents unsynced, so only the timer paces it")]
    [TestCase(FrameSync.Unlimited, true, false, false)]
    public void OnlyVSyncLeavesTheDrawToTheDisplay(FrameSync limiter, bool focused, bool headless, bool expected)
        => Assert.That(FramePacing.DisplayPacesDraws(limiter, focused, headless), Is.EqualTo(expected));
}
