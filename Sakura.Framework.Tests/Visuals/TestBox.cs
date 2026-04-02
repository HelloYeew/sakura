// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals;

public class TestBox : TestScene
{
    private Box box = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create Box", () =>
        {
            box = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Color = Color.Red,
                Size = new Vector2(100)
            };
        });

        AddStep("Add a box", () => Add(box));
    }

    [Test]
    public void TestResize()
    {
        AddAssert("Box should be added", () => Children.Count == 1);
        AddAssert("Color should be red", () => box.Color == Color.Red);
        AddStep("Resize box to 200x200", () => box.ResizeTo(new Vector2(200), 500));
        AddWaitStep("Wait for resize to complete", 500);
        AddAssert("Box should be resized to 200x200", () => box.Size == new Vector2(200));
    }

    [Test]
    public void TestFadeOut()
    {
        AddStep("Fade out box", () => box.FadeOut(500));
        AddUntilStep("Wait until box is invisible", () => box.Alpha == 0);
        AddAssert("Box should be faded out", () => box.Alpha == 0);
    }
}
