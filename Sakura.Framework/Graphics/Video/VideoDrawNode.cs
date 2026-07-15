// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Metal;
using Sakura.Framework.Graphics.Rendering.Uniforms;
using Sakura.Framework.Graphics.Textures;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// Renders a YUV420P video frame using the dedicated video shader.
/// All GPU calls run on the draw thread inside <see cref="Draw"/>.
/// No GL types are referenced here — GL stays inside <see cref="VideoTexture"/>
/// and <see cref="Sakura.Framework.Graphics.Textures.VideoGLTexture"/>.
/// </summary>
internal class VideoDrawNode : DrawNode
{
    private VideoTexture? videoTexture;
    private float[]? yuvMatrix;
    private IShader? videoShader;

    public void ApplyVideoState(VideoTexture? tex, float[]? matrix, IShader? shader)
    {
        videoTexture = tex;
        yuvMatrix = matrix;
        videoShader = shader;
    }

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || Vertices.Length == 0)
            return;

        if (videoTexture == null || videoShader == null)
            return;

        if (!videoTexture.UploadComplete)
            return;

        // TODO: This is still really bad????
        bool isGL = renderer is IGLRenderer;
        bool isMetal = renderer is IMetalRenderer;
        bool isD3D11 = renderer is Rendering.Direct3D11.ID3D11Renderer;
        if (!isGL && !isMetal && !isD3D11)
            return;

        renderer.FlushBatch();

        videoShader.Use();
        videoShader.SetUniformBlock("ProjectionBlock", new ProjectionBlock
            {
                Projection = renderer.ProjectionMatrix
            });

        // Bind Y/U/V planes to texture units 0/1/2 — backend specifics stay inside VideoTexture.
        // Tile fill repeats the frame (UVs > 1), so the planes need a repeating wrap; otherwise clamp.
        videoTexture.BindPlanes(FillMode == TextureFillMode.Tile);

        // GL maps sampler uniforms by name; on Metal these are no-ops (planes are bound by slot in
        // BindPlanes, matching the shader's [[texture(0/1/2)]]).
        videoShader.SetUniform("u_TextureY", 0);
        videoShader.SetUniform("u_TextureU", 1);
        videoShader.SetUniform("u_TextureV", 2);

        if (yuvMatrix != null)
            videoShader.SetUniformBlock("VideoBlock", VideoBlock.FromMat3(yuvMatrix));

        if (renderer is IGLRenderer glRenderer)
            glRenderer.DrawVerticesRaw(Vertices);
        else if (renderer is IMetalRenderer metalRenderer)
            metalRenderer.DrawVerticesRaw(Vertices);
        else if (renderer is Rendering.Direct3D11.ID3D11Renderer d3D11Renderer)
            d3D11Renderer.DrawVerticesRaw(Vertices);

        renderer.RestoreMainShader();
    }
}
