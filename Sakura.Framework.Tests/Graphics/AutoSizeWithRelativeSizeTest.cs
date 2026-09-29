// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Auto-sizing defers to relative sizing on any axis they share.
/// </summary>
[TestFixture]
public class AutoSizeWithRelativeSizeTest
{
    private partial class Probe : Container
    {
        public void RunAutoSize() => UpdateAutoSize();
    }

    /// <summary>
    /// The shared axis keeps its relative fraction; the other still auto-sizes.
    /// </summary>
    [Test]
    public void RelativeAxisIsNotOverwrittenByAutoSize()
    {
        var probe = new Probe
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Both,
            Width = 1f, // the whole parent
        };

        probe.Add(new Box { Size = new Vector2(21, 30) });
        probe.RunAutoSize();

        Assert.That(probe.Width, Is.EqualTo(1f).Within(0.0001f),
                    "X is relative, so it must stay a fraction rather than take the child's pixel width");
        Assert.That(probe.Height, Is.EqualTo(30f).Within(0.0001f),
                    "Y is not relative, so it should still size to the child");
    }

    /// <summary>
    /// With no overlap, auto-sizing behaves exactly as before.
    /// </summary>
    [Test]
    public void AutoSizeStillAppliesWhenThereIsNoOverlap()
    {
        var probe = new Probe { AutoSizeAxes = Axes.Both };

        probe.Add(new Box { Size = new Vector2(21, 30) });
        probe.RunAutoSize();

        Assert.That(probe.Width, Is.EqualTo(21f).Within(0.0001f));
        Assert.That(probe.Height, Is.EqualTo(30f).Within(0.0001f));
    }
}
