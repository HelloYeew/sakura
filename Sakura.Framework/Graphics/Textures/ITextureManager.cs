// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Defines the public contract for a texture management service.
/// </summary>
public interface ITextureManager : IDisposable
{
    /// <summary>
    /// A 1x1 white pixel texture.
    /// </summary>
    Texture WhitePixel { get; }

    /// <summary>
    /// Retrieves a texture from the specified path.
    /// Loads it from storage if not already cached.
    /// </summary>
    /// <param name="path">The path to the texture in storage.</param>
    /// <returns>A <see cref="Texture"/> object. Returns <see cref="WhitePixel"/> if the path is null or empty, or a fallback texture on load failure.</returns>
    Texture Get(string path);

    /// <summary>
    /// Retrieves a texture from the specified path, decoded at (at most) the size in
    /// <paramref name="decode"/>. Loads it from storage if not already cached.
    /// </summary>
    /// <remarks>
    /// Prefer this over <see cref="Get(string)"/> for anything whose display size is known and smaller
    /// than the source.
    /// </remarks>
    /// <param name="path">The path to the texture in storage.</param>
    /// <param name="decode">How far to reduce the image while decoding it.</param>
    /// <returns>A <see cref="Texture"/> object. Returns <see cref="WhitePixel"/> if the path is null or empty, or a fallback texture on load failure.</returns>
    Texture Get(string path, ImageLoadOptions decode);

    /// <summary>
    /// Creates a texture from raw pixel data.
    /// </summary>
    /// <param name="width">Width of the texture in pixels.</param>
    /// <param name="height">Height of the texture in pixels.</param>
    /// <param name="pixelData">Raw pixel data in RGBA format.</param>
    /// <returns>A new <see cref="Texture"/> object.</returns>
    Texture FromPixelData(int width, int height, ReadOnlySpan<byte> pixelData, string cacheKey = null);

    /// <summary>
    /// Decodes an encoded image (PNG, JPEG, …) from a stream and returns a GPU texture for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one-call path from "I have a file" to "I have a texture": it decodes through the
    /// framework's <see cref="IImageLoader"/> at the requested size, uploads through the per-frame budgeted
    /// queue, names the result for the texture viewer, and optionally reference-counts it under a share
    /// key. Prefer it over decoding yourself and calling <see cref="FromPixelData"/> — that route means
    /// owning an imaging dependency, a downscale, an unpooled pixel buffer, and the texture's lifetime.
    /// </para>
    /// <para>
    /// The texture is always standalone (never packed into <see cref="Atlas"/>), since a texture created
    /// this way is expected to be large and individually releasable.
    /// </para>
    /// <para>
    /// The stream is read but not disposed; that stays with the caller. When
    /// <see cref="TextureCreationOptions.ShareKey"/> hits an existing entry the stream is not read at all.
    /// </para>
    /// </remarks>
    /// <param name="stream">The encoded image. Read from its current position; not disposed.</param>
    /// <param name="options">Decode size, debug name and optional share key.</param>
    /// <returns>
    /// The texture, or null if the image could not be decoded. A shared texture must be released via
    /// <see cref="ReleaseSharedTexture"/> rather than disposed.
    /// </returns>
    Texture? CreateFromStream(Stream stream, TextureCreationOptions options);

    /// <summary>
    /// Removes a texture from the cache and immediately disposes its GPU resources.
    /// </summary>
    /// <param name="path">The path of the texture to evict from cache.</param>
    /// <returns>True if the texture was found and evicted; false if the texture was not in cache.</returns>
    bool Evict(string path);

    /// <summary>
    /// If a reference-counted texture is already held for <paramref name="cacheKey"/>, increments its
    /// reference count and returns it (no decode/upload). Returns false otherwise, in which case the
    /// caller should decode and call <see cref="AcquireSharedTexture"/>. Balance with
    /// <see cref="ReleaseSharedTexture"/>. See <see cref="SharedTextureStore"/>.
    /// </summary>
    bool TryAcquireSharedTexture(string cacheKey, out Texture texture);

    /// <summary>
    /// Returns a reference-counted texture shared under <paramref name="cacheKey"/>, uploading
    /// <paramref name="pixelData"/> on first use or reusing (and ref-counting) the existing one. Use for
    /// images shown in multiple places or reloaded repeatedly (e.g. cover art). Every acquire must be
    /// balanced by a <see cref="ReleaseSharedTexture"/>; do not <see cref="Texture.Dispose"/> the result
    /// directly, as it may be shared.
    /// </summary>
    Texture AcquireSharedTexture(string cacheKey, int width, int height, ReadOnlySpan<byte> pixelData);

    /// <summary>
    /// Releases one reference previously taken via <see cref="TryAcquireSharedTexture"/> or
    /// <see cref="AcquireSharedTexture"/>. The GPU texture is disposed once the last reference is released.
    /// </summary>
    void ReleaseSharedTexture(string cacheKey);

    /// <summary>
    /// Every live standalone texture in the process, whether it was cached under a key.
    /// Exclude atlas that report separately via <see cref="Atlas"/>. For the narrower "what is cached under a key"
    /// question, use <see cref="GetCachedTextures"/>.
    /// </summary>
    IEnumerable<Texture> GetAllTextures();

    /// <summary>
    /// Only the textures held in this manager's key-based cache (i.e. loaded via <see cref="Get"/> or
    /// given a cache key). A subset of <see cref="GetAllTextures"/>.
    /// </summary>
    IEnumerable<Texture> GetCachedTextures();

    /// <summary>
    /// Registers a video texture so it appears in the texture viewer and can be tracked.
    /// Called by <see cref="Sakura.Framework.Graphics.Video.VideoDecoder"/> when creating a new texture.
    /// </summary>
    void RegisterVideoTexture(IVideoTexture texture);

    /// <summary>
    /// Unregisters a video texture when it is disposed.
    /// </summary>
    void UnregisterVideoTexture(IVideoTexture texture);

    /// <summary>
    /// Returns all currently active video textures.
    /// </summary>
    IEnumerable<IVideoTexture> GetAllVideoTextures();

    /// <summary>
    /// The dynamic atlas that small regular textures are packed into, or <c>null</c> if this
    /// manager does not perform atlas packing (e.g. the headless manager).
    /// </summary>
    TextureAtlas? Atlas { get; }
}
