// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A <see cref="DelayedLoadWrapperContainer"/> that additionally <b>unloads</b> its content after it has
/// been off screen for <see cref="TimeBeforeUnload"/> milliseconds, and recreates it via the
/// content factory when it comes back into view. Use for very large scrolling lists where
/// keeping every previously seen item alive would
/// hold too much memory (e.g. thousands of song panels with cover-art textures).
/// </summary>
public partial class DelayedLoadUnloadWrapperContainer : DelayedLoadWrapperContainer
{
    /// <summary>
    /// Time in milliseconds this wrapper must be continuously off screen before the
    /// loaded content is removed.
    /// </summary>
    public double TimeBeforeUnload { get; }

    /// <summary>
    /// Invoked on the update thread after the content has been unloaded.
    /// </summary>
    public event Action? ContentUnloaded;

    private double timeHidden;

    /// <summary>
    /// <param name="createContent">Factory for the content; invoked on every (re)load.</param>
    /// <param name="timeBeforeLoad">Continuous on-screen time in milliseconds required before loading.</param>
    /// <param name="timeBeforeUnload">Continuous off-screen time in milliseconds before unloading.</param>
    /// </summary>
    public DelayedLoadUnloadWrapperContainer(Func<Drawable> createContent, double timeBeforeLoad = 500, double timeBeforeUnload = 1000)
        : base(createContent, timeBeforeLoad)
    {
        TimeBeforeUnload = timeBeforeUnload;
    }

    public override void Update()
    {
        base.Update();

        if (DelayedLoadCompleted)
        {
            if (!IsOnScreen)
            {
                timeHidden += Clock.ElapsedFrameTime;

                if (timeHidden >= TimeBeforeUnload)
                {
                    UnloadContent();
                    timeHidden = 0;
                    ContentUnloaded?.Invoke();
                }
            }
            else
                timeHidden = 0;
        }
    }
}
