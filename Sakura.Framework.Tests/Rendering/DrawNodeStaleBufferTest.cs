// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Rendering;

/// <summary>
/// Reproduction for the stuttering drawable bug (https://github.com/HelloYeew/sakura/pull/166)
/// </summary>
[TestFixture]
public class DrawNodeStaleBufferTest
{
    private ManualClock manual = null!;
    private Container root = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };

        root.Load();
        root.LoadComplete();
    }

    /// <summary>
    /// One host frame: update pass, then draw-node generation into the given buffer, exactly as
    /// AppHost.PerformUpdate does.
    /// </summary>
    private ContainerDrawNode frame(int buffer)
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
        return (ContainerDrawNode)root.GenerateDrawNodeSubtree(buffer);
    }

    [Test]
    public void TestChangeAfterUpdatePassReachesEveryBuffer()
    {
        var box = new Box { Position = new Vector2(10, 10), Size = new Vector2(50) };
        root.Add(box);

        for (int i = 0; i < 6; i++)
            frame(i % 3);

        // The change lands after this frame's update traversal already passed over the box —
        // e.g. a sibling that updates later in the traversal writing to it, or a callback.
        manual.CurrentTime += 16;
        root.UpdateSubTree();
        box.Position = new Vector2(200, 10);
        root.GenerateDrawNodeSubtree(0);

        // Nothing else changes from here on. Every buffer must converge on the new position.
        for (int i = 0; i < 9; i++)
        {
            var node = frame((i + 1) % 3);
            float x = node.Children[0].DrawRectangle.X;

            Assert.That(x, Is.EqualTo(200).Within(0.01f),
                $"Frame {i} (buffer {(i + 1) % 3}) drew X={x}; the change must be visible in every buffer.");
        }
    }

    /// <summary>
    /// The same thing without reaching in by hand: children are updated back-to-front, so a
    /// drawable written to by a sibling that sits *later* in the list is always written to after
    /// its own update pass. (This is the shape of a gameplay HUD whose score text is driven by a
    /// playfield added before it.)
    /// </summary>
    [Test]
    public void TestWriteFromLaterUpdatingSiblingReachesEveryBuffer()
    {
        var target = new Box { Position = new Vector2(10, 10), Size = new Vector2(50) };
        var writer = new Writer(target);

        // writer first, target second: the traversal runs target's update before writer's.
        root.Add(writer);
        root.Add(target);

        for (int i = 0; i < 6; i++)
            frame(i % 3);

        writer.NextPosition = new Vector2(200, 10);

        // The frame of the write itself legitimately still draws the old geometry: the new one does
        // not exist until the next update pass recomputes it. Every frame after that must be current.
        frame(0);

        for (int i = 1; i < 10; i++)
        {
            var node = frame(i % 3);
            var targetNode = node.Children[node.Children.Count - 1];

            Assert.That(targetNode.DrawRectangle.X, Is.EqualTo(200).Within(0.01f),
                $"Frame {i} (buffer {i % 3}) drew X={targetNode.DrawRectangle.X}; a sibling's write must reach every buffer.");
        }
    }

    private partial class Writer : Drawable
    {
        public Vector2? NextPosition;

        private readonly Drawable target;

        public Writer(Drawable target)
        {
            this.target = target;
        }

        public override void Update()
        {
            base.Update();

            if (NextPosition is Vector2 position)
            {
                target.Position = position;
                NextPosition = null;
            }
        }
    }

    [Test]
    public void TestMaskedAwayMovementReachesEveryBufferOnReturn()
    {
        var masked = new Container { Size = new Vector2(200, 200), Masking = true };
        var box = new Box { Position = new Vector2(10, 10), Size = new Vector2(50) };
        masked.Add(box);
        root.Add(masked);

        for (int i = 0; i < 6; i++)
            frame(i % 3);

        // Leave the masking bounds, keep moving while outside, then come back to a new position.
        box.Position = new Vector2(500, 500);
        for (int i = 0; i < 3; i++)
            frame(i % 3);

        box.Position = new Vector2(600, 600);
        for (int i = 0; i < 3; i++)
            frame(i % 3);

        box.Position = new Vector2(120, 20);
        for (int i = 0; i < 9; i++)
        {
            var maskedNode = (ContainerDrawNode)frame(i % 3).Children[0];
            Assert.That(maskedNode.Children, Has.Count.EqualTo(1), $"Frame {i}: the box must be back in the draw tree.");

            float x = maskedNode.Children[0].DrawRectangle.X;
            Assert.That(x, Is.EqualTo(120).Within(0.01f),
                $"Frame {i} (buffer {i % 3}) drew X={x}; a drawable returning from masking must be current in every buffer.");
        }
    }
}
