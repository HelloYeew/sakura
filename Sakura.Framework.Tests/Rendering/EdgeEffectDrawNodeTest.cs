// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Rendering;

[TestFixture]
public class EdgeEffectDrawNodeTest
{
    private const float tolerance = 0.01f;

    private Container root = null!;
    private Container target = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        root = new Container
        {
            Size = new Vector2(800, 600)
        };
        target = new Container
        {
            Position = new Vector2(100, 100),
            Size = new Vector2(200, 100),
            CornerRadius = 10,
        };

        root.Add(target);
        root.Load();
        root.LoadComplete();
    }

    private ContainerDrawNode targetNode(int bufferIndex)
    {
        root.UpdateSubTree();
        var rootNode = (ContainerDrawNode)root.GenerateDrawNodeSubtree(bufferIndex);
        return (ContainerDrawNode)rootNode.Children[0];
    }

    [Test]
    public void TestEdgeEffectIsSnapshotted()
    {
        target.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Shadow,
            Color = Color.FromArgb(128, Color.Black),
            Radius = 12,
            Roundness = 4,
            Offset = new Vector2(0, 5),
            Hollow = false,
        };

        var node = targetNode(0);

        Assert.Multiple(() =>
        {
            Assert.That(node.EdgeEffect.Type, Is.EqualTo(EdgeEffectType.Shadow));
            Assert.That(node.EdgeEffect.Radius, Is.EqualTo(12).Within(tolerance));
            Assert.That(node.EdgeEffect.Roundness, Is.EqualTo(4).Within(tolerance));
            Assert.That(node.EdgeEffect.Offset.Y, Is.EqualTo(5).Within(tolerance));
            Assert.That(node.EdgeEffect.Color.A, Is.EqualTo(128));
        });
    }

    [Test]
    public void TestDefaultEdgeEffectIsNone()
    {
        var node = targetNode(0);
        Assert.That(node.EdgeEffect.Type, Is.EqualTo(EdgeEffectType.None));
    }

    [Test]
    public void TestEdgeEffectChangeIsReflectedImmediately()
    {
        targetNode(0);

        target.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Color = Color.Cyan,
            Radius = 20,
        };

        var node = targetNode(1);
        Assert.Multiple(() =>
        {
            Assert.That(node.EdgeEffect.Type, Is.EqualTo(EdgeEffectType.Glow));
            Assert.That(node.EdgeEffect.Radius, Is.EqualTo(20).Within(tolerance));
        });
    }

    [Test]
    public void TestFadeEdgeEffectRadiusTransform()
    {
        target.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Color = Color.White,
            Radius = 0,
        };

        // Apply the radius transform directly to validate it mutates the struct in place.
        var transform = new EdgeEffectRadiusTransform
        {
            StartTime = 0,
            EndTime = 100,
            EndValue = 50,
        };

        transform.Apply(target, 100);

        Assert.That(target.EdgeEffect.Radius, Is.EqualTo(50).Within(tolerance));
    }

    [Test]
    public void TestEdgeEffectColorTransform()
    {
        target.EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Shadow,
            Color = Color.Black,
            Radius = 10,
        };

        var transform = new EdgeEffectColorTransform
        {
            StartTime = 0,
            EndTime = 100,
            EndValue = Color.White,
        };

        transform.Apply(target, 100);

        Assert.Multiple(() =>
        {
            Assert.That(target.EdgeEffect.Color.R, Is.EqualTo(255));
            Assert.That(target.EdgeEffect.Color.G, Is.EqualTo(255));
            Assert.That(target.EdgeEffect.Color.B, Is.EqualTo(255));
            Assert.That(target.EdgeEffect.Type, Is.EqualTo(EdgeEffectType.Shadow));
            Assert.That(target.EdgeEffect.Radius, Is.EqualTo(10).Within(tolerance));
        });
    }
}
