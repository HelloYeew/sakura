// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Platform;
using Sakura.Framework.Threading;

namespace Sakura.Framework.Tests.Threading;

/// <summary>
/// Covers how the host's level is worked out from the kind of app, the window and what the app says it's doing.
/// </summary>
[TestFixture]
public class ThreadActivityTrackerTest
{
    /// <summary>
    /// A game that never says what it's doing keeps the precise timing it had before the levels existed.
    /// </summary>
    [Test]
    public void AFocusedGameThatHasSaidNothingIsTight()
    {
        Assert.That(new ThreadActivityTracker(AppType.Game).Current, Is.EqualTo(ThreadActivity.Tight));
    }

    [TestCase(GameState.Gameplay, ThreadActivity.Tight)]
    [TestCase(GameState.UninterruptibleGameplay, ThreadActivity.Tight)]
    [TestCase(GameState.None, ThreadActivity.Smooth)]
    [TestCase(GameState.Content, ThreadActivity.Smooth)]
    public void AFocusedGameFollowsWhatItSays(GameState state, ThreadActivity expected)
    {
        var tracker = new ThreadActivityTracker(AppType.Game);

        tracker.SetGameState(state, false);

        Assert.That(tracker.Current, Is.EqualTo(expected));
    }

    [Test]
    public void AnApplicationIsNeverTight()
    {
        var tracker = new ThreadActivityTracker(AppType.Application);
        Assert.That(tracker.Current, Is.EqualTo(ThreadActivity.Smooth));

        tracker.SetGameState(GameState.Gameplay, false);
        Assert.That(tracker.Current, Is.EqualTo(ThreadActivity.Smooth));
    }

    [TestCase(AppType.Game)]
    [TestCase(AppType.Application)]
    public void AnUnfocusedWindowIsBackground(AppType appType)
    {
        var tracker = new ThreadActivityTracker(appType);

        tracker.SetFocused(false);

        Assert.That(tracker.Current, Is.EqualTo(ThreadActivity.Background));
    }

    [TestCase("minimized")]
    [TestCase("occluded")]
    [TestCase("in the background")]
    public void AWindowNobodyCanSeeIsHidden(string how)
    {
        var tracker = new ThreadActivityTracker(AppType.Game);
        tracker.SetGameState(GameState.Gameplay, false);

        switch (how)
        {
            case "minimized":
                tracker.SetMinimized(true);
                break;

            case "occluded":
                tracker.SetOccluded(true);
                break;

            default:
                tracker.SetInBackground(true);
                break;
        }

        Assert.That(tracker.Current, Is.EqualTo(ThreadActivity.Hidden));
    }

    /// <summary>
    /// Minimizing usually comes with losing the focus, and restoring may come before getting it back.
    /// </summary>
    [Test]
    public void EachStepOfMinimizingAndRestoringLandsWhereItShould()
    {
        var tracker = new ThreadActivityTracker(AppType.Game);

        tracker.SetFocused(false);
        tracker.SetMinimized(true);
        Assert.That(tracker.Current, Is.EqualTo(ThreadActivity.Hidden));

        tracker.SetMinimized(false);
        Assert.That(tracker.Current, Is.EqualTo(ThreadActivity.Background), "restored, not yet focused");

        tracker.SetFocused(true);
        Assert.That(tracker.Current, Is.EqualTo(ThreadActivity.Tight));
    }

    [Test]
    public void LoadingEasesOnlyTheUpdateThread()
    {
        var tracker = new ThreadActivityTracker(AppType.Game);

        tracker.SetGameState(GameState.Gameplay, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tracker.Current, Is.EqualTo(ThreadActivity.Tight));
            Assert.That(tracker.For(ThreadRole.Update), Is.EqualTo(ThreadActivity.Smooth));
            Assert.That(tracker.For(ThreadRole.Draw), Is.EqualTo(ThreadActivity.Tight));
            Assert.That(tracker.For(ThreadRole.Audio), Is.EqualTo(ThreadActivity.Tight));
        }

        tracker.SetGameState(GameState.Gameplay, false);
        Assert.That(tracker.For(ThreadRole.Update), Is.EqualTo(ThreadActivity.Tight));
    }

    [Test]
    public void LoadingLeavesALowerLevelAlone()
    {
        var tracker = new ThreadActivityTracker(AppType.Game);

        tracker.SetFocused(false);
        tracker.SetGameState(GameState.Gameplay, true);

        Assert.That(tracker.For(ThreadRole.Update), Is.EqualTo(ThreadActivity.Background));
    }

    [Test]
    public void EachChangeIsReportedOnceWithItsReason()
    {
        var tracker = new ThreadActivityTracker(AppType.Game);
        var changes = new List<(ThreadActivity, ThreadActivity, string)>();
        tracker.Changed += (previous, next, reason) => changes.Add((previous, next, reason));

        tracker.SetGameState(GameState.Gameplay, false);
        tracker.SetFocused(false);
        tracker.SetFocused(false);
        tracker.SetFocused(true);

        Assert.That(changes, Is.EqualTo(new[]
        {
            (ThreadActivity.Tight, ThreadActivity.Background, "unfocused"),
            (ThreadActivity.Background, ThreadActivity.Tight, "in play")
        }));
    }
}
