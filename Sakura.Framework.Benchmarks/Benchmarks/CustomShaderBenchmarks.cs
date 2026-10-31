// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using VertexData = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// What a game's own shader costs the CPU in a frame: a <see cref="CustomShaderDrawNode"/> with a uniform block taking its state
/// and drawing, on a renderer that does nothing with it (so not what the backend's own calls cost).
/// </summary>
[MemoryDiagnoser]
public class CustomShaderBenchmarks
{
    private static readonly Storage framework_shaders = new EmbeddedResourceStorage(typeof(App).Assembly, "Sakura.Framework.Resources.Shaders");

    private readonly NullRenderer renderer = new NullRenderer();
    private Container root = null!;
    private Box box = null!;
    private CustomShaderDrawNode<Block> node = null!;

    [GlobalSetup]
    public void SetUp()
    {
        root = new Container { Size = new Vector2(100), Clock = new FramedClock(new ManualClock { CurrentTime = 1000 }) };
        root.Load();
        root.CompleteLoad();
        root.Add(box = new Box { Size = new Vector2(50) });
        root.UpdateSubTree();

        node = new CustomShaderDrawNode<Block>(new CustomShaderStore(framework_shaders), "blur.frag", "Block", () => new Block { Color = new Vector4(1), Time = 1, Rings = 2, Alpha = 3 });

        // A node that returned early would make this look very cheap indeed.
        ApplyAndDraw();

        if (renderer.Drawn == 0)
            throw new InvalidOperationException("Nothing was drawn, so nothing would be measured.");
    }

    /// <summary>
    /// One drawable's frame: its state applied, block and all, on the update thread, and then drawn.
    /// </summary>
    [Benchmark]
    public void ApplyAndDraw()
    {
        node.ApplyState(box);
        node.Draw(renderer);
    }

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private struct Block
    {
        public Vector4 Color;
        public float Time;
        public float Rings;
        public float Alpha;
    }

    private sealed class NullRenderer : HeadlessRenderer, ICustomShaderRenderer
    {
        public int Drawn;

        public NullRenderer()
            : base(new HeadlessTextureManager())
        {
            ShaderStorage = framework_shaders;
        }

        public void DrawVerticesRaw(ReadOnlySpan<VertexData> vertices) => Drawn += vertices.Length;
    }
}
