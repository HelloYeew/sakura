// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Threading;

/// <summary>
/// Works out the host's <see cref="ThreadActivity"/> from the kind of app, the window's state and what the app says it's
/// doing. The window's state comes from the main thread and the app's from any thread; the threads read the result at
/// the top of each frame.
/// </summary>
internal sealed class ThreadActivityTracker
{
    private readonly AppType appType;
    private readonly Lock gate = new Lock();

    private bool focused = true;
    private bool minimized;
    private bool occluded;
    private bool inBackground;

    /// <summary>
    /// What the app last said it's doing, or null until it first says.
    /// </summary>
    private GameState? gameState;

    private volatile ThreadActivity current;
    private volatile bool loading;

    /// <summary>
    /// Raised when <see cref="Current"/> changes, with the old level, the new one and why, on the thread that changed
    /// it. Never raised twice at once.
    /// </summary>
    public event Action<ThreadActivity, ThreadActivity, string>? Changed;

    /// <param name="appType">The kind of app: an application never asks for <see cref="ThreadActivity.Tight"/>.</param>
    public ThreadActivityTracker(AppType appType)
    {
        this.appType = appType;
        current = resolve(out _);
    }

    /// <summary>
    /// The level every thread should be at.
    /// </summary>
    public ThreadActivity Current => current;

    /// <summary>
    /// Whether the app says it's loading.
    /// </summary>
    public bool Loading => loading;

    /// <summary>
    /// The level one thread should be at. While the app loads, the update thread runs its long, uneven frames at
    /// <see cref="ThreadActivity.Smooth"/>: precise timing buys nothing for them.
    /// </summary>
    public ThreadActivity For(ThreadRole role)
    {
        var activity = current;

        return role == ThreadRole.Update && loading && activity == ThreadActivity.Tight ? ThreadActivity.Smooth : activity;
    }

    /// <summary>
    /// Sets whether the window has the keyboard focus.
    /// </summary>
    public void SetFocused(bool value) => change(() => focused = value);

    /// <summary>
    /// Sets whether the window is minimized.
    /// </summary>
    public void SetMinimized(bool value) => change(() => minimized = value);

    /// <summary>
    /// Sets whether other windows cover the window entirely.
    /// </summary>
    public void SetOccluded(bool value) => change(() => occluded = value);

    /// <summary>
    /// Sets whether a phone app is in the background.
    /// </summary>
    public void SetInBackground(bool value) => change(() => inBackground = value);

    /// <summary>
    /// Sets what the app says it's doing. Until it first says, a focused game is taken to be in play.
    /// </summary>
    public void SetGameState(GameState state, bool isLoading)
    {
        change(() =>
        {
            gameState = state;
            loading = isLoading;
        });
    }

    private void change(Action set)
    {
        lock (gate)
        {
            set();

            var previous = current;
            var next = resolve(out string reason);

            if (next == previous)
                return;

            current = next;
            Changed?.Invoke(previous, next, reason);
        }
    }

    private ThreadActivity resolve(out string reason)
    {
        if (inBackground)
        {
            reason = "in the background";
            return ThreadActivity.Hidden;
        }

        if (minimized)
        {
            reason = "minimized";
            return ThreadActivity.Hidden;
        }

        if (occluded)
        {
            reason = "covered by other windows";
            return ThreadActivity.Hidden;
        }

        if (!focused)
        {
            reason = "unfocused";
            return ThreadActivity.Background;
        }

        if (appType == AppType.Application)
        {
            reason = "an application";
            return ThreadActivity.Smooth;
        }

        switch (gameState)
        {
            case null:
                reason = "a focused game";
                return ThreadActivity.Tight;

            case GameState.Gameplay:
            case GameState.UninterruptibleGameplay:
                reason = "in play";
                return ThreadActivity.Tight;

            default:
                reason = gameState == GameState.Content ? "showing content" : "not in play";
                return ThreadActivity.Smooth;
        }
    }
}
