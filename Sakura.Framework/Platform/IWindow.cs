// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Input;

namespace Sakura.Framework.Platform;

public interface IWindow : IDisposable
{
    /// <summary>
    /// The window's title.
    /// </summary>
    string Title { get; set; }

    /// <summary>
    /// Whether the window can be resizable by the user.
    /// </summary>
    bool Resizable { get; set; }

    /// <summary>
    /// Whether the window is currently active and has focus.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Whether the user has requested to exit the window.
    /// </summary>
    bool IsExiting { get; }

    /// <summary>
    /// The refresh rate of the display the window is on.
    /// </summary>
    int DisplayHz { get; }

    /// <summary>
    /// Current width of the window's drawable area in pixels.
    /// <remarks>
    /// This value got update when the window is resized, to get it in real-time use <see cref="GetDrawableSize"/> method instead,
    /// but use this for general purpose.
    /// </remarks>
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Current height of the window's drawable area in pixels.
    /// <remarks>
    /// This value got update when the window is resized, to get it in real-time use <see cref="GetDrawableSize"/> method instead,
    /// but use this for general purpose.
    /// </remarks>
    /// </summary>
    int Height { get; }

    /// <summary>
    /// The graphic surface associated with this window to render graphics to.
    /// This is a value that the graphic API will use to render graphics to the window.
    /// </summary>
    IGraphicsSurface GraphicsSurface { get; }

    /// <summary>
    /// Initialize all necessary parts for the window.
    /// This needs to be called before any function that interacts with the window.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Create the window.
    /// </summary>
    void Create();

    void PollEvents();

    void SwapBuffers();

    void SetVSync(bool enabled);

    /// <summary>
    /// Gets the underlying drawable surface size in physical pixels.
    /// </summary>
    void GetDrawableSize(out int width, out int height);

    event Action Update;
    event Action Suspended;
    event Action Resumed;
    event Action ExitRequested;
    event Action Exited;

    /// <summary>
    /// Invoked when a key is pressed.
    /// </summary>
    event Action<KeyEvent> OnKeyDown;

    /// <summary>
    /// Invoked when a key is released.
    /// </summary>
    event Action<KeyEvent> OnKeyUp;

    event Action<int> DisplayChanged;
    event Action<int, int> Resized;

    event Action<MouseButtonEvent> OnMouseDown;
    event Action<MouseButtonEvent> OnMouseUp;
    event Action<MouseEvent> OnMouseMove;
    event Action<ScrollEvent> OnScroll;

    /// <summary>
    /// Close the window peacefully.
    /// </summary>
    void Close();
}
