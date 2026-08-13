// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// Draws whatever frame an <see cref="IVideoTexture"/> currently holds, with no decoder and no
/// playback clock behind it. Used by
/// <see cref="Sakura.Framework.Graphics.Performance.TextureViewerDisplay"/> to preview the contents of
/// a video texture pool.
/// </summary>
public partial class VideoTexturePreview : Drawable
{
    private readonly IVideoTexture videoTexture;
    private readonly IShader? videoShader;

    /// <summary>
    /// Compiles the shader a preview draws with. Must be called on the draw thread.
    /// </summary>
    public static IShader CreateShader(IRenderer renderer) => renderer.CreateShader(renderer.ShaderStorage, "video.vert", "video.frag");

    /// <param name="videoTexture">The texture to preview. Borrowed, not owned.</param>
    /// <param name="videoShader">
    /// A shader from <see cref="CreateShader"/>, owned by the caller. When null (it has not finished
    /// compiling yet), nothing is drawn.
    /// </param>
    public VideoTexturePreview(IVideoTexture videoTexture, IShader? videoShader)
    {
        this.videoTexture = videoTexture;
        this.videoShader = videoShader;
    }

    protected override DrawNode CreateDrawNode() => new VideoDrawNode();

    public override DrawNode GenerateDrawNodeSubtree(int frameIndex)
    {
        var node = base.GenerateDrawNodeSubtree(frameIndex) as VideoDrawNode;
        node?.ApplyVideoState(videoTexture, videoTexture.ConversionMatrix, videoShader);
        return node!;
    }
}
