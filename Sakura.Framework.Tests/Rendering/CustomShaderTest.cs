// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Uniforms;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.SPIRV;
using Sakura.Framework.Statistic;
using Sakura.Framework.Testing;
using Sakura.Framework.Timing;
using VertexData = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// <see cref="CustomShaderStore"/>, <see cref="CustomShaderDrawNode"/> and <see cref="ShaderChecks"/>, without a GPU: what's asked
/// of a renderer, in what order. <see cref="Graphics.CustomShaderPixelTest"/> has what comes of it.
/// </summary>
[TestFixture]
public class CustomShaderTest
{
    private static readonly Storage test_shaders = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources.Shaders");
    private static readonly Storage framework_shaders = new EmbeddedResourceStorage(typeof(App).Assembly, "Sakura.Framework.Resources.Shaders");

    private ManualClock manual = null!;
    private Container root = null!;
    private CustomShaderStore store = null!;
    private RecordingRenderer renderer = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container { Size = new Vector2(100), Clock = new FramedClock(manual) };
        root.Load();
        root.CompleteLoad();

        store = new CustomShaderStore(test_shaders);
        renderer = new RecordingRenderer();
    }

    private Box box(BlendingMode blending = BlendingMode.Alpha, float alpha = 1)
    {
        var drawable = new Box { Size = new Vector2(50), Blending = blending, Alpha = alpha };
        root.Add(drawable);
        manual.CurrentTime += 16;
        root.UpdateSubTree();
        return drawable;
    }

    #region The store

    [Test]
    public void AShaderIsBuiltOnce()
    {
        IShader? first = store.Get(renderer, "test-solid.frag");
        IShader? second = store.Get(renderer, "test-solid.frag");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first));
            Assert.That(renderer.Created, Is.EqualTo(new[] { "shader.vert+test-solid.frag" }));
        });
    }

    [Test]
    public void EachPairIsItsOwnShader()
    {
        IShader? solid = store.Get(renderer, "test-solid.frag");
        IShader? ripple = store.Get(renderer, "test-ripple.frag");
        IShader? ownVertex = store.Get(renderer, "test-solid.frag", "test-both-stages.vert");

        Assert.Multiple(() =>
        {
            Assert.That(ripple, Is.Not.SameAs(solid));
            Assert.That(ownVertex, Is.Not.SameAs(solid));
            Assert.That(renderer.Created, Is.EqualTo(new[] { "shader.vert+test-solid.frag", "shader.vert+test-ripple.frag", "test-both-stages.vert+test-solid.frag" }));
        });
    }

    /// <summary>
    /// The backend switched: the old renderer's shaders are no use on the new one.
    /// </summary>
    [Test]
    public void ANewRendererGetsItsOwn()
    {
        var other = new RecordingRenderer();

        IShader? first = store.Get(renderer, "test-solid.frag");
        IShader? onTheOther = store.Get(other, "test-solid.frag");

        Assert.Multiple(() =>
        {
            Assert.That(onTheOther, Is.Not.SameAs(first));
            Assert.That(other.Created, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// One bad shader mustn't break the frame, or try to build again every frame it's drawn.
    /// </summary>
    [Test]
    public void AShaderThatWontBuildIsNullAndAskedForOnce()
    {
        renderer.FailToBuild = true;

        Assert.Multiple(() =>
        {
            Assert.That(store.Get(renderer, "test-solid.frag"), Is.Null);
            Assert.That(store.Get(renderer, "test-solid.frag"), Is.Null);
            Assert.That(renderer.Created, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ItsSourcesAreTheGamesWithTheFrameworksBehind()
    {
        Storage sources = store.Sources(framework_shaders);

        string fragment = ShaderCompiler.ReadWithIncludes(sources, "test-solid.frag");

        Assert.Multiple(() =>
        {
            Assert.That(fragment, Does.Contain("u_Color"), "the game's own");
            Assert.That(fragment, Does.Contain("writeFragment"), "the framework's it includes");
            Assert.That(fragment, Does.Contain("sdRoundParallelogram"), "and what that includes");
            Assert.That(sources.Exists("shader.vert"), Is.True, "and the vertex shader it pairs with");
        });
    }

    /// <summary>
    /// A game can replace a framework file it includes, by having its own of the same name.
    /// </summary>
    [Test]
    public void AGamesFileComesBeforeTheFrameworks()
    {
        using var game = new TemporaryStorage("sakura-custom-shader-test");
        File.WriteAllText(game.GetFullPath("sh_Utils.glsl"), "// the game's own");

        string read = ShaderCompiler.ReadWithIncludes(new CustomShaderStore(game).Sources(framework_shaders), "sh_Utils.glsl");

        Assert.That(read, Does.Contain("the game's own"));
    }

    #endregion

    #region The draw node

    [Test]
    public void ItDrawsInTheOrderARendererNeeds()
    {
        var node = new CustomShaderDrawNode<SolidBlock>(store, "test-solid.frag", "SolidBlock", () => new SolidBlock { Color = new Vector4(1) });
        node.ApplyState(box(BlendingMode.Additive));

        node.Draw(renderer);

        Assert.That(renderer.Calls, Is.EqualTo(new[]
        {
            "blend Additive",
            "flush",
            "use",
            "block ProjectionBlock",
            "block SolidBlock",
            "clip",
            "draw 4",
            "restore"
        }));
    }

    /// <summary>
    /// It draws in a way of its own, but it's still something drawn this frame.
    /// </summary>
    [Test]
    public void ItCountsAsDrawn()
    {
        var drawn = GlobalStatistics.Get<int>("Drawables", "Drawn Last Frame", StatisticKind.PerFrame);
        var node = new CustomShaderDrawNode(store, "test-solid.frag");
        node.ApplyState(box());

        int before = drawn.Accumulator;
        node.Draw(renderer);

        Assert.That(drawn.Accumulator, Is.EqualTo(before + 1));
    }

    [Test]
    public void ItGetsTheRenderersProjection()
    {
        renderer.Projection = Matrix4x4.CreateScale(2, 3, 4);
        var node = new CustomShaderDrawNode(store, "test-solid.frag");
        node.ApplyState(box());

        node.Draw(renderer);

        Assert.That(renderer.Shader!.Value<ProjectionBlock>("ProjectionBlock").Projection, Is.EqualTo(renderer.Projection));
    }

    /// <summary>
    /// It stays inside a mask by the clip the renderer writes on what's drawn, so that has to be what's handed over.
    /// </summary>
    [Test]
    public void WhatsDrawnHasItsClipApplied()
    {
        var node = new CustomShaderDrawNode(store, "test-solid.frag");
        node.ApplyState(box());

        node.Draw(renderer);

        Assert.That(renderer.DrawnClipRadius, Is.EqualTo(RecordingRenderer.CLIP_RADIUS));
    }

    /// <summary>
    /// The block is copied with the rest of a node's state, on the update thread: the drawable may have moved on by the time the
    /// draw thread gets to it, and reading it then would tear.
    /// </summary>
    [Test]
    public void ABlockIsReadWhenStateIsAppliedNotWhenDrawn()
    {
        float value = 0.25f;
        var node = new CustomShaderDrawNode<SolidBlock>(store, "test-solid.frag", "SolidBlock", () => new SolidBlock { Color = new Vector4(value) });
        node.ApplyState(box());

        value = 0.75f;
        node.Draw(renderer);

        Assert.That(renderer.Shader!.Value<SolidBlock>("SolidBlock").Color.X, Is.EqualTo(0.25f));

        node.ApplyState(root.Children[0]);
        node.Draw(renderer);

        Assert.That(renderer.Shader!.Value<SolidBlock>("SolidBlock").Color.X, Is.EqualTo(0.75f));
    }

    private static IEnumerable<TestCaseData> nothingToDraw()
    {
        yield return new TestCaseData(0f, false, false).SetName("Faded right out");
        yield return new TestCaseData(1f, true, false).SetName("With no vertices");
        yield return new TestCaseData(1f, false, true).SetName("With a shader that won't build");
    }

    /// <summary>
    /// Not just nothing drawn: nothing touched, since the batch would be flushed and the main shader rebound for nothing.
    /// </summary>
    [TestCaseSource(nameof(nothingToDraw))]
    public void WithNothingToDrawItLeavesTheRendererAlone(float alpha, bool noVertices, bool broken)
    {
        renderer.FailToBuild = broken;

        Box drawable = box(alpha: alpha);
        var node = new EmptiedNode(store, noVertices);
        node.ApplyState(drawable);

        node.Draw(renderer);

        Assert.That(renderer.Calls, Is.Empty);
    }

    /// <summary>
    /// Headless draws nothing with it, rather than failing: a drawable using one can be in any test.
    /// </summary>
    [Test]
    public void ARendererThatCantTakeACustomShaderIsLeftAlone()
    {
        var headless = new HeadlessRenderer(new HeadlessTextureManager());
        var node = new CustomShaderDrawNode(store, "test-solid.frag");
        node.ApplyState(box());

        Assert.DoesNotThrow(() => node.Draw(headless));
    }

    [Test]
    public void ABlockThatStopsShortOf16BytesIsTurnedAway()
    {
        var error = Assert.Throws<ArgumentException>(() => _ = new CustomShaderDrawNode<ShortBlock>(store, "test-solid.frag", "ShortBlock", () => default));

        Assert.That(error!.Message, Does.Contain("20 bytes"));
    }

    #endregion

    #region Checking a game's shaders

    [TestCase("test-solid.frag", TestName = "One that reads nothing but its block")]
    [TestCase("test-two-blocks.frag", TestName = "One with several blocks")]
    [TestCase("test-ripple.frag", TestName = "One that reads where it is")]
    [TestCase("test-stops-early.frag", TestName = "One that reads its first inputs and no more")]
    public void AGoodShaderHasNoProblems(string fragment) => Assert.That(ShaderChecks.Problems(test_shaders, fragment), Is.Empty);

    [Test]
    public void ItsOwnVertexShaderCountsToo() => Assert.That(ShaderChecks.Problems(test_shaders, "test-both-stages.frag", "test-both-stages.vert"), Is.Empty);

    /// <summary>
    /// And it's that one that's built for each backend, rather than the framework's.
    /// </summary>
    [Test]
    public void ABrokenVertexShaderIsReportedForEachBackend() => Assert.That(ShaderChecks.Problems(test_shaders, "test-solid.frag", "test-broken.vert"), Has.Count.EqualTo(4));

    /// <summary>
    /// Compiling fails the same everywhere, and each backend's failure is named.
    /// </summary>
    [Test]
    public void AShaderThatWontCompileIsReportedForEachBackend()
    {
        var problems = ShaderChecks.Problems(test_shaders, "test-broken.frag");

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Count.EqualTo(4));
            Assert.That(problems, Has.Some.StartWith("GLSL:"));
            Assert.That(problems, Has.Some.StartWith("HLSL:"));
            Assert.That(problems, Has.Some.StartWith("MSL:"));
            Assert.That(problems, Has.Some.StartWith("Vulkan:"));
        });
    }

    /// <summary>
    /// What a machine without Direct3D has no other way to see: it compiles everywhere, then draws wrong on one backend only.
    /// </summary>
    [Test]
    public void SkippingAnInputIsReportedByLocation()
    {
        var problems = ShaderChecks.Problems(test_shaders, "test-skips-input.frag");

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.StartWith("Direct3D:"));
            Assert.That(problems[0], Does.Contain("location 1"));
        });
    }

    /// <summary>
    /// The same: fine everywhere but on Vulkan.
    /// </summary>
    [Test]
    public void ABlockVulkanHasNoPlaceForIsReported()
    {
        var problems = ShaderChecks.Problems(test_shaders, "test-bad-binding.frag");

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.StartWith("Vulkan:"));
            Assert.That(problems[0], Does.Contain("binding 9"));
        });
    }

    /// <summary>
    /// <c>sh_Custom.glsl</c> is what takes care of that rule, and only reading them all does: its reads have to survive the optimiser.
    /// </summary>
    [Test]
    public void TheFrameworksStartingPointKeepsEveryInput()
    {
        var (_, fragment) = ShaderCompiler.GetOrCompile(store.Sources(framework_shaders), "shader.vert", "test-solid.frag", CrossCompileTarget.HLSL);

        Assert.Multiple(() =>
        {
            for (int input = 0; input < 9; input++)
                Assert.That(fragment, Does.Contain($": TEXCOORD{input};"));
        });
    }

    #endregion

    private struct SolidBlock
    {
        public Vector4 Color;
    }

    private struct ShortBlock
    {
#pragma warning disable CS0649
        public Vector4 Color;
        public float Time;
#pragma warning restore CS0649
    }

    /// <summary>
    /// A node that can have no vertices, whatever it's applied from.
    /// </summary>
    private class EmptiedNode : CustomShaderDrawNode
    {
        private readonly bool empty;

        public EmptiedNode(CustomShaderStore shaders, bool empty)
            : base(shaders, "test-solid.frag")
        {
            this.empty = empty;
        }

        protected override void ApplyVertices(Drawable source)
        {
            if (empty)
                SetVertexCount(0);
            else
                base.ApplyVertices(source);
        }
    }

    /// <summary>
    /// A renderer that says what's asked of it and builds nothing.
    /// </summary>
    private sealed class RecordingRenderer : HeadlessRenderer, ICustomShaderRenderer
    {
        public const float CLIP_RADIUS = 7;

        public readonly List<string> Calls = new List<string>();
        public readonly List<string> Created = new List<string>();

        public Matrix4x4 Projection = Matrix4x4.Identity;
        public bool FailToBuild;
        public RecordingShader? Shader;
        public float DrawnClipRadius;

        public RecordingRenderer()
            : base(new HeadlessTextureManager())
        {
        }

        Matrix4x4 IRenderer.ProjectionMatrix => Projection;

        Storage IRenderer.ShaderStorage { get; set; } = framework_shaders;

        void IRenderer.SetBlendMode(BlendingMode blendingMode) => Calls.Add($"blend {blendingMode}");

        void IRenderer.FlushBatch() => Calls.Add("flush");

        void IRenderer.RestoreMainShader() => Calls.Add("restore");

        void IRenderer.ApplyCurrentClip(Span<VertexData> vertices)
        {
            Calls.Add("clip");

            for (int i = 0; i < vertices.Length; i++)
                vertices[i].ClipRadius = CLIP_RADIUS;
        }

        IShader IRenderer.CreateShader(Storage storage, string vertexPath, string fragmentPath)
        {
            Created.Add($"{vertexPath}+{fragmentPath}");

            if (FailToBuild)
                throw new InvalidOperationException("It won't build.");

            return Shader = new RecordingShader(Calls);
        }

        public void DrawVerticesRaw(ReadOnlySpan<VertexData> vertices)
        {
            Calls.Add($"draw {vertices.Length}");
            DrawnClipRadius = vertices.IsEmpty ? 0 : vertices[0].ClipRadius;
        }
    }

    private sealed class RecordingShader : IShader
    {
        private readonly List<string> calls;
        private readonly Dictionary<string, object> blocks = new Dictionary<string, object>();

        public RecordingShader(List<string> calls)
        {
            this.calls = calls;
        }

        public T Value<T>(string blockName) where T : unmanaged => (T)blocks[blockName];

        public nint Handle => 0;

        public void Use() => calls.Add("use");

        public void SetUniformBlock<T>(string blockName, in T data) where T : unmanaged
        {
            calls.Add($"block {blockName}");
            blocks[blockName] = data;
        }

        public void SetUniform(string name, int value) { }
        public void SetUniform(string name, float value) { }
        public void SetUniform(string name, bool value) { }
        public void SetUniform(string name, Matrix4x4 value) { }
        public void SetUniform(string name, Vector2 value) { }
        public void SetUniform(string name, Vector4 value) { }
        public void SetUniform(string name, Color value) { }
        public void SetUniformIntArray(string name, int[] values) { }
        public void SetUniform(string name, float[] mat3X3) { }
        public void Dispose() { }
    }
}
