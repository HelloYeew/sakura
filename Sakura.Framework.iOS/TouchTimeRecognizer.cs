// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using CoreFoundation;
using Foundation;
using UIKit;

namespace Sakura.Framework.iOS;

/// <summary>
/// Gives SDL each touch at the time it happened, as the touch's event says. It watches the window's touches and
/// recognizes nothing.
/// </summary>
/// <remarks>
/// <para>
/// SDL stamps a touch from its event's timestamp, but it learns the offset between that clock and its own from the
/// first event it sees, taking it as handed over the moment it happened. It only ever lowers the offset after that, so
/// every touch is stamped late by the quickest hand-over seen so far, which shrinks in steps through a session.
/// </para>
/// <para>
/// A window gives its gesture recognizers each touch before the view it's on, on the same thread, so the time set here
/// is in place when SDL's view hands the touch to SDL. The touch-time watch then moves SDL's touch events onto it.
/// It's cleared once the event has been handled.
/// </para>
/// </remarks>
internal sealed class TouchTimeRecognizer : UIGestureRecognizer
{
    private readonly Action<long> setTouchTime;
    private bool clearQueued;

    /// <param name="setTouchTime">Hands the touch-time watch when the touch happened, on the <see cref="Stopwatch"/> timeline, or 0.</param>
    public TouchTimeRecognizer(Action<long> setTouchTime)
    {
        this.setTouchTime = setTouchTime;

        // It only watches: the touches reach the views as they would without it, and every other recognizer works too.
        CancelsTouchesInView = false;
        DelaysTouchesBegan = false;
        DelaysTouchesEnded = false;
        ShouldRecognizeSimultaneously = (_, _) => true;
    }

    public override void TouchesBegan(NSSet touches, UIEvent evt) => stamp(evt);

    public override void TouchesMoved(NSSet touches, UIEvent evt) => stamp(evt);

    public override void TouchesEnded(NSSet touches, UIEvent evt)
    {
        stamp(evt);
        failOnceAllHaveEnded(evt);
    }

    public override void TouchesCancelled(NSSet touches, UIEvent evt)
    {
        stamp(evt);
        failOnceAllHaveEnded(evt);
    }

    /// <summary>
    /// Sets the time the event happened, until it's been handled.
    /// </summary>
    private void stamp(UIEvent evt)
    {
        // How long ago it happened, on the system's uptime clock that the event's timestamp counts on, and back from the
        // Stopwatch read alongside it.
        long now = Stopwatch.GetTimestamp();
        double ago = NSProcessInfo.ProcessInfo.SystemUptime - evt.Timestamp;

        setTouchTime(now - (long)(Math.Max(0, ago) * Stopwatch.Frequency));

        if (clearQueued)
            return;

        // After this event has reached SDL's view, which comes next in the same pass.
        clearQueued = true;

        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            clearQueued = false;
            setTouchTime(0);
        });
    }

    /// <summary>
    /// Fails once the last finger has lifted, which makes the window start this afresh for the next touches. Before then
    /// it stays possible, which keeps the touches coming to it.
    /// </summary>
    private void failOnceAllHaveEnded(UIEvent evt)
    {
        if (evt.AllTouches is NSSet all)
        {
            foreach (var touch in all)
            {
                if (touch is UITouch { Phase: not (UITouchPhase.Ended or UITouchPhase.Cancelled) })
                    return;
            }
        }

        State = UIGestureRecognizerState.Failed;
    }
}
