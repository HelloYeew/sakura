// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// Direct3D 11-specific renderer extensions.
/// Only cast to this interface from code that is already D3D11-aware (e.g. the buffered-container
/// effect passes, which need a raw, slot-management-free draw)
/// </summary>
public interface ID3D11Renderer : IRenderer
{
    /// <summary>
    /// Uploads the given vertices and issues a raw triangle draw using whatever shader/pipeline is
    /// currently bound, without touching the renderer's texture-slot or clip-injection bookkeeping.
    /// Used by the buffered-container effect passes, which bind their own effect shader + source
    /// texture.
    /// </summary>
    void DrawVerticesRaw(ReadOnlySpan<Vertex.Vertex> vertices);

    /// <summary>
    /// The raw <c>ID3D11Device*</c> this renderer draws with, or <see cref="nint.Zero"/> before
    /// initialisation.
    /// </summary>
    /// <remarks>
    /// Exposed for one purpose: handing the device to FFmpeg's D3D11VA hardware context so the decoder
    /// produces frames on the *same* device this renderer samples them from. Letting FFmpeg create its
    /// own device would put every frame on a different device, and crossing that boundary costs a
    /// shared-resource copy — which is the entire thing zero-copy exists to avoid. The caller does not
    /// own this reference and must not release it.
    /// </remarks>
    nint NativeDevicePointer { get; }
}
