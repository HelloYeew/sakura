// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Graphics.Screens;

/// <summary>
/// A container that can be managed by a <see cref="ScreenStack"/>.
/// </summary>
public class Screen : Container
{
    /// <summary>
    /// The stack this screen belongs to.
    /// </summary>
    public ScreenStack? Stack { get; internal set; }

    private ScreenState state = ScreenState.Idle;

    /// <summary>
    /// The current state of the screen.
    /// </summary>
    public ScreenState State
    {
        get => state;
        private set
        {
            Logger.Verbose($"Screen {this} state changed from {state} to {value}");
            state = value;
        }
    }

    /// <summary>
    /// Checks if this is the currently active screen on its stack.
    /// </summary>
    public bool IsCurrentScreen => Stack?.CurrentScreen == this;

    public override void Load()
    {
        base.Load();

        // Screens should always be present to allow for transitions (e.g.fading out)
        // even when their Alpha is 0.
        AlwaysPresent = true;
    }

    #region Screen Lifecycle

    /// <summary>
    /// Called when this Screen is first pushed onto the stack and becomes active.
    /// </summary>
    /// <param name="last">The screen that was active before this one.</param>
    public virtual void OnEntering(Screen? last) { }

    /// <summary>
    /// Called when a new Screen is pushed on top of this one, causing this one to become suspended.
    /// </summary>
    /// <param name="next">The new screen that is becoming active.</param>
    public virtual void OnSuspending(Screen next) { }

    /// <summary>
    /// Called when the Screen above this one is popped, and this one becomes active again.
    /// </summary>
    /// <param name="last">The screen that was just popped.</param>
    public virtual void OnResuming(Screen? last) { }

    /// <summary>
    /// Called when this Screen is popped from the stack.
    /// This method is responsible for performing any exit transitions.
    /// The screen will be automatically removed from its parent after
    /// the longest-running transform defined here is complete.
    /// </summary>
    /// <param name="next">The screen that will become active after this one is removed.</param>
    public virtual void OnExiting(Screen? next) { }

    /// <summary>
    /// Exit this screen by popping it from its stack.
    /// </summary>
    public void Exit()
    {
        Stack?.Exit();
    }

    #endregion

    #region Internal State Management

    internal void InternalEnter(Screen? last)
    {
        if (State != ScreenState.Idle)
            throw new InvalidOperationException("Screen is already on a stack.");

        State = ScreenState.Active;
        OnEntering(last);
    }

    internal void InternalSuspend(Screen next)
    {
        if (State != ScreenState.Active)
            throw new InvalidOperationException("Screen is not active.");

        State = ScreenState.NotCurrent;
        OnSuspending(next);
    }

    internal void InternalResume(Screen? last)
    {
        if (State != ScreenState.NotCurrent)
            throw new InvalidOperationException("Screen is not suspended.");

        State = ScreenState.Active;
        OnResuming(last);
    }

    internal void InternalExit(Screen? next)
    {
        if (State == ScreenState.Idle)
            throw new InvalidOperationException("Screen is not on a stack.");

        State = ScreenState.Idle;

        OnExiting(next);

        // Check for any running transforms to determine when we can remove this screen.
        double exitDuration = GetLatestTransformEndTime() - Clock.CurrentTime;

        if (exitDuration <= 0)
        {
            // No transitions were defined, or they are instant.
            // Remove immediately.
            Parent?.Remove(this);
        }
        else
        {
            // Transitions are running. Schedule removal for when they finish.
            // Scheduler is defined in Drawable.cs
            Scheduler.AddDelayed(() => Parent?.Remove(this), exitDuration);
        }
    }

    #endregion

    #region Event Handling (State-aware)

    public override bool OnMouseDown(MouseButtonEvent e)
    {
        if (State != ScreenState.Active) return false;
        return base.OnMouseDown(e);
    }

    public override bool OnMouseUp(MouseButtonEvent e)
    {
        if (State != ScreenState.Active) return false;
        return base.OnMouseUp(e);
    }

    public override bool OnMouseMove(MouseEvent e)
    {
        if (State != ScreenState.Active) return false;
        return base.OnMouseMove(e);
    }

    public override bool OnScroll(ScrollEvent e)
    {
        if (State != ScreenState.Active) return false;
        return base.OnScroll(e);
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (State != ScreenState.Active) return false;
        return base.OnKeyDown(e);
    }

    public override bool OnKeyUp(KeyEvent e)
    {
        if (State != ScreenState.Active) return false;
        return base.OnKeyUp(e);
    }

    #endregion
}

/// <summary>
/// Represent the lifecycle state of a screen.
/// </summary>
public enum ScreenState
{
    /// <summary>
    /// The screen is not yet on the stack.
    /// </summary>
    Idle,

    /// <summary>
    /// The screen is on the stack, but not the current screen (suspended).
    /// </summary>
    NotCurrent,

    /// <summary>
    /// The screen is the current screen and current active.
    /// </summary>
    Active
}
