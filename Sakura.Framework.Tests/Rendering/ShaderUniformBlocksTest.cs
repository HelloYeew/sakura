// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.IO;
using Sakura.Framework.Platform;
using Sakura.Framework.SPIRV;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// <see cref="ShaderUniformBlocks"/>: finding where each uniform block lands in HLSL and MSL, whose source doesn't keep a
/// block's name. It's compared against what the cross-compiler really wrote, block by block, because a wrong number silently
/// uploads to the wrong place.
/// </summary>
[TestFixture]
public class ShaderUniformBlocksTest
{
    private static readonly Storage framework_shaders = new EmbeddedResourceStorage(typeof(App).Assembly, "Sakura.Framework.Resources.Shaders");

    private static readonly CrossCompileTarget[] cross_compiled = { CrossCompileTarget.HLSL, CrossCompileTarget.MSL };

    private const string vertex_header = @"#version 450
layout(location = 0) in vec2 aPosition;
layout(location = 0) out vec4 v_Color;
";

    private const string projection_block = @"
layout(set = 0, binding = 0, std140) uniform ProjectionBlock { mat4 u_Projection; };
";

    private const string fragment_header = @"#version 450
layout(location = 0) in vec4 v_Color;
layout(location = 0) out vec4 FragColor;
";

    private static string plainVertex => vertex_header + projection_block + "void main() { gl_Position = u_Projection * vec4(aPosition, 0.0, 1.0); v_Color = vec4(1.0); }";

    private static ShaderUniformBlock[] blocksOf(string fragment, string? vertex = null, CrossCompileTarget target = CrossCompileTarget.MSL) =>
        ShaderCompiler.GetOrCompileSourceWithBlocks(vertex ?? plainVertex, fragment, target).blocks;

    private static ShaderUniformBlock find(ShaderUniformBlock[] blocks, string name) => blocks.Single(block => block.Name == name);

    /// <summary>
    /// How many members each uniform block has in cross-compiled fragment source, by the buffer index or register it's bound at:
    /// with names gone, the only way to tell one block from another in what the compiler really wrote.
    /// </summary>
    private static Dictionary<int, int> membersByIndex(string fragment, CrossCompileTarget target)
    {
        var found = new Dictionary<int, int>();

        if (target == CrossCompileTarget.MSL)
        {
            foreach (Match parameter in Regex.Matches(fragment, @"constant (\w+)& \w+ \[\[buffer\((\d+)\)\]\]"))
            {
                Match structure = Regex.Match(fragment, $@"struct {parameter.Groups[1].Value}\s*\{{([^}}]*)\}}");
                found[int.Parse(parameter.Groups[2].Value)] = structure.Groups[1].Value.Count(c => c == ';');
            }
        }
        else
        {
            foreach (Match buffer in Regex.Matches(fragment, @"cbuffer \w+ : register\(b(\d+)\)\s*\{([^}]*)\}"))
                found[int.Parse(buffer.Groups[1].Value)] = buffer.Groups[2].Value.Count(c => c == ';');
        }

        return found;
    }

    /// <summary>
    /// What the fixed tables in the Metal and Direct3D 11 renderers said before these were found by reading the shader.
    /// </summary>
    [TestCase("shader.vert", "blur.frag", "BlurBlock")]
    [TestCase("shader.vert", "grayscale.frag", "GrayscaleBlock")]
    [TestCase("video.vert", "video.frag", "VideoBlock")]
    [TestCase("video.vert", "video_nv12.frag", "VideoBlock")]
    public void TheFrameworksShadersLandWhereTheyAlwaysDid(string vertex, string fragment, string block)
    {
        foreach (CrossCompileTarget target in cross_compiled)
        {
            ShaderUniformBlock[] blocks = ShaderCompiler.GetOrCompileWithBlocks(framework_shaders, vertex, fragment, target).blocks;

            Assert.That(blocks, Is.EqualTo(new[]
            {
                new ShaderUniformBlock("ProjectionBlock", true, false, 0),
                new ShaderUniformBlock(block, false, true, 1)
            }), target.ToString());
        }
    }

    /// <summary>
    /// A game's blocks, declared out of order with sizes telling them apart, against the indices and members the cross-compiler
    /// really wrote. Each stage numbers from the same counter, so the first fragment block is 1.
    /// </summary>
    [Test]
    public void EachBlockIsWhereTheCompilerPutIt([ValueSource(nameof(cross_compiled))] CrossCompileTarget target)
    {
        string fragment = fragment_header + @"
layout(set = 0, binding = 7, std140) uniform ThreeMembers { vec4 u_A; float u_B; vec4 u_C; };
layout(set = 0, binding = 5, std140) uniform OneMember { vec4 u_D; };
layout(set = 0, binding = 6, std140) uniform TwoMembers { vec4 u_E; vec4 u_F; };
void main() { FragColor = u_A * u_B + u_C + u_D + u_E + u_F + v_Color; }";

        var (_, frag, blocks) = ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, target);
        var written = membersByIndex(frag, target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(written[find(blocks, "OneMember").Index], Is.EqualTo(1));
            Assert.That(written[find(blocks, "TwoMembers").Index], Is.EqualTo(2));
            Assert.That(written[find(blocks, "ThreeMembers").Index], Is.EqualTo(3));
            Assert.That(find(blocks, "OneMember").Index, Is.EqualTo(1));
            Assert.That(find(blocks, "TwoMembers").Index, Is.EqualTo(2));
            Assert.That(find(blocks, "ThreeMembers").Index, Is.EqualTo(3));
        }
    }

    /// <summary>
    /// A block nothing reads doesn't survive compiling, and takes no number: every later one moves down. It's what counting the
    /// source's declarations would get wrong.
    /// </summary>
    [Test]
    public void ABlockNothingReadsTakesNoNumber([ValueSource(nameof(cross_compiled))] CrossCompileTarget target)
    {
        string fragment = fragment_header + @"
layout(set = 0, binding = 5, std140) uniform OneMember { vec4 u_A; };
layout(set = 0, binding = 6, std140) uniform Unread { vec4 u_Unread; };
layout(set = 0, binding = 7, std140) uniform TwoMembers { vec4 u_B; vec4 u_C; };
void main() { FragColor = u_A + u_B + u_C + v_Color; }";

        var (_, frag, blocks) = ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, target);
        var written = membersByIndex(frag, target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(blocks.Select(block => block.Name), Does.Not.Contain("Unread"));
            Assert.That(written[find(blocks, "TwoMembers").Index], Is.EqualTo(2));
            Assert.That(find(blocks, "TwoMembers").Index, Is.EqualTo(2));
        }
    }

    /// <summary>
    /// Set before binding: a later set's block comes after an earlier set's, whatever its binding.
    /// </summary>
    [Test]
    public void AnEarlierSetComesFirst([ValueSource(nameof(cross_compiled))] CrossCompileTarget target)
    {
        string fragment = fragment_header + @"
layout(set = 1, binding = 0, std140) uniform TwoMembers { vec4 u_A; vec4 u_B; };
layout(set = 0, binding = 7, std140) uniform OneMember { vec4 u_C; };
void main() { FragColor = u_A + u_B + u_C + v_Color; }";

        var (_, frag, blocks) = ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, target);
        var written = membersByIndex(frag, target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(find(blocks, "OneMember").Index, Is.EqualTo(1));
            Assert.That(find(blocks, "TwoMembers").Index, Is.EqualTo(2));
            Assert.That(written[1], Is.EqualTo(1));
            Assert.That(written[2], Is.EqualTo(2));
        }
    }

    [Test]
    public void ABlockInBothStagesHasOneNumber()
    {
        string vertex = vertex_header + projection_block + @"
layout(set = 0, binding = 5, std140) uniform Shared { vec4 u_Shared; };
void main() { gl_Position = u_Projection * vec4(aPosition, 0.0, 1.0); v_Color = u_Shared; }";

        string fragment = fragment_header + @"
layout(set = 0, binding = 5, std140) uniform Shared { vec4 u_Shared; };
layout(set = 0, binding = 6, std140) uniform JustHere { vec4 u_JustHere; };
void main() { FragColor = u_Shared + u_JustHere + v_Color; }";

        ShaderUniformBlock[] blocks = blocksOf(fragment, vertex);

        Assert.That(blocks, Is.EqualTo(new[]
        {
            new ShaderUniformBlock("ProjectionBlock", true, false, 0),
            new ShaderUniformBlock("Shared", true, true, 1),
            new ShaderUniformBlock("JustHere", false, true, 2)
        }));
    }

    /// <summary>
    /// A block the vertex stage has and the fragment stage doesn't is still counted, and both stages' numbers come from the one
    /// counter: what gives a fragment shader's first block 1 rather than 0.
    /// </summary>
    [Test]
    public void AVertexBlockTakesANumberFromTheFragmentsToo([ValueSource(nameof(cross_compiled))] CrossCompileTarget target)
    {
        string vertex = vertex_header + projection_block + @"
layout(set = 0, binding = 6, std140) uniform VertexOnly { vec4 u_VertexOnly; };
void main() { gl_Position = u_Projection * vec4(aPosition, 0.0, 1.0); v_Color = u_VertexOnly; }";

        string fragment = fragment_header + @"
layout(set = 0, binding = 5, std140) uniform Before { vec4 u_Before; };
layout(set = 0, binding = 7, std140) uniform After { vec4 u_After; vec4 u_Second; };
void main() { FragColor = u_Before + u_After + u_Second + v_Color; }";

        var (_, frag, blocks) = ShaderCompiler.GetOrCompileSourceWithBlocks(vertex, fragment, target);
        var written = membersByIndex(frag, target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(find(blocks, "Before").Index, Is.EqualTo(1));
            Assert.That(find(blocks, "VertexOnly").Index, Is.EqualTo(2));
            Assert.That(find(blocks, "After").Index, Is.EqualTo(3));
            Assert.That(written[3], Is.EqualTo(2), "After, past the vertex stage's block");
        }
    }

    [TestCase("layout(std140, binding = 5, set = 0) uniform Named { vec4 u_A; };", TestName = "Its qualifiers in another order")]
    [TestCase("layout(\n    set = 0,\n    binding = 5,\n    std140\n)\nuniform Named\n{\n    vec4 u_A;\n};", TestName = "Over several lines")]
    [TestCase("layout(set = 0, binding = 5, std140) uniform Named { vec4 u_A; } instance;\n#define u_A instance.u_A", TestName = "With an instance name")]
    [TestCase("layout(binding = 5, std140) uniform Named { vec4 u_A; };", TestName = "With no set, which is set 0")]
    [TestCase("// layout(set = 0, binding = 5, std140) uniform OldName { vec4 u_A; };\nlayout(set = 0, binding = 5, std140) uniform Named { vec4 u_A; };", TestName = "With an old one commented out before it")]
    [TestCase("layout(set = 0, binding = 5, std140) uniform Named { vec4 u_A; };\n/* layout(set = 0, binding = 5, std140)\n uniform OldName { vec4 u_A; }; */", TestName = "With an old one in a block comment after it")]
    public void ItsNameIsReadHoweverItsWritten(string declaration)
    {
        string fragment = fragment_header + declaration + "\nvoid main() { FragColor = u_A + v_Color; }";

        Assert.That(find(blocksOf(fragment), "Named").Index, Is.EqualTo(1));
    }

    /// <summary>
    /// Better a shader that fails to build, by name, than one whose block silently never uploads.
    /// </summary>
    [Test]
    public void ADeclarationThatCantBeReadIsAnError()
    {
        string fragment = fragment_header + @"
#define WHERE 5
layout(set = 0, binding = WHERE, std140) uniform Hidden { vec4 u_A; };
void main() { FragColor = u_A + v_Color; }";

        var error = Assert.Throws<InvalidOperationException>(() => blocksOf(fragment));

        Assert.That(error!.Message, Does.Contain("binding 5"));
    }

    [Test]
    public void GlslKeepsItsNamesAndHasNoNeedOfThis() =>
        Assert.Throws<ArgumentException>(() => ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment_header + "void main() { FragColor = v_Color; }", CrossCompileTarget.GLSL));

    /// <summary>
    /// It compiles the modules itself to read them, then hands them over, rather than giving the cross-compiler source to compile
    /// a second time. That has to come out the same, or a shader would change depending on which way it was asked for.
    /// </summary>
    [TestCase("shader.vert", "shader.frag")]
    [TestCase("shader.vert", "blur.frag")]
    [TestCase("shader.vert", "grayscale.frag")]
    [TestCase("video.vert", "video.frag")]
    [TestCase("video.vert", "video_nv12.frag")]
    public void TheShaderItselfComesOutTheSame(string vertex, string fragment)
    {
        foreach (CrossCompileTarget target in cross_compiled)
        {
            var plain = ShaderCompiler.GetOrCompile(framework_shaders, vertex, fragment, target);
            var withBlocks = ShaderCompiler.GetOrCompileWithBlocks(framework_shaders, vertex, fragment, target);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(withBlocks.vert, Is.EqualTo(plain.vert), $"{target} vertex");
                Assert.That(withBlocks.frag, Is.EqualTo(plain.frag), $"{target} fragment");
            }
        }
    }

    [Test]
    public void ItsBlocksSurviveBeingWrittenOut()
    {
        ShaderUniformBlock[] blocks =
        {
            new ShaderUniformBlock("ProjectionBlock", true, false, 0),
            new ShaderUniformBlock("Shared", true, true, 1),
            new ShaderUniformBlock("JustHere", false, true, 2)
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ShaderUniformBlocks.TryDeserialize(ShaderUniformBlocks.Serialize(blocks), out ShaderUniformBlock[] read), Is.True);
            Assert.That(read, Is.EqualTo(blocks));
        }
    }

    [Test]
    public void NoBlocksIsFineToo()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ShaderUniformBlocks.TryDeserialize(ShaderUniformBlocks.Serialize(Array.Empty<ShaderUniformBlock>()), out ShaderUniformBlock[] read), Is.True);
            Assert.That(read, Is.Empty);
        }
    }

    [TestCase("a line that isn't one")]
    [TestCase("Name 1 0 notanumber\n")]
    public void DamagedBlocksArentBelieved(string text) => Assert.That(ShaderUniformBlocks.TryDeserialize(text, out _), Is.False);

    [TestFixture]
    public class WithACache
    {
        private const string fragment = fragment_header + @"
layout(set = 0, binding = 5, std140) uniform Block { vec4 u_A; };
void main() { FragColor = u_A + v_Color; }";

        private string directory = null!;
        private DiskCache cache = null!;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "sakura-uniform-blocks-test", Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            cache = new DiskCache(new NativeStorage(directory));
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, true);

        private string[] entries => Directory.GetFiles(directory);

        [Test]
        public void ASecondCallReadsTheSameBack()
        {
            var first = ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, CrossCompileTarget.MSL, cache);
            string[] written = entries;
            var second = ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, CrossCompileTarget.MSL, cache);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(written, Has.Length.EqualTo(2), "the shader, and where its blocks are");
                Assert.That(entries, Is.EquivalentTo(written));
                Assert.That(second.vert, Is.EqualTo(first.vert));
                Assert.That(second.frag, Is.EqualTo(first.frag));
                Assert.That(second.blocks, Is.EqualTo(first.blocks));
            }
        }

        /// <summary>
        /// A second call really does read them, not compile them again: a change to what's in the cache comes back.
        /// </summary>
        [Test]
        public void ASecondCallDoesntCompile()
        {
            string key = keyOfBlocks();
            ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, CrossCompileTarget.MSL, cache);
            cache.WriteStrings(key, "FromTheCache 0 1 9\n", string.Empty);

            var second = ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, CrossCompileTarget.MSL, cache);

            Assert.That(second.blocks, Is.EqualTo(new[] { new ShaderUniformBlock("FromTheCache", false, true, 9) }));
        }

        /// <summary>
        /// A cache written by an older version has the shader but not where its blocks are, and mustn't leave a shader with none.
        /// </summary>
        [Test]
        public void AShaderCachedWithoutItsBlocksFindsThem()
        {
            ShaderCompiler.GetOrCompileSource(plainVertex, fragment, CrossCompileTarget.MSL, cache);
            Assert.That(entries, Has.Length.EqualTo(1));

            var withBlocks = ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, CrossCompileTarget.MSL, cache);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(find(withBlocks.blocks, "Block").Index, Is.EqualTo(1));
                Assert.That(entries, Has.Length.EqualTo(2));
            }
        }

        [Test]
        public void ADamagedEntryIsCompiledAgain()
        {
            string key = keyOfBlocks();
            ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, CrossCompileTarget.MSL, cache);
            cache.WriteStrings(key, "damaged", string.Empty);

            var again = ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, CrossCompileTarget.MSL, cache);

            Assert.That(find(again.blocks, "Block").Index, Is.EqualTo(1));
        }

        /// <summary>
        /// HLSL and MSL number the same way today, but nothing says they always will, so they don't share an entry.
        /// </summary>
        [Test]
        public void EachTargetHasItsOwnEntries()
        {
            ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, CrossCompileTarget.MSL, cache);
            ShaderCompiler.GetOrCompileSourceWithBlocks(plainVertex, fragment, CrossCompileTarget.HLSL, cache);

            Assert.That(entries, Has.Length.EqualTo(4));
        }

        /// <summary>
        /// The key <see cref="ShaderCompiler"/> keeps blocks under, for a test to write there.
        /// </summary>
        private static string keyOfBlocks() =>
            $"blocks#{DiskCache.HashString(plainVertex)}#{DiskCache.HashString(fragment)}#{(int)CrossCompileTarget.MSL}#1";
    }
}
