// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// Everything about a decoded frame that decides which <see cref="VideoTexture"/> can hold it. The
/// decoder's pool is warmed for exactly one shape at a time; a frame that does not match means the
/// pool has to be rebuilt and the old textures dropped as they come back, rather than uploaded into.
/// </summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="Layout">
/// The plane set, which also picks the shader variant — see <see cref="VideoPlaneLayout"/>.
/// </param>
/// <param name="FromHardwareFrame">
/// Whether frames of this shape stay in the decoder's own GPU memory and are sampled there, rather
/// than being read back and uploaded.
/// <para>
/// This is part of the shape and not a separate flag because the two kinds of texture are not
/// interchangeable even at identical dimensions and layout: one owns plane storage and copies into it,
/// the other borrows a <c>CVPixelBuffer</c> it is handed. <see cref="Layout"/> nearly covers it in
/// practice — toggling hardware acceleration flips NV12 to YUV420P — but only by accident, since
/// software decode of some codecs also yields NV12.
/// </para>
/// </param>
internal readonly record struct VideoTextureShape(
    int Width,
    int Height,
    VideoPlaneLayout Layout,
    bool FromHardwareFrame);
