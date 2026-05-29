// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Rendering;
using Silk.NET.OpenGL;
using Shader = Sakura.Framework.Graphics.Rendering.Shader;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// Renders a YUV420P video frame using the dedicated video shader.
/// All GL calls run on the draw thread inside <see cref="Draw"/>.
/// </summary>
internal class VideoDrawNode : DrawNode
{
    private VideoTexture? videoTexture;
    private float[]? yuvMatrix;
    private Shader? videoShader;
    private GL? gl;

    public void ApplyVideoState(VideoSprite source, VideoTexture? tex, float[]? matrix, Shader? shader, GL glRef)
    {
        videoTexture = tex;
        yuvMatrix = matrix;
        videoShader = shader;
        gl = glRef;
    }

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || Vertices.Length == 0)
            return;

        if (videoTexture == null || videoShader == null || gl == null)
            return;

        if (!videoTexture.UploadComplete)
            return;

        var glTex = videoTexture.GlTexture;

        renderer.FlushBatch();

        videoShader.Use();
        videoShader.SetUniform("u_Projection", renderer.ProjectionMatrix);

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, glTex.YHandle);

        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(TextureTarget.Texture2D, glTex.UHandle);

        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(TextureTarget.Texture2D, glTex.VHandle);

        videoShader.SetUniform("u_TextureY", 0);
        videoShader.SetUniform("u_TextureU", 1);
        videoShader.SetUniform("u_TextureV", 2);

        if (yuvMatrix != null)
            setMatrix3Uniform("u_YuvCoeff", yuvMatrix);

        renderer.DrawVerticesRaw(Vertices);
        renderer.RestoreMainShader();
    }

    private unsafe void setMatrix3Uniform(string name, float[] mat)
    {
        int loc = gl!.GetUniformLocation(videoShader!.Handle, name);
        if (loc == -1) return;

        fixed (float* p = mat)
            gl.UniformMatrix3(loc, 1, false, p);
    }
}
