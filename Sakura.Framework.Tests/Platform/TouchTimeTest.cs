// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Sakura.Framework.Platform;
using SDL;
using static SDL.SDL3;

namespace Sakura.Framework.Tests.Platform;

/// <summary>
/// A touch the platform knows the time of is queued with that time rather than with when SDL got it. This rests on SDL
/// queueing the very event its watchers were handed, which an SDL update could change.
/// </summary>
[TestFixture]
public class TouchTimeTest
{
    private const double delay_ms = 5;

    private const ulong nanoseconds_per_millisecond = 1_000_000;

    [SetUp]
    public void SetUp()
    {
        Assert.That((bool)SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_EVENTS), Is.True, SDL_GetError());
        SDLWindow.AddTouchTimeWatch();
        drain();

        // SDL's clock starts at zero when it's initialised, and a touch can't be dated to before that.
        while (SDL_GetTicksNS() < 50 * nanoseconds_per_millisecond)
            Thread.Sleep(5);
    }

    [TearDown]
    public void TearDown()
    {
        Retimer.Set(0);
        drain();
        SDLWindow.RemoveTouchTimeWatch();
        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_EVENTS);
    }

    [Test]
    public void ATouchTakesTheTimeItHappened()
    {
        ulong before = SDL_GetTicksNS();
        Retimer.Set(Stopwatch.GetTimestamp() - (long)(delay_ms / 1000 * Stopwatch.Frequency));
        push(SDL_EventType.SDL_EVENT_FINGER_DOWN);
        ulong after = SDL_GetTicksNS();

        ulong stamped = take(SDL_EventType.SDL_EVENT_FINGER_DOWN);
        const ulong delay = (ulong)(delay_ms * nanoseconds_per_millisecond);

        // SDL stamped it somewhere in [before, after], and the watch took off at least the delay and at most the delay
        // plus however long that took (and a millisecond for the two clocks' rates).
        Assert.That(stamped, Is.InRange(before - delay - (after - before) - nanoseconds_per_millisecond, after - delay));
    }

    [Test]
    public void ATouchWithNoKnownTimeKeepsSdls()
    {
        ulong before = SDL_GetTicksNS();
        push(SDL_EventType.SDL_EVENT_FINGER_MOTION);
        ulong after = SDL_GetTicksNS();

        Assert.That(take(SDL_EventType.SDL_EVENT_FINGER_MOTION), Is.InRange(before, after));
    }

    [Test]
    public void OnlyTouchesAreRetimed()
    {
        ulong before = SDL_GetTicksNS();
        Retimer.Set(Stopwatch.GetTimestamp() - (long)(delay_ms / 1000 * Stopwatch.Frequency));
        push(SDL_EventType.SDL_EVENT_KEY_DOWN);
        ulong after = SDL_GetTicksNS();

        Assert.That(take(SDL_EventType.SDL_EVENT_KEY_DOWN), Is.InRange(before, after));
    }

    private static unsafe void push(SDL_EventType type)
    {
        var e = new SDL_Event();
        e.type = (uint)type;
        Assert.That((bool)SDL_PushEvent(&e), Is.True, SDL_GetError());
    }

    private static unsafe ulong take(SDL_EventType type)
    {
        SDL_Event e;
        Assert.That(SDL_PeepEvents(&e, 1, SDL_EventAction.SDL_GETEVENT, (uint)type, (uint)type), Is.EqualTo(1), "the event wasn't queued");
        return e.common.timestamp;
    }

    private static unsafe void drain()
    {
        SDL_Event e;
        while (SDL_PeepEvents(&e, 1, SDL_EventAction.SDL_GETEVENT, (uint)SDL_EventType.SDL_EVENT_FIRST, (uint)SDL_EventType.SDL_EVENT_LAST) > 0)
        {
        }
    }

    private abstract class Retimer : SDLWindow
    {
        public static void Set(long stopwatchTimestamp) => SetTouchTime(stopwatchTimestamp);
    }
}
