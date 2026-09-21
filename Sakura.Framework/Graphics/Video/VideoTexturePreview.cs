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
    private IVideoTexture? videoTexture;
    private VideoShaderSet? videoShaders;

    /// <summary>
    /// Compiles the shaders a preview draws with. Must be called on the draw thread. A pool can hold
    /// either layout, so both variants are needed — see <see cref="VideoShaderSet"/>.
    /// </summary>
    public static VideoShaderSet CreateShader(IRenderer renderer) => VideoShaderSet.Create(renderer);

    /// <param name="videoTexture">The texture to preview. Borrowed, not owned.</param>
    /// <param name="videoShaders">
    /// Shaders from <see cref="CreateShader"/>, owned by the caller. When null (they have not finished
    /// compiling yet), nothing is drawn.
    /// </param>
    public VideoTexturePreview(IVideoTexture? videoTexture, VideoShaderSet? videoShaders)
    {
        this.videoTexture = videoTexture;
        this.videoShaders = videoShaders;
    }

    /// <summary>
    /// Points this preview at a different texture or at a shader that has since finished compiling,
    /// mainly use it for texture viewer previews.
    /// </summary>
    public void Bind(IVideoTexture? texture, VideoShaderSet? shaders)
    {
        videoTexture = texture;
        videoShaders = shaders;
    }

    protected override DrawNode CreateDrawNode() => new VideoDrawNode();

    public override DrawNode GenerateDrawNodeSubtree(int frameIndex)
    {
        var node = base.GenerateDrawNodeSubtree(frameIndex) as VideoDrawNode;
        node?.ApplyVideoState(videoTexture, videoTexture?.ConversionMatrix,
            videoTexture != null ? videoShaders?.For(videoTexture.Layout) : null);
        return node!;
    }
}
