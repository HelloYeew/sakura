// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Silk.NET.OpenGL;
using Shader = Sakura.Framework.Graphics.Rendering.Shader;

namespace Sakura.Framework.Graphics.Video;

/// <summary>
/// Renders a YUV420P video frame using the dedicated video shader.
/// All GL calls run on the draw thread inside <see cref="Draw"/>.
/// Uses <see cref="IRenderer.DrawVerticesRaw"/> to bypass the renderer's texture-slot
/// management so that Y/U/V planes bound to units 0/1/2 are never overwritten.
/// </summary>
internal class VideoDrawNode : DrawNode
{
    // Written by ApplyVideoState() on the update thread, read by Draw() on the draw thread.
    // Triple-buffering (one node per frame index) keeps this race-free.
    private VideoGLTexture? yuvTexture;
    private float[]? yuvMatrix;
    private Shader? videoShader;
    private GL? gl;

    public void ApplyVideoState(VideoSprite source, VideoGLTexture? tex, float[]? matrix, Shader? shader, GL glRef)
    {
        yuvTexture  = tex;
        yuvMatrix   = matrix;
        videoShader = shader;
        gl          = glRef;
    }

    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0 || Vertices.Length == 0)
            return;

        // Shader not compiled yet or no frame uploaded yet — nothing to show.
        if (yuvTexture == null || !yuvTexture.Available || videoShader == null || gl == null)
            return;

        // 1. Flush pending batch so earlier drawables render with the main shader.
        renderer.FlushBatch();

        // 2. Switch to the video shader and set required uniforms.
        videoShader.Use();

        // u_Projection must be set on every shader program that uses it —
        // each GL program has its own uniform state, independent of other programs.
        videoShader.SetUniform("u_Projection", renderer.ProjectionMatrix);

        // 3. Bind Y/U/V planes to texture units 0/1/2.
        //    These bindings must NOT be touched by DrawVertices (hence DrawVerticesRaw below).
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, yuvTexture.YHandle);

        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(TextureTarget.Texture2D, yuvTexture.UHandle);

        gl.ActiveTexture(TextureUnit.Texture2);
        gl.BindTexture(TextureTarget.Texture2D, yuvTexture.VHandle);

        videoShader.SetUniform("u_TextureY", 0);
        videoShader.SetUniform("u_TextureU", 1);
        videoShader.SetUniform("u_TextureV", 2);

        if (yuvMatrix != null)
            setMatrix3Uniform("u_YuvCoeff", yuvMatrix);

        // 4. Upload + draw the quad directly — no texture-slot management, no binding overwrite.
        renderer.DrawVerticesRaw(Vertices);

        // 5. Restore the main shader so subsequent drawables render correctly.
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
