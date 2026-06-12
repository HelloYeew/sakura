// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A placeholder that defers creating and loading its content until it has been on screen
/// (inside its masking ancestors' bounds) for <see cref="TimeBeforeLoad"/> milliseconds.
/// The load then happens asynchronously, so it never blocks the update thread.
/// Use this for long scrolling lists — e.g. a song-select carousel where each panel has cover
/// art and text: panels that are never scrolled into view are never loaded, and fast scrolling
/// past a panel doesn't trigger a load at all (it must stay visible for the delay first).
/// <remark>
/// The wrapper itself must be given the size the content will occupy (it is the placeholder
/// that gets scrolled around before the content exists).
/// </remark>
/// </summary>
public partial class DelayedLoadWrapperContainer : Container
{
    /// <summary>
    /// Time in milliseconds this wrapper must be continuously on screen before the load begins.
    /// </summary>
    public double TimeBeforeLoad { get; }

    /// <summary>
    /// The loaded content, null until created.
    /// </summary>
    public Drawable? Content { get; private set; }

    /// <summary>
    /// Whether the content has finished loading and been added to the hierarchy.
    /// </summary>
    public bool DelayedLoadCompleted { get; private set; }

    /// <summary>
    /// Invoked on the update thread when the content has loaded and been added.
    /// </summary>
    public event Action<Drawable>? DelayedLoadComplete;

    private readonly Func<Drawable> createContent;

    private protected double TimeVisible;
    private protected bool LoadTriggered;

    /// <summary>
    /// <param name="createContent">Factory for the content; invoked when the load is triggered.</param>
    /// <param name="timeBeforeLoad">Continuous on-screen time in milliseconds required before loading.</param>
    /// </summary>
    public DelayedLoadWrapperContainer(Func<Drawable> createContent, double timeBeforeLoad = 500)
    {
        this.createContent = createContent ?? throw new ArgumentNullException(nameof(createContent));
        TimeBeforeLoad = timeBeforeLoad;

        // The wrapper must keep updating while masked away, otherwise it could never
        // observe itself coming back on screen (masked-away drawables skip Update).
        AlwaysPresent = true;
    }

    /// <param name="content">The content to wrap. Prefer the factory overload when using
    /// <see cref="DelayedLoadUnloadWrapperContainer"/> so content can be recreated after unloading.</param>
    /// <param name="timeBeforeLoad">Continuous on-screen time in milliseconds required before loading.</param>
    public DelayedLoadWrapperContainer(Drawable content, double timeBeforeLoad = 500)
        : this(() => content, timeBeforeLoad)
    {
    }

    /// <summary>
    /// Whether this wrapper currently counts as on screen.
    /// </summary>
    protected virtual bool IsOnScreen => !IsMaskedAway;

    public override void Update()
    {
        base.Update();

        if (!LoadTriggered)
        {
            // Require continuous visibility: scrolling quickly past resets the timer.
            if (IsOnScreen)
                TimeVisible += Clock.ElapsedFrameTime;
            else
                TimeVisible = 0;

            if (TimeVisible >= TimeBeforeLoad)
                beginLoad();
        }
    }

    private void beginLoad()
    {
        LoadTriggered = true;

        var content = createContent();
        Content = content;

        LoadComponentAsync(content, loaded =>
        {
            AddInternal(loaded);
            DelayedLoadCompleted = true;
            DelayedLoadComplete?.Invoke(loaded);
        });
    }

    /// <summary>
    /// Removes the loaded content and resets so a future on-screen period can load it again
    /// (used by <see cref="DelayedLoadUnloadWrapperContainer"/>).
    /// </summary>
    private protected void UnloadContent()
    {
        if (Content == null)
            return;

        if (Content.Parent == this)
            RemoveInternal(Content);

        Content = null;
        DelayedLoadCompleted = false;
        LoadTriggered = false;
        TimeVisible = 0;
    }
}
