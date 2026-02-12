// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Platform;

public class HeadlessWindow : IWindow
{
    public string Title { get; set; } = "Headless Window";
    public string ApplicationName { get; set; } = "Headless Application";
    public bool Resizable { get; set; }
    public bool IsActive => true;
    public bool IsExiting { get; private set; }
    public int DisplayHz => 60;
    public int Width => 800;
    public int Height => 600;
    public IGraphicsSurface GraphicsSurface { get; } = new HeadlessGraphicsSurface();
    public void Initialize()
    {
        Logger.Verbose("🪟 Headless window initialized");
    }

    public void Create()
    {

    }

    public void PollEvents()
    {
        Update?.Invoke();
    }

    public void SwapBuffers()
    {

    }

    public void SetVSync(bool enabled)
    {

    }

    public void GetDrawableSize(out int width, out int height)
    {
        width = Width;
        height = Height;
    }

    public void GetPhysicalSize(out int width, out int height)
    {
        width = Width;
        height = Height;
    }

    public void Close()
    {
        IsExiting = true;
        ExitRequested?.Invoke();
        Exited?.Invoke();
    }

    public event Action? Update;
    public event Action? FocusLost;
    public event Action? FocusGained;
    public event Action? Minimized;
    public event Action? Restored;
    public event Action? ExitRequested;
    public event Action? Exited;
    public event Action<KeyEvent>? OnKeyDown;
    public event Action<KeyEvent>? OnKeyUp;
    public event Action<int>? DisplayChanged;
    public event Action<int, int>? Resized;
    public event Action<MouseButtonEvent>? OnMouseDown;
    public event Action<MouseButtonEvent>? OnMouseUp;
    public event Action<MouseEvent>? OnMouseMove;
    public event Action<ScrollEvent>? OnScroll;
    public event Action<DragDropFileEvent>? OnDragDropFile;
    public event Action<DragDropTextEvent>? OnDragDropText;
    public event Action? RenderRequested;

    public void Dispose()
    {
    }
}
