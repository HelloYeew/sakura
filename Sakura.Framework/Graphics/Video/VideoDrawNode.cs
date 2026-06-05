// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Rendering;

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

        // Video rendering requires GL-specific operations (sRGB toggle, raw vertex upload).
        // On non-GL renderers video is silently skipped until a backend-agnostic path exists.
        if (renderer is not IGLRenderer glRenderer)
            return;

        renderer.FlushBatch();

        videoShader.Use();
        videoShader.SetUniform("u_Projection", renderer.ProjectionMatrix);

        // Bind Y/U/V planes to texture units 0/1/2 — GL stays inside VideoTexture.
        videoTexture.BindPlanes();

        videoShader.SetUniform("u_TextureY", 0);
        videoShader.SetUniform("u_TextureU", 1);
        videoShader.SetUniform("u_TextureV", 2);

        if (yuvMatrix != null)
            videoShader.SetUniform("u_YuvCoeff", yuvMatrix);

        glRenderer.DisableSrgb();
        glRenderer.DrawVerticesRaw(Vertices);
        glRenderer.RestoreSrgb();
        renderer.RestoreMainShader();
    }
}
