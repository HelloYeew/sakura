// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.IO;
using Sakura.Framework.Platform;
using Sakura.Framework.SPIRV;

namespace Sakura.Framework.Tests.Rendering;

[TestFixture]
public class ShaderCompilerTest
{
    private string tempDir = null!;
    private NativeStorage sourceStorage = null!;
    private DiskCache cache = null!;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "sakura-shadercompiler-test", Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        sourceStorage = new NativeStorage(tempDir);
        cache = new DiskCache(new NativeStorage(Path.Combine(tempDir, "cache")));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
    }

    private void writeSource(string name, string content) => File.WriteAllText(Path.Combine(tempDir, name), content);

    #region #include resolution (no GPU / compiler needed)

    [Test]
    public void ReadWithIncludes_InlinesIncludedFile()
    {
        writeSource("util.glsl", "float helper() { return 1.0; }");
        writeSource("main.frag", "#include \"util.glsl\"\nvoid main() { }");

        string resolved = ShaderCompiler.ReadWithIncludes(sourceStorage, "main.frag");

        Assert.That(resolved, Does.Contain("float helper()"));
        Assert.That(resolved, Does.Contain("void main()"));
        Assert.That(resolved, Does.Not.Contain("#include"));
    }

    [Test]
    public void ReadWithIncludes_ResolvesNestedIncludes()
    {
        writeSource("a.glsl", "// a");
        writeSource("b.glsl", "#include \"a.glsl\"\n// b");
        writeSource("main.frag", "#include \"b.glsl\"\n// main");

        string resolved = ShaderCompiler.ReadWithIncludes(sourceStorage, "main.frag");

        Assert.That(resolved, Does.Contain("// a"));
        Assert.That(resolved, Does.Contain("// b"));
        Assert.That(resolved, Does.Contain("// main"));
    }

    [Test]
    public void ReadWithIncludes_MissingInclude_Throws()
    {
        writeSource("main.frag", "#include \"nope.glsl\"\nvoid main() { }");

        Assert.That(() => ShaderCompiler.ReadWithIncludes(sourceStorage, "main.frag"),
            Throws.TypeOf<FileNotFoundException>());
    }

    #endregion

    #region Compilation + caching

    private const string minimal_vert_450 =
        "#version 450\n" +
        "layout(location = 0) in vec2 aPosition;\n" +
        "layout(set = 0, binding = 0) uniform ProjBlock { mat4 u_Projection; };\n" +
        "void main() { gl_Position = u_Projection * vec4(aPosition, 0.0, 1.0); }\n";

    private const string minimal_frag_450 =
        "#version 450\n" +
        "layout(location = 0) out vec4 FragColor;\n" +
        "void main() { FragColor = vec4(1.0); }\n";

    private static bool tryCompile(out Exception error)
    {
        try
        {
            ShaderCompiler.GetOrCompileSource(minimal_vert_450, minimal_frag_450, CrossCompileTarget.GLSL);
            error = null!;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    [Test]
    public void CompilesMinimalShader_ToGLSL()
    {
        if (!tryCompile(out Exception err))
            Assert.Ignore($"SPIR-V native compiler unavailable on this platform: {err.Message}");

        var (vert, frag) = ShaderCompiler.GetOrCompileSource(
            minimal_vert_450, minimal_frag_450, CrossCompileTarget.GLSL);

        Assert.That(vert, Does.Contain("void main"));
        Assert.That(frag, Does.Contain("void main"));
    }

    [Test]
    public void CompilesMinimalShader_ToMSL()
    {
        if (!tryCompile(out Exception err))
            Assert.Ignore($"SPIR-V native compiler unavailable on this platform: {err.Message}");

        var (vert, frag) = ShaderCompiler.GetOrCompileSource(
            minimal_vert_450, minimal_frag_450, CrossCompileTarget.MSL);

        Assert.That(vert, Does.Contain("#include <metal_stdlib>"));
        Assert.That(frag, Does.Contain("#include <metal_stdlib>"));
    }

    [Test]
    public void SecondCall_HitsCache_AndReturnsSameOutput()
    {
        if (!tryCompile(out Exception err))
            Assert.Ignore($"SPIR-V native compiler unavailable on this platform: {err.Message}");

        var first = ShaderCompiler.GetOrCompileSource(
            minimal_vert_450, minimal_frag_450, CrossCompileTarget.GLSL, cache);

        // The cache directory should now contain exactly one entry.
        string cacheDir = Path.Combine(tempDir, "cache");
        Assert.That(Directory.GetFiles(cacheDir), Has.Length.EqualTo(1));

        var second = ShaderCompiler.GetOrCompileSource(
            minimal_vert_450, minimal_frag_450, CrossCompileTarget.GLSL, cache);

        Assert.That(second.vert, Is.EqualTo(first.vert));
        Assert.That(second.frag, Is.EqualTo(first.frag));
        // Still only one entry, the second call read it rather than writing a new one.
        Assert.That(Directory.GetFiles(cacheDir), Has.Length.EqualTo(1));
    }

    [Test]
    public void DifferentTarget_ProducesSeparateCacheEntry()
    {
        if (!tryCompile(out Exception err))
            Assert.Ignore($"SPIR-V native compiler unavailable on this platform: {err.Message}");

        ShaderCompiler.GetOrCompileSource(minimal_vert_450, minimal_frag_450, CrossCompileTarget.GLSL, cache);
        ShaderCompiler.GetOrCompileSource(minimal_vert_450, minimal_frag_450, CrossCompileTarget.MSL, cache);

        string cacheDir = Path.Combine(tempDir, "cache");
        Assert.That(Directory.GetFiles(cacheDir), Has.Length.EqualTo(2));
    }

    #endregion
}
