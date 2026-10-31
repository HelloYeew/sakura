// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// The small conveniences: <see cref="Container.Child"/> read back, <see cref="MarginPadding"/>'s horizontal and vertical
/// shorthand, and <see cref="ClickableContainer.TriggerClick"/>.
/// </summary>
[TestFixture]
public class ContainerShorthandTest
{
    [Test]
    public void ChildReadsTheOnlyChild()
    {
        var box = new Box();
        var container = new Container { Child = box };

        Assert.That(container.Child, Is.SameAs(box));
    }

    [Test]
    public void ChildThrowsWithNoneOrSeveral()
    {
        var container = new Container();
        Assert.Throws<InvalidOperationException>(() => _ = container.Child, "none");

        container.Add(new Box());
        container.Add(new Box());
        Assert.Throws<InvalidOperationException>(() => _ = container.Child, "two");
    }

    /// <summary>
    /// A container showing its children through another (<c>Content</c>) reads as it sets: both go to that one.
    /// </summary>
    [Test]
    public void ChildFollowsContent()
    {
        var box = new Box();
        var container = new WithContent { Child = box };

        Assert.Multiple(() =>
        {
            Assert.That(container.Child, Is.SameAs(box));
            Assert.That(container.Inner.Children, Has.Member(box));
        });
    }

    [Test]
    public void HorizontalAndVerticalSetBothSides()
    {
        var padding = new MarginPadding { Horizontal = 3, Vertical = 5 };

        Assert.Multiple(() =>
        {
            Assert.That(padding.Left, Is.EqualTo(3));
            Assert.That(padding.Right, Is.EqualTo(3));
            Assert.That(padding.Top, Is.EqualTo(5));
            Assert.That(padding.Bottom, Is.EqualTo(5));
            Assert.That(padding.Total, Is.EqualTo(new Vector2(6, 10)));
        });
    }

    [Test]
    public void ASideSetAfterTheShorthandWins()
    {
        var padding = new MarginPadding { Horizontal = 3, Left = 7 };

        Assert.Multiple(() =>
        {
            Assert.That(padding.Left, Is.EqualTo(7));
            Assert.That(padding.Right, Is.EqualTo(3));
        });
    }

    [Test]
    public void TriggerClickDoesWhatAClickDoes()
    {
        int clicks = 0;
        var button = new ClickableContainer { Action = () => clicks++ };

        Assert.Multiple(() =>
        {
            Assert.That(button.TriggerClick(), Is.True);
            Assert.That(clicks, Is.EqualTo(1));
        });
    }

    [Test]
    public void TriggerClickDoesNothingWhileDisabled()
    {
        int clicks = 0;
        var button = new ClickableContainer { Action = () => clicks++ };
        button.Enabled.Value = false;

        Assert.Multiple(() =>
        {
            Assert.That(button.TriggerClick(), Is.False);
            Assert.That(clicks, Is.Zero);
        });
    }

    [Test]
    public void TriggerClickWithNoActionDoesNothing() => Assert.That(new ClickableContainer().TriggerClick(), Is.False);

    private partial class WithContent : Container
    {
        public readonly Container Inner = new Container();

        protected override Container Content => Inner;

        public WithContent()
        {
            AddInternal(Inner);
        }
    }
}
