// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Uniforms;
using Sakura.Framework.Graphics.Rendering.Vulkan;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// Compiles every framework shader to SPIR-V the way the Vulkan renderer does, reflects it, and checks
/// it against the renderer's fixed resource layout, so a shader edit that would break Vulkan fails here
/// rather than on a GPU.
/// </summary>
[TestFixture]
public class VulkanShaderLayoutTest
{
    /// <summary>
    /// Every vertex + fragment pair the framework creates. A new shader file must be added here, or
    /// <see cref="EveryFrameworkShader_IsInAPair"/> fails.
    /// </summary>
    private static readonly (string vert, string frag)[] framework_pairs =
    [
        ("shader.vert", "shader.frag"),
        ("shader.vert", "blur.frag"),
        ("shader.vert", "grayscale.frag"),
        ("video.vert", "video.frag"),
        ("video.vert", "video_nv12.frag"),
    ];

    private static readonly Storage shader_storage =
        new EmbeddedResourceStorage(typeof(ShaderCompiler).Assembly, "Sakura.Framework.Resources").GetStorageForDirectory("Shaders");

    [OneTimeSetUp]
    public void CheckCompilerAvailable()
    {
        try
        {
            ShaderCompiler.GetOrCompileSpirvSource(
                "#version 450\nvoid main() { gl_Position = vec4(0.0); }\n",
                "#version 450\nlayout(location = 0) out vec4 c;\nvoid main() { c = vec4(1.0); }\n");
        }
        catch (Exception ex)
        {
            Assert.Ignore($"SPIR-V native compiler unavailable on this platform: {ex.Message}");
        }
    }

    private static IEnumerable<TestCaseData> pairsInBothModes()
    {
        foreach (var (vert, frag) in framework_pairs)
        {
            yield return new TestCaseData(vert, frag, false).SetName($"{vert}+{frag} (optimised)");
            yield return new TestCaseData(vert, frag, true).SetName($"{vert}+{frag} (debug)");
        }
    }

    private static (SpirvReflection vert, SpirvReflection frag) reflect(string vert, string frag, bool debug = false)
        => reflect(ShaderCompiler.GetOrCompileSpirv(shader_storage, vert, frag, debug: debug));

    private static (SpirvReflection vert, SpirvReflection frag) reflect(SpirvShaderPair pair)
        => (SpirvReflection.Parse(pair.VertexForReflection), SpirvReflection.Parse(pair.FragmentForReflection));

    private static VulkanShaderLayout layoutOf(string vert, string frag, bool debug = false)
    {
        var (vs, fs) = reflect(vert, frag, debug);
        return VulkanShaderLayout.Create(vs, fs, $"{vert}+{frag}");
    }

    #region Framework shaders

    [TestCaseSource(nameof(pairsInBothModes))]
    public void FrameworkShaderPair_FitsLayout(string vert, string frag, bool debug)
    {
        Assert.DoesNotThrow(() => layoutOf(vert, frag, debug));
    }

    /// <summary>
    /// Names are reflected from debug copies because the optimiser strips them, which is only sound if
    /// the optimised modules the driver runs keep every binding those copies describe.
    /// </summary>
    [TestCaseSource(nameof(frameworkPairs))]
    public void OptimisedModules_KeepTheBindingsOfTheirReflectionCopies(string vert, string frag)
    {
        var pair = ShaderCompiler.GetOrCompileSpirv(shader_storage, vert, frag);
        var (namedVert, namedFrag) = reflect(pair);
        var optimisedVert = SpirvReflection.Parse(pair.Vertex);
        var optimisedFrag = SpirvReflection.Parse(pair.Fragment);

        Assert.That(optimisedVert.UniformBlocks.Concat(optimisedFrag.UniformBlocks).All(b => b.Name.Length == 0), Is.True,
            "the optimiser kept block names; if that is now reliable the reflection copies could go");

        assertSameBindings(namedVert, optimisedVert);
        assertSameBindings(namedFrag, optimisedFrag);

        static void assertSameBindings(SpirvReflection named, SpirvReflection optimised)
        {
            Assert.That(optimised.UniformBlocks.Select(b => (b.Set, b.Binding, b.Size)), Is.SubsetOf(named.UniformBlocks.Select(b => (b.Set, b.Binding, b.Size))));
            Assert.That(optimised.Samplers.Select(t => (t.Set, t.Binding, t.ArrayLength)), Is.SubsetOf(named.Samplers.Select(t => (t.Set, t.Binding, t.ArrayLength))));
            Assert.That(optimised.VertexInputs.Select(i => (i.Location, i.ComponentCount)), Is.SubsetOf(named.VertexInputs.Select(i => (i.Location, i.ComponentCount))));
            Assert.That(optimised.UniformBlocks.SelectMany(b => b.Members.Select(m => (b.Binding, m.Offset, m.Size))),
                Is.SubsetOf(named.UniformBlocks.SelectMany(b => b.Members.Select(m => (b.Binding, m.Offset, m.Size)))));
        }
    }

    private static IEnumerable<TestCaseData> frameworkPairs()
        => framework_pairs.Select(p => new TestCaseData(p.vert, p.frag).SetName($"{p.vert}+{p.frag}"));

    [Test]
    public void EveryFrameworkShader_IsInAPair()
    {
        const string prefix = "Sakura.Framework.Resources.Shaders.";

        var paired = framework_pairs.SelectMany(p => new[] { p.vert, p.frag }).ToHashSet();
        var stages = typeof(ShaderCompiler).Assembly.GetManifestResourceNames()
                                           .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                                           .Select(n => n[prefix.Length..])
                                           .Where(f => f.EndsWith(".vert", StringComparison.Ordinal) || f.EndsWith(".frag", StringComparison.Ordinal))
                                           .ToList();

        Assert.That(stages, Is.Not.Empty, "no shaders found in the framework's embedded resources");
        Assert.That(stages, Is.SubsetOf(paired), "a framework shader isn't covered by these tests; add its pair to framework_pairs");
    }

    [TestCase("shader.vert", "shader.frag", "ProjectionBlock:0,MaskBlock:1", "u_Textures:0[16]")]
    [TestCase("shader.vert", "blur.frag", "ProjectionBlock:0,BlurBlock:3", "u_Texture:0")]
    [TestCase("shader.vert", "grayscale.frag", "ProjectionBlock:0,GrayscaleBlock:2", "u_Texture:0")]
    [TestCase("video.vert", "video.frag", "ProjectionBlock:0,VideoBlock:4", "u_TextureY:0,u_TextureU:1,u_TextureV:2")]
    [TestCase("video.vert", "video_nv12.frag", "ProjectionBlock:0,VideoBlock:4", "u_TextureY:0,u_TextureCbCr:1")]
    public void FrameworkShaderPair_HasExpectedBindings(string vert, string frag, string expectedBlocks, string expectedTextures)
    {
        var layout = layoutOf(vert, frag);

        string blocks = string.Join(",", layout.UniformBlocks.Values.OrderBy(b => b.Binding).Select(b => $"{b.Name}:{b.Binding}"));
        string textures = string.Join(",", layout.Samplers.Select(s => s.ArrayLength > 1 ? $"{s.Name}:{s.Binding}[{s.ArrayLength}]" : $"{s.Name}:{s.Binding}"));

        Assert.That(blocks, Is.EqualTo(expectedBlocks));
        Assert.That(textures, Is.EqualTo(expectedTextures));
        Assert.That(layout.UsesTextureArray, Is.EqualTo(frag == "shader.frag"));
    }

    [Test]
    public void FrameworkUniformBlocks_MatchTheirCSharpStructs()
    {
        var structs = new Dictionary<string, Type>
        {
            [nameof(ProjectionBlock)] = typeof(ProjectionBlock),
            [nameof(MaskBlock)] = typeof(MaskBlock),
            [nameof(GrayscaleBlock)] = typeof(GrayscaleBlock),
            [nameof(BlurBlock)] = typeof(BlurBlock),
            [nameof(VideoBlock)] = typeof(VideoBlock),
        };

        var seen = new HashSet<string>();

        foreach (var (vert, frag) in framework_pairs)
        {
            foreach (var block in layoutOf(vert, frag).UniformBlocks.Values)
            {
                Assert.That(structs.TryGetValue(block.Name, out var type), $"'{block.Name}' has no C# struct in {typeof(ProjectionBlock).Namespace}");
                seen.Add(block.Name);

                Assert.That(block.Size, Is.LessThanOrEqualTo(Marshal.SizeOf(type!)), $"{block.Name}: the C# struct is smaller than the block the shader reads");

                foreach (var member in block.Members)
                {
                    // GLSL members are the C# field names with a u_ prefix (u_BorderColor ↔ BorderColor).
                    string field = member.Name.StartsWith("u_", StringComparison.Ordinal) ? member.Name[2..] : member.Name;

                    Assert.That(type!.GetField(field), Is.Not.Null, $"{block.Name}.{member.Name} has no C# field '{field}'");
                    Assert.That((uint)Marshal.OffsetOf(type, field), Is.EqualTo(member.Offset), $"{block.Name}.{member.Name}: std140 offset differs from the C# field offset");
                }
            }
        }

        Assert.That(seen, Is.EquivalentTo(structs.Keys), "every uniform block struct should be used by some framework shader");
    }

    [TestCase("shader.vert")]
    [TestCase("video.vert")]
    public void FrameworkVertexShader_ReadsTheVertexFormat(string vert)
    {
        var (vs, _) = reflect(vert, "shader.frag");

        Assert.That(vs.VertexInputs.Select(i => i.Location), Is.EqualTo(Enumerable.Range(0, VulkanShaderLayout.VertexAttributes.Count).Select(i => (uint)i)));

        foreach (var input in vs.VertexInputs)
        {
            var attribute = VulkanShaderLayout.VertexAttributes[(int)input.Location];
            Assert.That(input.ComponentKind, Is.EqualTo(SpirvScalarKind.Float), input.Name);
            Assert.That(input.ComponentCount, Is.EqualTo(attribute.Components), $"{input.Name} at location {input.Location} ({attribute.Field})");
        }
    }

    #endregion

    #region Rejections

    private const string plain_vert =
        "#version 450\n" +
        "layout(location = 0) in vec2 aPosition;\n" +
        "layout(set = 0, binding = 0, std140) uniform ProjectionBlock { mat4 u_Projection; };\n" +
        "void main() { gl_Position = u_Projection * vec4(aPosition, 0.0, 1.0); }\n";

    private static string fragment(string declarations, string colour) =>
        "#version 450\n" +
        "layout(location = 0) out vec4 FragColor;\n" +
        declarations + "\n" +
        $"void main() {{ FragColor = {colour}; }}\n";

    private static void assertRejected(string vert, string frag, params string[] expectedFragments)
    {
        var (vs, fs) = reflect(ShaderCompiler.GetOrCompileSpirvSource(vert, frag));

        var ex = Assert.Throws<InvalidOperationException>(() => VulkanShaderLayout.Create(vs, fs, "custom.vert+custom.frag"));

        Assert.That(ex!.Message, Does.Contain("custom.vert+custom.frag"));

        foreach (string expected in expectedFragments)
            Assert.That(ex.Message, Does.Contain(expected));
    }

    [Test]
    public void SingleTextures_AtBindingsZeroToThree_AreAccepted()
    {
        var (vs, fs) = reflect(ShaderCompiler.GetOrCompileSpirvSource(plain_vert, fragment(
            "layout(set = 1, binding = 0) uniform sampler2D a;\nlayout(set = 1, binding = 3) uniform sampler2D b;\n" +
            "layout(set = 0, binding = 5, std140) uniform GameBlock { vec4 u_Tint; };",
            "texture(a, vec2(0.5)) + texture(b, vec2(0.5)) * u_Tint")));

        var layout = VulkanShaderLayout.Create(vs, fs, "custom");

        Assert.That(layout.UsesTextureArray, Is.False);
        Assert.That(layout.Samplers.Select(s => s.Binding), Is.EqualTo(new uint[] { 0, 3 }));
        Assert.That(layout.UniformBlocks["GameBlock"].Binding, Is.EqualTo(5));
        Assert.That(layout.UniformBlocks["GameBlock"].Size, Is.EqualTo(16));
    }

    [Test]
    public void PushConstants_AreRejected()
        => assertRejected(plain_vert, fragment("layout(push_constant) uniform Push { vec4 u_Colour; };", "u_Colour"), "push constants");

    [Test]
    public void StorageBuffers_AreRejected()
        => assertRejected(plain_vert, fragment("layout(set = 0, binding = 5, std430) readonly buffer Lights { vec4 u_Lights[]; };", "u_Lights[0]"), "'Lights'", "storage buffer");

    [Test]
    public void UniformBlock_OutsideSetZero_IsRejected()
        => assertRejected(plain_vert, fragment("layout(set = 1, binding = 5, std140) uniform GameBlock { vec4 u_Tint; };", "u_Tint"), "'GameBlock'", "set 1, binding 5");

    [Test]
    public void UniformBlock_PastBindingSeven_IsRejected()
        => assertRejected(plain_vert, fragment("layout(set = 0, binding = 8, std140) uniform GameBlock { vec4 u_Tint; };", "u_Tint"), "'GameBlock'", "binding 8");

    [Test]
    public void SingleTexture_PastBindingThree_IsRejected()
        => assertRejected(plain_vert, fragment("layout(set = 1, binding = 4) uniform sampler2D u_Extra;", "texture(u_Extra, vec2(0.5))"), "'u_Extra'", "binding 4");

    [Test]
    public void TextureArray_NextToASingleTexture_IsRejected()
        => assertRejected(plain_vert, fragment(
            "layout(set = 1, binding = 0) uniform sampler2D u_Textures[16];\nlayout(set = 1, binding = 1) uniform sampler2D u_Extra;",
            "texture(u_Textures[0], vec2(0.5)) + texture(u_Extra, vec2(0.5))"), "texture array");

    [Test]
    public void SeparateImage_IsRejected()
        => assertRejected(plain_vert, fragment(
            "layout(set = 1, binding = 0) uniform texture2D u_Image;\nlayout(set = 1, binding = 1) uniform sampler u_Sampler;",
            "texture(sampler2D(u_Image, u_Sampler), vec2(0.5))"), "'u_Image'", "'u_Sampler'");

    [Test]
    public void TwoBlocksSharingABinding_AreRejected()
    {
        const string vert =
            "#version 450\n" +
            "layout(location = 0) in vec2 aPosition;\n" +
            "layout(set = 0, binding = 5, std140) uniform VertexBlock { mat4 u_Transform; };\n" +
            "void main() { gl_Position = u_Transform * vec4(aPosition, 0.0, 1.0); }\n";

        assertRejected(vert, fragment("layout(set = 0, binding = 5, std140) uniform FragmentBlock { vec4 u_Tint; };", "u_Tint"),
            "'VertexBlock'", "'FragmentBlock'", "binding 5");
    }

    [Test]
    public void VertexInput_WiderThanItsAttribute_IsRejected()
    {
        const string vert =
            "#version 450\n" +
            "layout(location = 0) in vec4 aPosition;\n" +
            "void main() { gl_Position = aPosition; }\n";

        assertRejected(vert, fragment(string.Empty, "vec4(1.0)"), "'aPosition'", "location 0", "Position");
    }

    [Test]
    public void VertexInput_PastTheVertexFormat_IsRejected()
    {
        const string vert =
            "#version 450\n" +
            "layout(location = 7) in vec2 aExtra;\n" +
            "void main() { gl_Position = vec4(aExtra, 0.0, 1.0); }\n";

        assertRejected(vert, fragment(string.Empty, "vec4(1.0)"), "'aExtra'", "location 7");
    }

    #endregion

    #region Parser

    [Test]
    public void Parse_RejectsWrongMagicNumber()
    {
        byte[] bytes = new byte[20];
        Assert.Throws<FormatException>(() => SpirvReflection.Parse(bytes));
    }

    [Test]
    public void Parse_RejectsTruncatedModule()
    {
        byte[] vs = ShaderCompiler.GetOrCompileSpirvSource(plain_vert, fragment(string.Empty, "vec4(1.0)")).Vertex;

        // Not a whole number of words.
        Assert.Throws<FormatException>(() => SpirvReflection.Parse(vs.AsSpan(0, vs.Length - 2)));

        // The header plus only the first word of the first instruction (OpCapability, two words long).
        Assert.Throws<FormatException>(() => SpirvReflection.Parse(vs.AsSpan(0, 24)));
    }

    [Test]
    public void Parse_ReportsStageAndEntryPoint()
    {
        var pair = ShaderCompiler.GetOrCompileSpirvSource(plain_vert, fragment(string.Empty, "vec4(1.0)"));

        var vertex = SpirvReflection.Parse(pair.VertexForReflection);
        var fragmentModule = SpirvReflection.Parse(pair.Fragment);

        Assert.That(vertex.Stage, Is.EqualTo(SpirvStage.Vertex));
        Assert.That(fragmentModule.Stage, Is.EqualTo(SpirvStage.Fragment));
        Assert.That(vertex.EntryPoint, Is.EqualTo("main"));
        Assert.That(vertex.UniformBlocks.Single().Size, Is.EqualTo(64));
    }

    #endregion
}
