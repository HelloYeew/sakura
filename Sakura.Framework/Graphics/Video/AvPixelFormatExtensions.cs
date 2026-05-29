// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using FFmpeg.AutoGen;

namespace Sakura.Framework.Graphics.Video;

internal static class AvPixelFormatExtensions
{
    public static bool IsHardwareFormat(this AVPixelFormat fmt) => fmt switch
    {
        AVPixelFormat.AV_PIX_FMT_VDPAU => true,
        AVPixelFormat.AV_PIX_FMT_CUDA => true,
        AVPixelFormat.AV_PIX_FMT_VAAPI => true,
        AVPixelFormat.AV_PIX_FMT_DXVA2_VLD => true,
        AVPixelFormat.AV_PIX_FMT_D3D11 => true,
        AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX => true,
        AVPixelFormat.AV_PIX_FMT_MEDIACODEC => true,
        _ => false,
    };
}
