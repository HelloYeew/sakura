// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Sakura.Framework.SPIRV;

namespace Sakura.Framework.Tests.SPIRV;

/// <summary>
/// Tests SPIRV with framework shader
/// </summary>
[TestFixture]
public class SpirvCompilationTest
{
    private static string? shaderVert;
    private static string? shaderFrag;
    private static string? videoVert;
    private static string? videoFrag;
    private static string? videoNv12Frag;

    [OneTimeSetUp]
    public void LoadFrameworkShaders()
    {
        string? shaderDir = findShadersDirectory();

        if (shaderDir != null)
        {
            shaderVert = readShaderWithIncludes(shaderDir, "shader.vert");
            shaderFrag = readShaderWithIncludes(shaderDir, "shader.frag");
            videoVert = readShaderWithIncludes(shaderDir, "video.vert");
            videoFrag = readShaderWithIncludes(shaderDir, "video.frag");
            videoNv12Frag = readShaderWithIncludes(shaderDir, "video_nv12.frag");
        }
    }

    // framework shaders older than #version 450 can't be run through the SPIR-V pipeline yet
    private static bool shadersAreSpirVCompatible(string? src) =>
        src != null && src.Contains("#version 450");

    #region Framework shader tests

    [Test]
    public void FrameworkMainShader_CompilesTo_GLSL()
    {
        if (shaderVert == null) Assert.Ignore("Framework shader files not found — skipping.");
        if (!shadersAreSpirVCompatible(shaderVert))
            Assert.Ignore("Framework shaders are not #version 450 yet.");

        var result = compileVertexFragment(shaderVert!, shaderFrag!, CrossCompileTarget.GLSL);

        Assert.That(result.VertexShader, Does.Contain("void main"));
        Assert.That(result.FragmentShader, Does.Contain("void main"));

        Console.WriteLine($"main shader → GLSL: vert={result.VertexShader.Length}b, frag={result.FragmentShader.Length}b");
    }

    [Test]
    public void FrameworkMainShader_CompilesTo_MSL()
    {
        if (shaderVert == null)
            Assert.Ignore("Framework shader files not found — skipping.");
        if (!shadersAreSpirVCompatible(shaderVert))
            Assert.Ignore("Framework shaders are not #version 450 yet.");

        var result = compileVertexFragment(shaderVert!, shaderFrag!, CrossCompileTarget.MSL,
            options: new CrossCompileOptions(fixClipSpaceZ: false, invertVertexOutputY: true));

        Assert.That(result.VertexShader, Does.Contain("#include <metal_stdlib>"));
        Assert.That(result.FragmentShader, Does.Contain("#include <metal_stdlib>"));

        Console.WriteLine($"main shader → MSL: vert={result.VertexShader.Length}b, frag={result.FragmentShader.Length}b");
        Console.WriteLine(result.VertexShader[..Math.Min(500, result.VertexShader.Length)]);
    }

    [Test]
    public void FrameworkMainShader_CompilesTo_HLSL()
    {
        if (shaderVert == null)
            Assert.Ignore("Framework shader files not found — skipping.");
        if (!shadersAreSpirVCompatible(shaderVert))
            Assert.Ignore("Framework shaders are not #version 450 yet.");

        var result = compileVertexFragment(shaderVert!, shaderFrag!, CrossCompileTarget.HLSL,
            options: new CrossCompileOptions(fixClipSpaceZ: true, invertVertexOutputY: true));

        // SPIRV-Cross emits HLSL system-value semantics: SV_Position on the vertex output and
        // SV_Target on the fragment output.
        Assert.That(result.VertexShader, Does.Contain("SV_Position"));
        Assert.That(result.FragmentShader, Does.Contain("SV_Target"));

        Console.WriteLine($"main shader → HLSL: vert={result.VertexShader.Length}b, frag={result.FragmentShader.Length}b");
        Console.WriteLine(result.VertexShader[..Math.Min(500, result.VertexShader.Length)]);
    }

    [Test]
    public void FrameworkVideoShader_CompilesTo_GLSL()
    {
        if (videoVert == null)
            Assert.Ignore("Framework video shader files not found — skipping.");
        if (!shadersAreSpirVCompatible(videoVert))
            Assert.Ignore("Framework video shaders are not #version 450 yet.");

        var result = compileVertexFragment(videoVert, videoFrag!, CrossCompileTarget.GLSL);

        Assert.That(result.VertexShader, Does.Contain("void main"));
        Assert.That(result.FragmentShader, Does.Contain("void main"));

        Console.WriteLine($"video shader → GLSL: vert={result.VertexShader.Length}b, frag={result.FragmentShader.Length}b");
    }

    [Test]
    public void FrameworkVideoShader_CompilesTo_MSL()
    {
        if (videoVert == null)
            Assert.Ignore("Framework video shader files not found — skipping.");
        if (!shadersAreSpirVCompatible(videoVert))
            Assert.Ignore("Framework video shaders are not #version 450 yet.");

        var result = compileVertexFragment(videoVert!, videoFrag!, CrossCompileTarget.MSL,
            options: new CrossCompileOptions(fixClipSpaceZ: false, invertVertexOutputY: true));

        Assert.That(result.VertexShader, Does.Contain("#include <metal_stdlib>"));

        Console.WriteLine($"video shader → MSL: vert={result.VertexShader.Length}b, frag={result.FragmentShader.Length}b");
    }

    [Test]
    public void FrameworkVideoShader_CompilesTo_HLSL()
    {
        if (videoVert == null)
            Assert.Ignore("Framework video shader files not found — skipping.");
        if (!shadersAreSpirVCompatible(videoVert))
            Assert.Ignore("Framework video shaders are not #version 450 yet.");

        var result = compileVertexFragment(videoVert!, videoFrag!, CrossCompileTarget.HLSL,
            options: new CrossCompileOptions(fixClipSpaceZ: true, invertVertexOutputY: true));

        Assert.That(result.VertexShader, Does.Contain("SV_Position"));
        Assert.That(result.FragmentShader, Does.Contain("SV_Target"));

        Console.WriteLine($"video shader → HLSL: vert={result.VertexShader.Length}b, frag={result.FragmentShader.Length}b");
    }

    /// <summary>
    /// The NV12 variant differs from <c>video.frag</c> only in its fetch, but it is the fetch that the
    /// cross-compilers have to agree about: two sampled images rather than three, and a two-channel
    /// read from the second. A variant that compiled but landed its samplers at unexpected slots would
    /// draw a picture, just not the right one.
    /// </summary>
    [Test]
    public void FrameworkVideoNv12Shader_CompilesTo_AllTargets()
    {
        if (videoNv12Frag == null)
            Assert.Ignore("Framework video shader files not found — skipping.");
        if (!shadersAreSpirVCompatible(videoVert))
            Assert.Ignore("Framework video shaders are not #version 450 yet.");

        var glsl = compileVertexFragment(videoVert!, videoNv12Frag!, CrossCompileTarget.GLSL);
        var msl = compileVertexFragment(videoVert!, videoNv12Frag!, CrossCompileTarget.MSL,
            options: new CrossCompileOptions(fixClipSpaceZ: false, invertVertexOutputY: true));
        var hlsl = compileVertexFragment(videoVert!, videoNv12Frag!, CrossCompileTarget.HLSL,
            options: new CrossCompileOptions(fixClipSpaceZ: true, invertVertexOutputY: true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(glsl.FragmentShader, Does.Contain("void main"));
            Assert.That(msl.FragmentShader, Does.Contain("#include <metal_stdlib>"));
            Assert.That(hlsl.FragmentShader, Does.Contain("SV_Target"));

            // Metal binds planes by slot in BindPlanes, so the two samplers must land at texture(0)
            // and texture(1) — there is no name-based fallback on that backend to save a mismatch.
            Assert.That(msl.FragmentShader, Does.Contain("texture(0)"));
            Assert.That(msl.FragmentShader, Does.Contain("texture(1)"));
            Assert.That(msl.FragmentShader, Does.Not.Contain("texture(2)"), "NV12 has two planes; a third sampler means the variant is sampling something that is never bound");

            // Same on Direct3D 11, where BindPlanes fills t0/t1 and nothing else. Matched on the full
            // register syntax: a bare "t2" also occurs inside every "float2" in the generated source.
            Assert.That(hlsl.FragmentShader, Does.Contain("register(t0)"));
            Assert.That(hlsl.FragmentShader, Does.Contain("register(t1)"));
            Assert.That(hlsl.FragmentShader, Does.Not.Contain("register(t2)"));
        }

        Console.WriteLine($"video_nv12 → GLSL frag={glsl.FragmentShader.Length}b, MSL frag={msl.FragmentShader.Length}b, HLSL frag={hlsl.FragmentShader.Length}b");
    }

    #endregion

    #region Helpers

    private static VertexFragmentCompilationResult compileVertexFragment(
        string vert, string frag, CrossCompileTarget target, CrossCompileOptions? options = null)
    {
        return SpirvCompilation.CompileVertexFragment(
            Encoding.UTF8.GetBytes(vert),
            Encoding.UTF8.GetBytes(frag),
            target,
            options ?? new CrossCompileOptions());
    }

    /// <summary>
    /// Walks up from the test binary to find the Sakura.Framework shaders directory.
    /// </summary>
    private static string? findShadersDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "Sakura.Framework", "Resources", "Shaders");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>
    /// Reads a shader file and resolves #include directives from the same directory.
    /// </summary>
    private static string readShaderWithIncludes(string shaderDir, string filename)
    {
        string path = Path.Combine(shaderDir, filename);
        if (!File.Exists(path)) return null!;

        string src = File.ReadAllText(path);
        var sb = new StringBuilder();

        foreach (string line in src.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("#include", StringComparison.OrdinalIgnoreCase))
            {
                int start = trimmed.IndexOfAny(new[] { '"', '<' }) + 1;
                int end = trimmed.LastIndexOfAny(new[] { '"', '>' });
                if (start > 0 && end > start)
                {
                    string includePath = Path.Combine(shaderDir, trimmed[start..end]);
                    if (File.Exists(includePath)) { sb.AppendLine(File.ReadAllText(includePath)); continue; }
                }
            }
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    #endregion
}
