// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// The parts of texture creation that are identical across every backend: getting pixels onto the GPU
/// without extra copies, and turning an encoded stream into a texture.
/// </summary>
/// <remarks>
/// The Metal, OpenGL and Direct3D 11 managers differ only in how they allocate a native texture — which
/// <see cref="IRenderer.CreateNativeTexture"/> already abstracts. Everything else was triplicated, and had
/// already drifted between the three. Shared here instead.
/// </remarks>
internal static class TextureUploads
{
    /// <summary>
    /// Schedules an upload of pixel data the caller still owns, copying it into a pooled buffer that
    /// survives until the upload actually runs.
    /// </summary>
    /// <remarks>
    /// A copy is unavoidable here: uploads are queued to the draw thread and budgeted per frame, so the
    /// caller's span is long gone by the time the upload happens. What is avoidable is <em>allocating</em>
    /// that copy — a full-screen image is a large-object-heap block per call, and this used to be a
    /// <c>ToArray()</c>. Renting reuses the same few blocks and returns them the moment the upload
    /// completes. Prefer <see cref="ScheduleOwned"/> where the pixels are already owned, which copies
    /// nothing at all.
    /// </remarks>
    public static void Schedule(IRenderer renderer, INativeTexture nativeTexture, int width, int height, ReadOnlySpan<byte> pixelData)
        => ScheduleOwned(renderer, nativeTexture, ImageRawData.CopyFrom(width, height, pixelData));

    /// <summary>
    /// Schedules an upload straight out of decoded image data, taking over its lifetime: the data is
    /// disposed once the upload has run.
    /// </summary>
    /// <remarks>
    /// The zero-copy path. The decoder already produced an owned (pooled) buffer, so there is no reason
    /// to copy it again just to keep it alive — the upload holds it instead, and hands it back to the pool
    /// afterwards.
    /// </remarks>
    public static void ScheduleOwned(IRenderer renderer, INativeTexture nativeTexture, ImageRawData raw)
    {
        long bytes = (long)raw.Width * raw.Height * 4;

        renderer.ScheduleTextureUpload(() =>
        {
            try
            {
                nativeTexture.Upload(raw.Data);
            }
            finally
            {
                raw.Dispose();
            }
        }, bytes);
    }

    /// <summary>
    /// The backend-independent implementation of <see cref="ITextureManager.CreateFromStream"/>.
    /// </summary>
    /// <param name="stream">The encoded image. Read but not disposed.</param>
    /// <param name="options">Decode size, debug name and optional share key.</param>
    /// <param name="renderer">Used to allocate the native texture and queue the upload.</param>
    /// <param name="imageLoader">Performs the decode and reduction.</param>
    /// <param name="sharedTextures">The manager's reference-counted store, consulted when a share key is set.</param>
    /// <param name="release">
    /// The manager's release path, used only to discard a texture that lost a race to an identical one.
    /// </param>
    public static Texture? FromStream(
        Stream stream,
        TextureCreationOptions options,
        IRenderer renderer,
        IImageLoader imageLoader,
        SharedTextureStore sharedTextures,
        Action<Texture> release)
    {
        string? shareKey = options.ShareKey;

        // A hit here is the point of sharing: the stream is never read, nothing is decoded, and no second
        // copy of the image reaches the GPU.
        if (!string.IsNullOrEmpty(shareKey) && sharedTextures.TryAcquire(shareKey, out var shared))
        {
            SharedTextureStatistics.RecordHit();
            return shared;
        }

        Texture texture;

        // Not a `using`: on the success path ownership of the pixel buffer passes to the queued upload,
        // which releases it after it runs. Disposing here would hand the buffer back to the pool while
        // the upload still needs to read it.
        ImageRawData raw = default;

        try
        {
            // Deliberately outside the shared store's lock: decoding is slow, and holding a global lock
            // across it would serialise every texture load in the process.
            raw = imageLoader.Load(stream, options.Decode);

            if (!raw.IsValid || raw.Width <= 0 || raw.Height <= 0)
            {
                raw.Dispose();
                return null;
            }

            var nativeTexture = renderer.CreateNativeTexture(raw.Width, raw.Height);
            texture = new Texture(nativeTexture) { Name = options.Name };

            ScheduleOwned(renderer, nativeTexture, raw);
        }
        catch (Exception ex)
        {
            // Idempotent, and a no-op if the decode itself was what failed.
            raw.Dispose();

            Logger.Error($"Failed to create texture '{options.Name ?? "(unnamed)"}' from stream: {ex.Message}");
            return null;
        }

        if (string.IsNullOrEmpty(shareKey))
            return texture;

        var winner = sharedTextures.AddOrAcquire(shareKey, () => texture);

        // Another thread decoded the same key while this one was working. Theirs is already in the store,
        // so drop ours rather than leaking it.
        if (!ReferenceEquals(winner, texture))
        {
            release(texture);
            SharedTextureStatistics.RecordHit();
        }

        SharedTextureStatistics.SetKeyCount(sharedTextures.Count);

        return winner;
    }
}
