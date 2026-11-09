// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// The draw thread's half of a frame. One op walks a frame's draw nodes into the batch, as the GPU backends do, and throws
/// each batch away where a backend would upload and draw it (<see cref="BatchingRenderer"/>). The draw nodes are made once,
/// in setup, so none of the update thread's work is in it: only the walk, the vertex copies, the clip and texture-slot
/// stamping, and the flushes.
/// </summary>
/// <remarks>
/// Each tree's setup writes its draw calls and vertices a frame to the log, so a change to batching shows there too.
/// </remarks>
[MemoryDiagnoser]
public class DrawThreadBenchmarks
{
    private const float width = 1280;
    private const float height = 720;

    /// <summary>
    /// The batch's shape: false is Metal's, 6 vertices a quad; true is Direct3D 11, Vulkan and OpenGL's, 4 vertices and
    /// 6 indices a quad.
    /// </summary>
    [Params(false, true)]
    public bool Indexed;

    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore fontStore = null!;
    private BatchingRenderer renderer = null!;
    private ManualClock clock = null!;

    /// <summary>
    /// Sets up the rhythm lane frame.
    /// </summary>
    [GlobalSetup(Target = nameof(RhythmFrame))]
    public void SetupRhythmFrame() => setup(nameof(RhythmFrame), addRhythmFrame);

    /// <summary>
    /// Sets up 10,000 sprites.
    /// </summary>
    [GlobalSetup(Target = nameof(Sprites_10k))]
    public void SetupSprites10K() => setup(nameof(Sprites_10k), root => addSprites(root, 10_000));

    /// <summary>
    /// Sets up 100,000 sprites.
    /// </summary>
    [GlobalSetup(Target = nameof(Sprites_100k))]
    public void SetupSprites100K() => setup(nameof(Sprites_100k), root => addSprites(root, 100_000));

    /// <summary>
    /// Sets up the list of masked cards.
    /// </summary>
    [GlobalSetup(Target = nameof(MaskedCards))]
    public void SetupMaskedCards() => setup(nameof(MaskedCards), addMaskedCards);

    /// <summary>
    /// Sets up the screen of labels.
    /// </summary>
    [GlobalSetup(Target = nameof(Text))]
    public void SetupText() => setup(nameof(Text), addText);

    /// <summary>
    /// Disposes the fonts and the textures.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        fontStore.Dispose();
        textureManager.Dispose();
    }

    /// <summary>
    /// The cross-framework bench's rhythm lane frame: four lanes of 64 notes, a judgement line, 20 boxes and an 8-digit
    /// score. One op is one frame drawn.
    /// </summary>
    [Benchmark]
    public int RhythmFrame() => drawFrame();

    /// <summary>
    /// 10,000 tinted sprites of one texture in one container, as in the bench's sprite sweep. One op is one frame drawn.
    /// </summary>
    [Benchmark]
    public int Sprites_10k() => drawFrame();

    /// <summary>
    /// 100,000 tinted sprites of one texture in one container, as in the bench's sprite sweep. One op is one frame drawn.
    /// </summary>
    [Benchmark]
    public int Sprites_100k() => drawFrame();

    /// <summary>
    /// A masked list of 14 rounded cards with borders, two of them scrolled out of view, each with a background, a cover
    /// and an accent bar: a song list. Every border is a batch of its own. One op is one frame drawn.
    /// </summary>
    [Benchmark]
    public int MaskedCards() => drawFrame();

    /// <summary>
    /// A screenful of 64 labels of 30 characters. One op is one frame drawn.
    /// </summary>
    [Benchmark]
    public int Text() => drawFrame();

    /// <summary>
    /// Draws the frame's draw nodes as a backend's frame does.
    /// </summary>
    /// <returns>The vertices drawn, so nothing of it can be left out.</returns>
    private int drawFrame()
    {
        renderer.StartFrame();
        renderer.Draw(clock);
        return renderer.VerticesDrawn;
    }

    /// <summary>
    /// Builds a tree with <paramref name="build"/>, settles it, makes its draw nodes once and checks they draw something.
    /// </summary>
    private void setup(string name, Action<Container> build)
    {
        Logger.Initialize();

        textureManager = new HeadlessTextureManager();
        renderer = new BatchingRenderer(textureManager, Indexed);
        fontStore = new RendererFontStore(renderer);
        fontStore.AddFontFamily(new EmbeddedResourceStorage(typeof(App).Assembly, "Sakura.Framework.Resources").GetStorageForDirectory("Fonts"), "NotoSans", hasItalics: true);

        clock = new ManualClock();

        var root = new FontRoot
        {
            Size = new Vector2(width, height),
            Clock = new FramedClock(clock)
        };

        root.Load();
        root.CompleteLoad();
        root.Provide(fontStore);
        build(root);

        for (int i = 0; i < 3; i++)
        {
            clock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
            root.UpdateSubTree();
            root.GenerateDrawNodeSubtree(i % 3);
        }

        renderer.SetRoot(root.GenerateDrawNodeSubtree(0));
        drawFrame();

        if (renderer.VerticesDrawn == 0)
            throw new InvalidOperationException($"{name} drew nothing: its tree didn't load or settle.");

        Console.WriteLine($"// {name} ({(Indexed ? "indexed" : "non-indexed")}): {renderer.DrawCalls} draw calls, {renderer.VerticesDrawn} vertices a frame");
    }

    /// <summary>
    /// The bench's rhythm lane scene, held at one moment.
    /// </summary>
    private static void addRhythmFrame(Container root)
    {
        const int lanes = 4;
        const int notes_per_lane = 64;
        const float spacing = height / notes_per_lane;

        var playfield = new Container
        {
            Position = new Vector2(440, 0),
            Size = new Vector2(400, height)
        };

        for (int i = 0; i < lanes; i++)
        {
            var lane = new Container
            {
                Position = new Vector2(i * 100, 0),
                Size = new Vector2(100, height)
            };

            for (int j = 0; j < notes_per_lane; j++)
            {
                lane.Add(new Box
                {
                    Position = new Vector2(5, j * spacing),
                    Size = new Vector2(90, 30),
                    Color = Color.LightPink
                });
            }

            playfield.Add(lane);
        }

        playfield.Add(new Box
        {
            Position = new Vector2(0, 640),
            Size = new Vector2(400, 4),
            Color = Color.White
        });

        root.Add(playfield);

        for (int i = 0; i < 20; i++)
        {
            root.Add(new Box
            {
                Position = new Vector2(i * 60, 10),
                Size = new Vector2(50, 20),
                Color = Color.Gray
            });
        }

        root.Add(new SpriteText
        {
            Text = "04567281",
            Position = new Vector2(1000, 40),
            Font = FontUsage.Default.With(family: "NotoSans", size: 32f),
            Color = Color.White
        });
    }

    /// <summary>
    /// <paramref name="count"/> 32-pixel sprites of one texture, spread over the screen in a fixed pattern and tinted.
    /// </summary>
    private void addSprites(Container root, int count)
    {
        var texture = textureManager.FromPixelData(32, 32, new byte[32 * 32 * 4]);
        var holder = new Container
        {
            RelativeSizeAxes = Axes.Both
        };

        for (int i = 0; i < count; i++)
        {
            holder.Add(new Sprite
            {
                Texture = texture,
                Size = new Vector2(32),
                Position = new Vector2(i * 37 % (width - 32), i * 91 % (height - 32)),
                Color = Color.FromArgb(255, 128 + i % 128, 255 - i % 96, 160 + i % 64)
            });
        }

        root.Add(holder);
    }

    /// <summary>
    /// A masked list of 14 rounded, bordered cards 68 pixels apart, the first half above the top: twelve are in view and
    /// the last two are culled.
    /// </summary>
    private void addMaskedCards(Container root)
    {
        var cover = textureManager.FromPixelData(64, 64, new byte[64 * 64 * 4]);
        var list = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true
        };

        for (int i = 0; i < 14; i++)
        {
            var card = new Container
            {
                Position = new Vector2(340, i * 68 - 34),
                Size = new Vector2(600, 64),
                Masking = true,
                CornerRadius = 10,
                BorderThickness = 2,
                BorderColor = Color.White
            };

            card.Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.DarkSlateGray
            });

            card.Add(new Sprite
            {
                Texture = cover,
                Size = new Vector2(64)
            });

            card.Add(new Box
            {
                Position = new Vector2(594, 0),
                Size = new Vector2(6, 64),
                Color = Color.HotPink
            });

            list.Add(card);
        }

        root.Add(list);
    }

    /// <summary>
    /// 64 labels of 30 characters down the screen, as in the update thread's text suite.
    /// </summary>
    private static void addText(Container root)
    {
        for (int i = 0; i < 64; i++)
        {
            root.Add(new SpriteText
            {
                Text = "The quick brown fox jumps over",
                Font = FontUsage.Default.With(family: "NotoSans", size: 16f),
                Position = new Vector2(10, i * 11)
            });
        }
    }

    /// <summary>
    /// A root that provides the font store to the text in it.
    /// </summary>
    private partial class FontRoot : Container
    {
        /// <summary>
        /// Caches <paramref name="store"/> for the text loaded under this root.
        /// </summary>
        public void Provide(IFontStore store) => Cache(store);
    }
}
