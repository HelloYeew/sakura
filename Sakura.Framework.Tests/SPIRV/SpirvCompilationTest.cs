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

    [OneTimeSetUp]
    public void LoadFrameworkShaders()
    {
        string? shaderDir = findShadersDirectory();

        if (shaderDir != null)
        {
            shaderVert = readShaderWithIncludes(shaderDir, "shader.vert");
            shaderFrag = readShaderWithIncludes(shaderDir, "shader.frag");
            videoVert  = readShaderWithIncludes(shaderDir, "video.vert");
            videoFrag  = readShaderWithIncludes(shaderDir, "video.frag");
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
