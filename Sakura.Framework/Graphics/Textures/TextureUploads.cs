// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Statistic;

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
    /// Bounds how many image decodes run at once, process-wide.
    /// </summary>
    public static int MaxConcurrentDecodes
    {
        get => maxConcurrentDecodes;
        set
        {
            int clamped = Math.Max(1, value);

            lock (decode_gate_lock)
            {
                if (clamped == maxConcurrentDecodes)
                    return;

                maxConcurrentDecodes = clamped;

                // SemaphoreSlim can only be released beyond its initial
                // count, not resized, and a decode in flight still holds a permit on the old instance.
                decodeGate = new SemaphoreSlim(clamped, clamped);
            }
        }
    }

    private static int maxConcurrentDecodes = 1;

    private static readonly Lock decode_gate_lock = new Lock();

    private static SemaphoreSlim decodeGate = new SemaphoreSlim(1, 1);

    /// <summary>
    /// How many decoded images may be waiting for their GPU upload before a new decode is made to wait.
    /// Zero or less disables the limit.
    /// </summary>
    public static int MaxOutstandingUploads { get; set; }

    /// <summary>
    /// How long a decode waits for upload headroom before giving up and proceeding anyway.
    /// </summary>
    /// <remarks>
    /// A memory optimization must never be able to wedge loading. If the draw thread is not draining the
    /// queue (a minimized window, a renderer being disposed, a queue nobody pumps) waiting forever would
    /// hang every texture load, so the wait is bounded and simply degrades to the previous behavior.
    /// Settable so tests do not have to spend it.
    /// </remarks>
    internal static TimeSpan UploadHeadroomTimeout { get; set; } = TimeSpan.FromSeconds(1);

    private static readonly object upload_headroom_lock = new object();

    private static int outstandingUploads;

    /// <summary>
    /// Set when a wait for headroom timed out, and cleared by the next upload that completes. While it is
    /// set the limit is not enforced, so a stalled queue costs one timeout in total rather than one per
    /// decode.
    /// </summary>
    private static bool uploadQueueStalled;

    private static readonly GlobalStatistic<int> stat_outstanding_uploads
        = GlobalStatistics.Get<int>("Textures", "Uploads Awaiting Draw Thread");

    /// <summary>
    /// Decoded buffers currently rented and waiting for their upload to run.
    /// </summary>
    internal static int OutstandingUploads
    {
        get
        {
            lock (upload_headroom_lock)
                return outstandingUploads;
        }
    }

    /// <summary>
    /// Blocks until fewer than <see cref="MaxOutstandingUploads"/> decoded buffers are awaiting upload.
    /// Called before a decode rents anything, since waiting afterward would hold the very buffer it is
    /// trying not to duplicate.
    /// </summary>
    private static void waitForUploadHeadroom()
    {
        int limit = MaxOutstandingUploads;

        if (limit <= 0)
            return;

        // Only thread-pool callers are made to wait, and this is a correctness rule rather than a
        // preference. The queue is drained by the draw thread, so anything waiting here depends on that
        // thread making progress — and a synchronous load *on* the draw thread would then be waiting for
        // uploads only it can run. The framework's own threads are dedicated (`new Thread`, see AppThread),
        // so they are never pool threads, while every load that motivates this limit arrives through
        // LoadComponentAsync on the pool. Erring this way costs some back-pressure on an unusual caller;
        // erring the other way risks a stall on the one thread that must never stall.
        if (!Thread.CurrentThread.IsThreadPoolThread)
            return;

        lock (upload_headroom_lock)
        {
            // A queue already known to be stalled is not waited on at all — see uploadQueueStalled.
            while (!uploadQueueStalled && outstandingUploads >= limit)
            {
                if (Monitor.Wait(upload_headroom_lock, UploadHeadroomTimeout))
                    continue;

                // Nothing drained for a whole timeout, so stop enforcing the limit until something does.
                // The count is deliberately *not* cleared: every queued upload still releases its slot if
                // the draw thread comes back, so the count is the truth and clearing it would let far more
                // rentals through than intended once draining resumes.
                Logger.Log($"Texture upload queue did not drain within {UploadHeadroomTimeout.TotalMilliseconds:N0} ms "
                           + $"with {outstandingUploads} upload(s) outstanding; not enforcing the limit until it does.",
                    level: LogLevel.Debug);

                uploadQueueStalled = true;
                return;
            }
        }
    }

    private static void releaseUploadSlot()
    {
        lock (upload_headroom_lock)
        {
            outstandingUploads = Math.Max(0, outstandingUploads - 1);
            stat_outstanding_uploads.Value = outstandingUploads;

            // Something drained, so whatever the stall was, it is over.
            uploadQueueStalled = false;

            Monitor.PulseAll(upload_headroom_lock);
        }
    }

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

        // Counted from here rather than from the decode, because this is the point the buffer starts waiting
        // on another thread. Released when the upload has actually run — see MaxOutstandingUploads.
        lock (upload_headroom_lock)
        {
            outstandingUploads++;
            stat_outstanding_uploads.Value = outstandingUploads;
        }

        bool scheduled = false;

        try
        {
            renderer.ScheduleTextureUpload(() =>
            {
                try
                {
                    nativeTexture.Upload(raw.Data);
                }
                finally
                {
                    raw.Dispose();
                    releaseUploadSlot();
                }
            }, bytes);

            scheduled = true;
        }
        finally
        {
            // If scheduling itself threw, the upload will never run and nothing else will release the slot.
            if (!scheduled)
                releaseUploadSlot();
        }
    }

    /// <summary>
    /// Runs one decode under <see cref="MaxConcurrentDecodes"/>.
    /// </summary>
    private static ImageRawData decode(IImageLoader imageLoader, Stream stream, TextureCreationOptions options)
    {
        // Before the gate, and before anything is rented: this waits for buffers already decoded to be
        // uploaded and returned to the pool, so it must not itself be holding one or a permit to make one.
        waitForUploadHeadroom();

        SemaphoreSlim gate;

        lock (decode_gate_lock)
            gate = decodeGate;

        gate.Wait();

        try
        {
            return imageLoader.Load(stream, options.Decode);
        }
        finally
        {
            gate.Release();
        }
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
            // Deliberately outside the shared store's lock: decoding is slow, and holding that lock across
            // it would serialize every texture load behind an unrelated one. The gate below bounds decode
            // concurrency without contending on the store — see MaxConcurrentDecodes for why bounding it
            // at all is the point.
            raw = decode(imageLoader, stream, options);

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
