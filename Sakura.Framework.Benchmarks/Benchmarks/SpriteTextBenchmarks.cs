// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using BenchmarkDotNet.Attributes;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Benchmarks.Benchmarks;

/// <summary>
/// A screenful of labels, moved or faded every frame: what plain text costs, which is most text there is. And a score whose
/// text is new every frame, which shapes every frame. One op is one frame on the update thread, its draw nodes included.
/// </summary>
[MemoryDiagnoser]
public class SpriteTextBenchmarks
{
    private const int labels = 64;
    private const string text = "The quick brown fox jumps over";

    /// <summary>
    /// How many different scores the score cycles through: more than any cache keeps, so each frame's is new to it.
    /// </summary>
    private const int score_count = 4096;

    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore store = null!;

    private FontRoot movingRoot = null!;
    private ManualClock movingClock = null!;
    private readonly SpriteText[] moving = new SpriteText[labels];

    private FontRoot fadingRoot = null!;
    private ManualClock fadingClock = null!;
    private readonly SpriteText[] fading = new SpriteText[labels];

    private FontRoot scoreRoot = null!;
    private ManualClock scoreClock = null!;
    private SpriteText score = null!;
    private readonly string[] scores = new string[score_count];

    private int frame;

    [GlobalSetup]
    public void Setup()
    {
        Logger.Initialize();

        textureManager = new HeadlessTextureManager();
        store = new RendererFontStore(new HeadlessRenderer(textureManager));
        store.AddFontFamily(new EmbeddedResourceStorage(typeof(App).Assembly, "Sakura.Framework.Resources").GetStorageForDirectory("Fonts"), "NotoSans", hasItalics: true);

        (movingRoot, movingClock) = createRoot(moving);
        (fadingRoot, fadingClock) = createRoot(fading);

        // Made ahead, so formatting the score isn't part of the frame.
        for (int i = 0; i < score_count; i++)
        {
            scores[i] = (12345678 + i * 7).ToString("D8");
        }

        scoreClock = new ManualClock();
        scoreRoot = new FontRoot { Size = new Vector2(1280, 720), Clock = new FramedClock(scoreClock) };
        scoreRoot.Load();
        scoreRoot.CompleteLoad();
        scoreRoot.Provide(store);
        scoreRoot.Add(score = new SpriteText
        {
            Text = scores[0],
            Font = FontUsage.Default.With(family: "NotoSans", size: 32f)
        });
        scoreRoot.UpdateSubTree();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        store.Dispose();
        textureManager.Dispose();
    }

    private (FontRoot Root, ManualClock Clock) createRoot(SpriteText[] sprites)
    {
        var clock = new ManualClock();
        var root = new FontRoot { Size = new Vector2(1280, 720), Clock = new FramedClock(clock) };

        root.Load();
        root.CompleteLoad();
        root.Provide(store);

        for (int i = 0; i < labels; i++)
        {
            root.Add(sprites[i] = new SpriteText
            {
                Text = text,
                Font = FontUsage.Default.With(family: "NotoSans", size: 16f),
                Position = new Vector2(10, i * 11)
            });
        }

        root.UpdateSubTree();
        return (root, clock);
    }

    [Benchmark]
    public DrawNode MovedEveryFrame()
    {
        frame++;

        float x = 10 + MathF.Sin(frame * 0.05f) * 8;

        for (int i = 0; i < labels; i++)
            moving[i].X = x;

        movingClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        movingRoot.UpdateSubTree();
        return movingRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    [Benchmark]
    public DrawNode FadedEveryFrame()
    {
        frame++;

        float alpha = 0.25f + 0.75f * (MathF.Sin(frame * 0.05f) * 0.5f + 0.5f);

        for (int i = 0; i < labels; i++)
            fading[i].Alpha = alpha;

        fadingClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        fadingRoot.UpdateSubTree();
        return fadingRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    /// <summary>
    /// A score shown with new text every frame, as a game's score is while notes are hit.
    /// </summary>
    [Benchmark]
    public DrawNode ScoreChangedEveryFrame()
    {
        frame++;

        score.Text = scores[frame % score_count];

        scoreClock.CurrentTime += BenchmarkTree.FRAME_STEP_MS;
        scoreRoot.UpdateSubTree();
        return scoreRoot.GenerateDrawNodeSubtree(frame % 3);
    }

    private partial class FontRoot : Container
    {
        public void Provide(IFontStore fontStore) => Cache(fontStore);
    }
}
