// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// Draws the frame a <see cref="VideoSprite"/> is showing a second time, somewhere else: under a blur,
/// as a thumbnail, as a reflection. It shares the sprite's decoded frames, so the second picture costs a
/// draw, not a second decode.
/// </summary>
/// <remarks>
/// <para>
/// It follows the sprite's frame and nothing else. Its position, size, colour and
/// <see cref="Drawable.FillMode"/> are its own, and it has no playback controls: pause or seek the
/// sprite. It draws nothing until the sprite has a frame, and nothing once the sprite is disposed.
/// </para>
/// <para>
/// It only asks to be redrawn when the sprite's frame changes, so under a
/// <see cref="Containers.BufferedContainer"/> that caches its buffer the buffer re-renders at the video's
/// frame rate rather than the display's.
/// </para>
/// </remarks>
public partial class VideoMirror : Drawable
{
    /// <summary>
    /// How many of its source's updates a draw node of this mirror can still be on its way to the screen
    /// for, after the mirror was last drawn: one per buffered draw node.
    /// </summary>
    private const int frames_in_flight = 3;

    private VideoSprite? source;
    private IRenderer? renderer;

    // Written on the draw thread once compiled, read on the update thread, as in VideoSprite.
    private VideoShaderSet? videoShaders;

    // What the draw nodes are handed: read from the source each update.
    private IVideoTexture? shownTexture;
    private float[]? shownMatrix;
    private VideoShaderSet? shownShaders;

    /// <summary>
    /// The texture handed to each of the three buffered draw nodes, indexed the same way. The source holds
    /// on to a frame while any of them can still draw it.
    /// </summary>
    private readonly IVideoTexture?[] stampedTextures = new IVideoTexture?[frames_in_flight];

    /// <summary>
    /// The source's <see cref="VideoSprite.Updates"/> when this mirror last updated where it can be seen.
    /// </summary>
    private long drawnAt;

    /// <summary>
    /// The sprite whose frames this draws, or null to draw nothing.
    /// </summary>
    public VideoSprite? Source
    {
        get => source;
        set
        {
            if (source == value)
                return;

            source?.RemoveMirror(this);
            source = value;
            source?.AddMirror(this);
        }
    }

    [BackgroundDependencyLoader]
    private void load(AppHost host)
    {
        var drawRenderer = renderer = host.Renderer;

        // Its own pair rather than the source's: those go with the source, which a mirror can outlive.
        // Compiled on the draw thread, the GL context owner in multithreaded mode.
        drawRenderer.ScheduleToDrawThread(() => videoShaders = VideoShaderSet.Create(drawRenderer));
    }

    protected override DrawNode CreateDrawNode() => new VideoDrawNode();

    public override void Update()
    {
        base.Update();

        var live = source is { IsDisposed: false } ? source : null;

        if (live != null && !IsEffectivelyHidden && !IsMaskedAway)
            drawnAt = live.Updates;

        // The sprite's dimension-only texture, so FillMode lays the frame out as it would the sprite's.
        var dimensions = live?.Texture;

        if (Texture != dimensions)
            Texture = dimensions;

        var texture = live?.DisplayedTexture;
        var matrix = live?.DisplayedMatrix;

        if (ReferenceEquals(texture, shownTexture) && ReferenceEquals(matrix, shownMatrix) && shownShaders == videoShaders)
            return;

        shownTexture = texture;
        shownMatrix = matrix;
        shownShaders = videoShaders;

        MarkDrawStateRecomputed();
    }

    public override DrawNode GenerateDrawNodeSubtree(int frameIndex)
    {
        var node = base.GenerateDrawNodeSubtree(frameIndex) as VideoDrawNode;
        node?.ApplyVideoState(shownTexture, shownMatrix, shownTexture != null ? shownShaders?.For(shownTexture.Layout) : null);

        if (frameIndex >= 0 && frameIndex < stampedTextures.Length)
            stampedTextures[frameIndex] = shownTexture;

        return node!;
    }

    /// <summary>
    /// Whether this mirror can still draw <paramref name="texture"/>, so its source must not recycle it yet.
    /// </summary>
    /// <remarks>
    /// Only for as long as its draw nodes can still reach the screen. Hidden, masked away or no longer
    /// updated, a mirror isn't drawn, and the frames its nodes hold would only be kept from the video,
    /// whose pool is a handful of frames deep. Shown again, its nodes are rebuilt from the frame it
    /// shows then.
    /// </remarks>
    internal bool Holds(IVideoTexture texture, long sourceUpdates) =>
        sourceUpdates - drawnAt <= frames_in_flight && Holds(texture);

    /// <summary>
    /// Whether this mirror's draw nodes hold <paramref name="texture"/>, or it is about to hand it to them.
    /// </summary>
    internal bool Holds(IVideoTexture texture)
    {
        if (ReferenceEquals(texture, shownTexture))
            return true;

        foreach (var stamped in stampedTextures)
        {
            if (ReferenceEquals(texture, stamped))
                return true;
        }

        return false;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (IsDisposed)
            return;

        Source = null;
        shownTexture = null;
        Array.Clear(stampedTextures);

        // Queued behind the compile in load, so it always finds the shaders that compile made, even when
        // this is disposed before the draw thread got to it.
        renderer?.ScheduleToDrawThread(() =>
        {
            videoShaders?.Dispose();
            videoShaders = null;
        });

        base.Dispose(isDisposing);
    }
}
