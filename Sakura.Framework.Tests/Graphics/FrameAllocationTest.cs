// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// A steady frame of a common tree allocates nothing on the update thread: its update, then its draw nodes, as the host
/// runs them. Allocation is exact, unlike timing, so these hold on any machine, and a change that adds garbage to every
/// frame fails here rather than showing up later as GC pauses.
/// </summary>
/// <remarks>
/// Each test builds its tree on a <see cref="ManualClock"/>, runs warm-up frames, then counts what the thread allocates
/// over the measured frames. Only the tree is in it, not the host's own work (input, statistics, its scheduler).
///
/// The warm-up is as long as the measurement, and every motion that takes children in and out of a mask's view repeats
/// within it, so the measured frames only revisit views the warm-up reached. That keeps the one-time costs out: first
/// calls, caches, and what a child allocates the first time it comes into view. A child that leaves the view and comes
/// back allocates nothing.
/// </remarks>
[TestFixture]
public class FrameAllocationTest
{
    private const double frame_step_ms = 1000.0 / 120;
    private const int measured_frames = 120;
    private const int warm_up_frames = measured_frames;
    private const string label_text = "The quick brown fox jumps over";

    private ManualClock clock = null!;
    private FontRoot root = null!;

    /// <summary>
    /// What changes on the labels each frame, while their text stays the same.
    /// </summary>
    public enum LabelChange
    {
        /// <summary>
        /// Nothing.
        /// </summary>
        None,

        /// <summary>
        /// Every label moves, so its glyph vertices are rebuilt.
        /// </summary>
        Position,

        /// <summary>
        /// Every label fades, the color-only path.
        /// </summary>
        Alpha
    }

    /// <summary>
    /// Opens the logger, which a tree's first load writes to.
    /// </summary>
    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    /// <summary>
    /// Closes the logger.
    /// </summary>
    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    /// <summary>
    /// Makes a loaded 1280×720 root on a manual clock for the test's tree.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        root = new FontRoot
        {
            Size = new Vector2(1280, 720),
            Clock = new FramedClock(clock)
        };

        root.Load();
        root.CompleteLoad();
    }

    /// <summary>
    /// 10 columns of 100 boxes, with nothing changing.
    /// </summary>
    [Test]
    public void AFrameOfStillBoxesAllocatesNothing()
    {
        addColumns(10, 100);

        assertNothingAllocated(allocatedOverFrames());
    }

    /// <summary>
    /// A rhythm game's notes: 4 lanes of 64 boxes, each moved before every frame and wrapped back to the top past the
    /// bottom.
    /// </summary>
    [Test]
    public void AFrameOfMovingBoxesAllocatesNothing()
    {
        var notes = addLanes();

        assertNothingAllocated(allocatedOverFrames(_ =>
        {
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                float y = note.Y + 6;
                note.Y = y > 720 ? -30 : y;
            }
        }));
    }

    /// <summary>
    /// 10 columns of 100 boxes, every one faded before every frame: only their color changes.
    /// </summary>
    [Test]
    public void AFrameOfFadingBoxesAllocatesNothing()
    {
        var boxes = addColumns(10, 100);

        assertNothingAllocated(allocatedOverFrames(frame =>
        {
            float alpha = frame % 2 == 0 ? 0.5f : 0.6f;

            for (int i = 0; i < boxes.Count; i++)
            {
                boxes[i].Alpha = alpha;
            }
        }));
    }

    /// <summary>
    /// 4 columns of 64 boxes, each with a move and a fade running through every measured frame.
    /// </summary>
    [Test]
    public void AFrameOfRunningTransformsAllocatesNothing()
    {
        var boxes = addColumns(4, 64);

        foreach (var box in boxes)
        {
            box.MoveTo(new Vector2(640, 720), 10_000);
            box.FadeTo(0.25f, 10_000);
        }

        assertNothingAllocated(allocatedOverFrames());
    }

    /// <summary>
    /// A masked list of 12 rounded, bordered cards, each masking its own content, sliding up and down through the list's
    /// edges so cards pass in and out of view.
    /// </summary>
    [Test]
    public void AFrameOfMaskedCardsAllocatesNothing()
    {
        var content = new Container
        {
            Size = new Vector2(600, 12 * 68)
        };

        for (int i = 0; i < 12; i++)
        {
            var card = new Container
            {
                Position = new Vector2(0, i * 68),
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

            card.Add(new Box
            {
                Position = new Vector2(594, 0),
                Size = new Vector2(6, 64),
                Color = Color.HotPink
            });

            content.Add(card);
        }

        var list = new Container
        {
            Position = new Vector2(340, 160),
            Size = new Vector2(600, 400),
            Masking = true
        };

        list.Add(content);
        root.Add(list);

        assertNothingAllocated(allocatedOverFrames(frame => content.Y = frame % 100 * 4 - 200));
    }

    /// <summary>
    /// A scrollable list of 60 rows, either dragged a little every frame or gliding to a new target every 30 frames, as
    /// after a turn of the wheel.
    /// </summary>
    [Test]
    public void AFrameOfAScrollingListAllocatesNothing([Values] bool glide)
    {
        var scroll = new ScrollableContainer
        {
            Position = new Vector2(340, 60),
            Size = new Vector2(600, 600)
        };

        for (int i = 0; i < 60; i++)
        {
            scroll.Add(new Box
            {
                Position = new Vector2(0, i * 40),
                Size = new Vector2(580, 36),
                Color = Color.SlateGray
            });
        }

        root.Add(scroll);

        assertNothingAllocated(allocatedOverFrames(frame =>
        {
            if (!glide)
                scroll.CurrentScroll = new Vector2(0, frame % 120 * 10);
            else if (frame % 30 == 0)
                scroll.ScrollTo(new Vector2(0, frame % 60 == 0 ? 1200 : 0));
        }));
    }

    /// <summary>
    /// 64 labels of 30 characters whose text never changes, with <paramref name="change"/> made to every one before
    /// every frame.
    /// </summary>
    [Test]
    public void AFrameOfUnchangedTextAllocatesNothing([Values] LabelChange change)
    {
        using var textureManager = new HeadlessTextureManager();
        using var store = new RendererFontStore(new HeadlessRenderer(textureManager));

        var fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources").GetStorageForDirectory("Fonts");
        store.AddFont(fonts, "Comfortaa-Regular.ttf", alias: "Label");
        root.Provide(store);

        var labels = new List<SpriteText>();

        for (int i = 0; i < 64; i++)
        {
            var label = new SpriteText
            {
                Text = label_text,
                Font = FontUsage.Default.With(family: "Label", size: 16f),
                Position = new Vector2(10, i * 11)
            };

            labels.Add(label);
            root.Add(label);
        }

        assertNothingAllocated(allocatedOverFrames(frame =>
        {
            for (int i = 0; i < labels.Count; i++)
            {
                switch (change)
                {
                    case LabelChange.Position:
                        labels[i].X = 10 + frame % 2;
                        break;

                    case LabelChange.Alpha:
                        labels[i].Alpha = frame % 2 == 0 ? 0.5f : 0.6f;
                        break;
                }
            }
        }));
    }

    /// <summary>
    /// Adds <paramref name="columns"/> containers of <paramref name="rows"/> boxes across the root.
    /// </summary>
    /// <returns>Every box added.</returns>
    private List<Box> addColumns(int columns, int rows)
    {
        var boxes = new List<Box>();
        float width = 1280f / columns;
        float height = 720f / rows;

        for (int i = 0; i < columns; i++)
        {
            var column = new Container
            {
                Position = new Vector2(i * width, 0),
                Size = new Vector2(width, 720)
            };

            for (int j = 0; j < rows; j++)
            {
                var box = new Box
                {
                    Position = new Vector2(0, j * height),
                    Size = new Vector2(width - 2, height - 1),
                    Color = Color.LightPink
                };

                boxes.Add(box);
                column.Add(box);
            }

            root.Add(column);
        }

        return boxes;
    }

    /// <summary>
    /// Adds a 400-pixel playfield of 4 lanes of 64 notes, centered on the root.
    /// </summary>
    /// <returns>Every note added.</returns>
    private List<Box> addLanes()
    {
        var notes = new List<Box>();
        var playfield = new Container
        {
            Position = new Vector2(440, 0),
            Size = new Vector2(400, 720)
        };

        for (int i = 0; i < 4; i++)
        {
            var lane = new Container
            {
                Position = new Vector2(i * 100, 0),
                Size = new Vector2(100, 720)
            };

            for (int j = 0; j < 64; j++)
            {
                var note = new Box
                {
                    Position = new Vector2(5, j * (720f / 64)),
                    Size = new Vector2(90, 30),
                    Color = Color.LightPink
                };

                notes.Add(note);
                lane.Add(note);
            }

            playfield.Add(lane);
        }

        root.Add(playfield);
        return notes;
    }

    /// <summary>
    /// Runs the warm-up frames, then counts what this thread allocates over the measured ones.
    /// </summary>
    /// <param name="beforeFrame">Run before each frame with its number, as a game sets its tree up before the frame.</param>
    /// <returns>The bytes allocated over all the measured frames.</returns>
    private long allocatedOverFrames(Action<int>? beforeFrame = null)
    {
        int frame = 0;

        for (; frame < warm_up_frames; frame++)
        {
            runFrame(frame, beforeFrame);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (; frame < warm_up_frames + measured_frames; frame++)
        {
            runFrame(frame, beforeFrame);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>
    /// One update thread frame, as the host runs it: the tree's update, then its draw nodes into one of three buffers.
    /// </summary>
    private void runFrame(int frame, Action<int>? beforeFrame)
    {
        beforeFrame?.Invoke(frame);

        clock.CurrentTime += frame_step_ms;
        root.UpdateSubTree();
        root.GenerateDrawNodeSubtree(frame % 3);
    }

    /// <summary>
    /// Fails with the bytes allocated unless there were none.
    /// </summary>
    private static void assertNothingAllocated(long allocated) => Assert.That(allocated, Is.Zero, $"{allocated:N0} B over {measured_frames} frames");

    /// <summary>
    /// A root that provides a font store to the text in it.
    /// </summary>
    private partial class FontRoot : Container
    {
        /// <summary>
        /// Caches <paramref name="store"/> for the text loaded under this root.
        /// </summary>
        public void Provide(IFontStore store) => Cache(store);
    }
}
