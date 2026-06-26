// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Input;

public partial class TestCursorFollowsMove : ManualInputManagerTestScene
{
    private Box marker = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add a marker box", () =>
        {
            TestContent.Clear();
            TestContent.Add(marker = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(80),
                Color = Color.SteelBlue
            });
        });
    }

    [Test]
    public void TestMoveUpdatesCursorStateWithoutClick()
    {
        Vector2 target = Vector2.Zero;

        AddStep("Move mouse to marker (no click)", () =>
        {
            InputManager.MoveMouseTo(marker);
            target = marker.DrawRectangle.Center;
        });

        AddAssert("Input state tracks the move", () =>
            Precision.AlmostEquals(InputManager.InputManager.CurrentState.MousePosition, target, 1f));
    }

    [Test]
    public void TestSubsequentMovesKeepTracking()
    {
        AddStep("Move to centre", () => InputManager.MoveMouseTo(marker));

        Vector2 elsewhere = new Vector2(50, 50);
        AddStep("Move to a different point (no click)", () => InputManager.MoveMouseTo(elsewhere));
        AddAssert("State followed the second move", () =>
            Precision.AlmostEquals(InputManager.InputManager.CurrentState.MousePosition, elsewhere, 1f));
    }
}
