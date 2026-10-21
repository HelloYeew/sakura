// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Graphics.Rendering.Direct3D11;

/// <summary>
/// Direct3D 11-specific renderer extensions.
/// Only cast to this interface from code that is already D3D11-aware (e.g., the D3D11VA hardware
/// decoder, which needs the device)
/// </summary>
public interface ID3D11Renderer : ICustomShaderRenderer
{
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
