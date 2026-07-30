// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// A public-facing texture that drawables use.
/// Points to a specific region (UvRect) within a larger <see cref="INativeTexture"/>
/// (atlas or standalone).
/// </summary>
public class Texture : IDisposable
{
    /// <summary>
    /// The underlying GPU texture. Null for dimension-only proxy textures.
    /// </summary>
    public INativeTexture? BackendTexture { get; }

    /// <summary>
    /// UV region within the native texture (0–1 coordinates).
    /// </summary>
    public RectangleF UvRect { get; }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// What <see cref="Dispose"/> is allowed to do to <see cref="BackendTexture"/>.
    /// </summary>
    public TextureOwnership Ownership { get; }

    /// <summary>
    /// A human-readable label for debugging and tooling (shown by the texture viewer). Set it to
    /// whatever identifies the texture in the game or app's own terms like a source path, a cache key, or a
    /// description that you want
    /// </summary>
    /// <remarks>
    /// Worth setting on anything created from raw pixel data: without it the viewer can only show
    /// dimensions, and a screen full of identically-sized anonymous cards is not diagnosable.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// True once the GPU upload has completed and the texture is safe to render. False both before the
    /// upload runs and after the GPU resource has been destroyed.
    /// </summary>
    public bool IsAvailable => BackendTexture?.Available ?? false;

    private readonly TextureRegistry.Entry registryEntry;

    /// <summary>
    /// Creates a texture wrapping the entire area of a <see cref="INativeTexture"/>.
    /// </summary>
    /// <param name="backendTexture">The GPU texture to wrap.</param>
    /// <param name="ownership">
    /// Whether disposing this texture destroys <paramref name="backendTexture"/>. Defaults to
    /// <see cref="TextureOwnership.Owned"/>, which is right for a texture created over its own
    /// allocation.
    /// </param>
    public Texture(INativeTexture backendTexture, TextureOwnership ownership = TextureOwnership.Owned)
    {
        BackendTexture = backendTexture;
        UvRect = new RectangleF(0, 0, 1, 1);
        Width = backendTexture.Width;
        Height = backendTexture.Height;
        Ownership = ownership;

        registryEntry = TextureRegistry.Register(this);
    }

    /// <summary>
    /// Creates a texture wrapping a sub-region of a <see cref="INativeTexture"/>.
    /// Used by <see cref="TextureAtlas"/> to return atlas slices.
    /// </summary>
    /// <remarks>
    /// Always <see cref="TextureOwnership.Borrowed"/>: the page belongs to the atlas and outlives any
    /// individual region sliced out of it.
    /// </remarks>
    public Texture(INativeTexture backendTexture, RectangleF uvRect)
    {
        BackendTexture = backendTexture;
        UvRect = uvRect;
        Width = (int)(backendTexture.Width * uvRect.Width);
        Height = (int)(backendTexture.Height * uvRect.Height);
        Ownership = TextureOwnership.Borrowed;

        registryEntry = TextureRegistry.Register(this);
    }

    /// <summary>
    /// Creates a dimension-only proxy texture with no GPU backing.
    /// Used by the video pipeline so <see cref="Drawable"/>
    /// can compute FillMode layout without knowing the underlying GPU resource type.
    /// </summary>
    public Texture(int width, int height)
    {
        BackendTexture = null;
        UvRect = new RectangleF(0, 0, 1, 1);
        Width = width;
        Height = height;
        Ownership = TextureOwnership.Owned;

        registryEntry = TextureRegistry.Register(this);
    }

    /// <summary>
    /// Whether this texture has been disposed. A disposed texture is excluded from
    /// <see cref="TextureRegistry"/> enumeration even while a reference to it is still held.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Releases this texture: unregisters it from <see cref="TextureRegistry"/> and, when it owns its
    /// GPU resource, destroys that too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only correct way to release a texture. Disposing <see cref="BackendTexture"/> directly
    /// frees the GPU memory but leaves this object registered and undisposed, so every tool keeps
    /// reporting it as live and anything still holding it renders a flat white rectangle.
    /// </para>
    /// <para>
    /// Destroying a GPU resource must happen on the draw thread, so call this there — from a caller
    /// that is already on it, or via <see cref="Rendering.IRenderer.ScheduleToDrawThread"/>. Disposing a
    /// <see cref="TextureOwnership.Borrowed"/> or <see cref="TextureOwnership.Shared"/> texture touches
    /// no GPU state and is safe from anywhere.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        // A shared singleton is handed to many callers and belongs to the framework for the process
        // lifetime. Any of them disposing what they were given must be a no-op, not a release.
        if (Ownership == TextureOwnership.Shared)
            return;

        if (IsDisposed)
            return;

        IsDisposed = true;

        TextureRegistry.Unregister(registryEntry);

        if (Ownership == TextureOwnership.Owned)
            BackendTexture?.Dispose();
    }

    public override string ToString() => Name == null
        ? $"{Width}x{Height}"
        : $"{Name} ({Width}x{Height})";
}
