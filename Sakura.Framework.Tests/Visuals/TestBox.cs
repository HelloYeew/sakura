// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals;

public class TestBox : TestScene
{
    private Box box;

    public TestBox()
    {
        box = new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Color = Color.Red,
            Size = new Vector2(100)
        };
        AddStep("Add a box", () => Add(box));
        AddAssert("Box should be added", () => Children.Count == 1);
        AddAssert("Color should be red", () => box.Color == Color.Red);
        AddStep("Resize box to 200x200", () => box.ResizeTo(new Vector2(200), 500));
        AddAssert("Box should be resized to 200x200", () => box.Size == new Vector2(200));
    }
}
