// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Tests.Input;

/// <summary>
/// <see cref="App.ReleaseHeldInput"/> lets go of whatever is still held. The host calls it when the
/// window loses focus and when a native file dialog closes, where the real release went elsewhere. A
/// button left held would otherwise look held for good (a drag that never ends, say).
/// </summary>
[TestFixture]
public class ReleaseHeldInputTest
{
    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [Test]
    public void TestHeldButtonsAndKeysAreReleased()
    {
        var app = new App();

        app.OnMouseDown(new MouseButtonEvent(new MouseState { Position = new Vector2(10, 20) }, MouseButton.Left, 1));
        app.OnKeyDown(new KeyEvent(Key.D, KeyModifiers.None, false));

        Assume.That(app.InputManager.CurrentState.IsPressed(MouseButton.Left));
        Assume.That(app.InputManager.CurrentState.IsPressed(Key.D));

        app.ReleaseHeldInput();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(app.InputManager.CurrentState.PressedMouseButtons, Is.Empty);
            Assert.That(app.InputManager.CurrentState.PressedKeys, Is.Empty);
            Assert.That(app.InputManager.CurrentState.MousePosition, Is.EqualTo(new Vector2(10, 20)), "Released where the pointer is, not at the origin.");
        }
    }

    [Test]
    public void TestNothingHeldIsANoOp()
    {
        var app = new App();

        Assert.DoesNotThrow(() => app.ReleaseHeldInput());
        Assert.That(app.InputManager.CurrentState.PressedMouseButtons, Is.Empty);
    }
}
