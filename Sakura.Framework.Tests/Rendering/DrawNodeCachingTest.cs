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
/// Tests for draw-node subtree caching: clean subtrees skip regeneration, while anything
/// that changes draw-tree membership or content (invalidations, topology, lifetime
/// crossings, masking transitions) must refresh the cached tree.
/// </summary>
[TestFixture]
public class DrawNodeCachingTest
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

    private void frame()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();
    }

    private ContainerDrawNode generate(int buffer = 0) => (ContainerDrawNode)root.GenerateDrawNodeSubtree(buffer);

    private void settle()
    {
        for (int i = 0; i < 3; i++)
        {
            frame();
            generate(i % 3);
        }
    }

    [Test]
    public void TestIdleGenerationSkipsSubtree()
    {
        var box = new Box { Position = new Vector2(10, 10), Size = new Vector2(50) };
        root.Add(box);
        settle();

        frame();
        var node = generate();
        long version = node.AppliedSubtreeVersion;

        frame();
        node = generate();

        Assert.Multiple(() =>
        {
            Assert.That(node.AppliedSubtreeVersion, Is.EqualTo(version), "An idle subtree must not be regenerated.");
            Assert.That(node.Children, Has.Count.EqualTo(1));
            Assert.That(node.Children[0].DrawRectangle.X, Is.EqualTo(10).Within(0.01f));
        });
    }

    [Test]
    public void TestLeafChangeRefreshesAllBuffers()
    {
        var nest = new Container { Size = new Vector2(400, 400) };
        var box = new Box { Position = new Vector2(10, 10), Size = new Vector2(50) };
        nest.Add(box);
        root.Add(nest);
        settle();

        // Make sure every buffer holds a cached, clean tree.
        for (int i = 0; i < 6; i++)
        {
            frame();
            generate(i % 3);
        }

        box.Position = new Vector2(200, 150);
        frame();

        for (int buffer = 0; buffer < 3; buffer++)
        {
            var nestNode = (ContainerDrawNode)generate(buffer).Children[0];
            Assert.That(nestNode.Children[0].DrawRectangle.X, Is.EqualTo(200).Within(0.01f),
                $"Buffer {buffer} must reflect the new position after a leaf change.");
        }
    }

    [Test]
    public void TestLifetimeAppearanceRefreshesDrawTree()
    {
        var box = new Box { Position = new Vector2(10, 10), Size = new Vector2(50) };
        root.Add(box);
        box.LifetimeStart = manual.CurrentTime + 200;
        settle();

        Assert.That(generate().Children, Is.Empty, "A not-yet-alive drawable must not be in the draw tree.");

        // Cross the lifetime start (several frames so the transition is observed and settled).
        manual.CurrentTime += 300;
        frame();
        frame();

        Assert.That(generate().Children, Has.Count.EqualTo(1),
            "A drawable whose lifetime begins must appear in the draw tree without any other invalidation.");
    }

    [Test]
    public void TestLifetimeExpiryRemovesFromDrawTreeWhenKeptAlive()
    {
        var box = new Box { Position = new Vector2(10, 10), Size = new Vector2(50), RemoveWhenNotAlive = false };
        root.Add(box);
        settle();

        Assert.That(generate().Children, Has.Count.EqualTo(1));

        box.LifetimeEnd = manual.CurrentTime + 50;
        manual.CurrentTime += 100;
        frame();
        frame();

        Assert.Multiple(() =>
        {
            Assert.That(root.Contains(box), Is.True, "RemoveWhenNotAlive=false must keep the child in the hierarchy.");
            Assert.That(generate().Children, Is.Empty, "An expired drawable must leave the draw tree.");
        });
    }

    [Test]
    public void TestMaskingTransitionRefreshesDrawTree()
    {
        var masked = new Container { Size = new Vector2(200, 200), Masking = true };
        var box = new Box { Position = new Vector2(10, 10), Size = new Vector2(50) };
        masked.Add(box);
        root.Add(masked);
        settle();

        var maskedNode = (ContainerDrawNode)generate().Children[0];
        Assert.That(maskedNode.Children, Has.Count.EqualTo(1), "A child inside the mask must be drawn.");

        // Move the child fully outside the masking bounds.
        box.Position = new Vector2(500, 500);
        frame();
        frame();

        maskedNode = (ContainerDrawNode)generate(1).Children[0];
        Assert.That(maskedNode.Children, Is.Empty, "A child fully outside the mask must be culled from the draw tree.");

        // And back inside.
        box.Position = new Vector2(10, 10);
        frame();
        frame();

        maskedNode = (ContainerDrawNode)generate(2).Children[0];
        Assert.That(maskedNode.Children, Has.Count.EqualTo(1), "A child re-entering the mask must reappear in the draw tree.");
    }

    [Test]
    public void TestAddAndRemoveRefreshCachedTree()
    {
        var holder = new Container { Size = new Vector2(400, 400) };
        root.Add(holder);
        settle();

        var holderNode = (ContainerDrawNode)generate().Children[0];
        Assert.That(holderNode.Children, Is.Empty);

        var box = new Box { Size = new Vector2(50) };
        holder.Add(box);
        frame();

        holderNode = (ContainerDrawNode)generate().Children[0];
        Assert.That(holderNode.Children, Has.Count.EqualTo(1), "An added child must appear in the cached draw tree.");

        holder.Remove(box);
        frame();

        holderNode = (ContainerDrawNode)generate().Children[0];
        Assert.That(holderNode.Children, Is.Empty, "A removed child must leave the cached draw tree.");
    }
}
