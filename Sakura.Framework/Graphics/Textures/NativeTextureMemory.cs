// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Sizing for the GPU allocations behind the native texture backends, so the three of them report the
/// same figure for the same texture.
/// </summary>
/// <remarks>
/// Kept in one place because it is an assumption, not a measurement: the backends allocate RGBA8 color
/// textures and this multiplies out their dimensions. It does not know about driver-side padding,
/// alignment, or the extra plane a depth or multisample attachment would carry, so the total is a close
/// floor rather than an exact VRAM figure. That is enough for the question being asked — whether a
/// category grows and never comes back down — and pretending to more precision than the backends expose
/// would be worse than stating the assumption.
/// </remarks>
internal static class NativeTextureMemory
{
    /// <summary>
    /// Bytes per pixel for the RGBA8 format every color texture in the framework uses.
    /// </summary>
    private const int bytes_per_pixel = 4;

    /// <summary>
    /// Bytes an RGBA8 texture of the given dimensions occupies.
    /// </summary>
    internal static long BytesFor(int width, int height)
        => (long)Math.Max(0, width) * Math.Max(0, height) * bytes_per_pixel;

    /// <summary>
    /// Records an RGBA8 texture allocation of the given dimensions and returns its lease.
    /// </summary>
    internal static NativeMemoryLease Lease(NativeMemoryCategory category, int width, int height)
        => NativeMemoryTracker.Add(category, BytesFor(width, height));

    /// <summary>
    /// Bytes the three single-channel planes of a YUV420P video frame occupy: a full-resolution luma
    /// plane plus two chroma planes at half resolution per axis, rounded up exactly as the video
    /// textures size them.
    /// </summary>
    internal static long BytesForVideoPlanes(int width, int height)
    {
        long chromaWidth = (Math.Max(0, width) + 1) / 2;
        long chromaHeight = (Math.Max(0, height) + 1) / 2;

        return (long)Math.Max(0, width) * Math.Max(0, height) + 2 * chromaWidth * chromaHeight;
    }
}
