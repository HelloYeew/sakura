// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Tests.Extensions;

/// <summary>
/// Walking a drawable's tree: all of it, in order, internal children included.
/// </summary>
[TestFixture]
public class DrawableExtensionsTest
{
    /// <summary>
    /// Parents before their children, and each container's children in its order.
    /// </summary>
    [Test]
    public void ParentsComeBeforeTheirChildren()
    {
        Box a, c;
        Container b, root = new Container
        {
            Children = new Drawable[]
            {
                a = new Box(),
                b = new Container { Child = c = new Box() },
            }
        };
        var d = new Box();
        root.Add(d);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Descendants(), Is.EqualTo(new Drawable[] { a, b, c, d }));
            Assert.That(root.DescendantsAndSelf(), Is.EqualTo(new Drawable[] { root, a, b, c, d }));
            Assert.That(c.Descendants(), Is.Empty, "a drawable with nothing under it");
        }
    }

    /// <summary>
    /// A container with content of its own lists only that content as its children; the walk reaches the rest too.
    /// </summary>
    [Test]
    public void InternalChildrenToo()
    {
        var holder = new WithContent();
        var item = new Box();
        holder.Add(item);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(holder.Children, Is.EqualTo(new Drawable[] { item }), "as the container lists them");
            Assert.That(holder.Descendants(), Is.EqualTo(new Drawable[] { holder.Frame, holder.Inner, item }));
        }
    }

    /// <summary>
    /// A scrollable container's bars and content holder are under it, as well as what it scrolls.
    /// </summary>
    [Test]
    public void AScrollableContainersBarsToo()
    {
        var item = new Box();
        var scroll = new ScrollableContainer { Child = item };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scroll.Descendants(), Does.Contain(item));
            Assert.That(scroll.Descendants().Count(), Is.GreaterThan(2));
        }
    }

    /// <summary>
    /// A tree far deeper than any screen's is walked without calls as deep as it.
    /// </summary>
    [Test]
    public void ADeepTree()
    {
        var root = new Container();
        Container last = root;

        for (int i = 0; i < 20_000; i++)
        {
            var next = new Container();
            last.Add(next);
            last = next;
        }

        Assert.That(root.Descendants().Count(), Is.EqualTo(20_000));
    }

    private partial class WithContent : Container
    {
        public readonly Box Frame = new Box();
        public readonly Container Inner = new Container();

        protected override Container Content => Inner;

        public WithContent()
        {
            AddInternal(Frame);
            AddInternal(Inner);
        }
    }
}
