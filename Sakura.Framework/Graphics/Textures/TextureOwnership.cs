// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// What disposing a <see cref="Texture"/> is allowed to do to the GPU resource behind it.
/// </summary>
/// <remarks>
/// A <see cref="Texture"/> and its <see cref="INativeTexture"/> are not one-to-one: several textures can
/// be views into one atlas page, and a couple of textures are framework-wide singletons handed to every
/// caller. Recording which case a texture is in lets every release path be a plain
/// <see cref="Texture.Dispose"/> call. Before this existed, callers reached past the texture to dispose
/// the native resource directly — which freed the GPU memory but left the <see cref="Texture"/>
/// registered and undisposed, so the texture viewer kept listing it (rendering as a flat white
/// rectangle, since both backends bind a white fallback for an unsampleable texture) and
/// <see cref="TextureRegistry.LiveBytes"/> never came back down.
/// </remarks>
public enum TextureOwnership
{
    /// <summary>
    /// The texture owns its GPU resource: disposing it destroys the resource. The normal case, and the
    /// default for a texture created over its own allocation.
    /// </summary>
    Owned,

    /// <summary>
    /// The GPU resource belongs to something else — an atlas page shared by every region sliced out of
    /// it. Disposing unregisters the texture but leaves the resource alone.
    /// </summary>
    Borrowed,

    /// <summary>
    /// A framework-wide singleton handed out to many callers, such as
    /// <see cref="Rendering.IRenderer.WhitePixel"/> or a texture manager's missing-texture fallback.
    /// Disposing does nothing at all: it lives for the process, and one caller releasing what it was
    /// handed must not take it away from everyone else.
    /// </summary>
    Shared,
}
