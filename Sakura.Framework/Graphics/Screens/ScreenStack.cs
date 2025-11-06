// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Graphics.Screens;

/// <summary>
/// A container that manages a stack of <see cref="Screen"/> drawables.
/// Provides functionality for pushing new screens and exiting current ones.
/// </summary>
public class ScreenStack : Container
{
    private readonly Stack<Screen> screenStack = new();

    /// <summary>
    /// The currently active <see cref="Screen"/>.
    /// </summary>
    public Screen? CurrentScreen => screenStack.Count > 0 ? screenStack.Peek() : null;

    /// <summary>
    /// Pushes a new <see cref="Screen"/> onto the stack, making it the active screen.
    /// </summary>
    /// <param name="screen">The screen to push.</param>
    public void Push(Screen screen)
    {
        if (screen.Stack != null)
            throw new InvalidOperationException("Screen is already part of a stack.");

        Screen? lastScreen = CurrentScreen;

        lastScreen?.InternalSuspend(screen);

        screenStack.Push(screen);
        screen.Stack = this;

        screen.Depth = (lastScreen?.Depth ?? -1) + 1;

        Add(screen);

        screen.InternalEnter(lastScreen);

        Logger.Verbose($"Screen stack {this} pushed screen {screen} (depth: {screenStack.Count}).");
    }

    /// <summary>
    /// Exits the currently active <see cref="Screen"/>, removing it from the stack
    /// and resuming the previous screen.
    /// </summary>
    public void Exit()
    {
        if (CurrentScreen == null)
            return;

        Screen exitingScreen = screenStack.Pop();
        exitingScreen.Stack = null;

        Screen? nextScreen = CurrentScreen;

        // Tell the exiting screen it's exiting.
        // The screen's OnExiting method is responsible for transitions
        // and for removing itself from the Parent (this ScreenStack).
        exitingScreen.InternalExit(nextScreen);

        Logger.Verbose($"Screen stack {this} exited screen {exitingScreen} (depth: {screenStack.Count}).");

        // Tell the next screen (if any) that it's resuming.
        nextScreen?.InternalResume(exitingScreen);
    }
}
