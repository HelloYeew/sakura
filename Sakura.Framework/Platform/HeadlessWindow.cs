// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform.Dialogs;
using Sakura.Framework.Reactive;

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
    public WindowMode WindowMode { get; set; } = WindowMode.Windowed;
    public Reactive<WindowMode> WindowModeReactive { get; } = new Reactive<WindowMode>(WindowMode.Windowed);
    public bool CursorVisible { get; set; } = true;
    public Reactive<CursorState> CursorState { get; } = new Reactive<CursorState>(Input.CursorState.Default);
    public bool CursorInWindow { get; } = true;
    public bool RelativeMouseMode { get; set; }
    public double CursorSensitivity { get; set; } = 1.0;
    public Reactive<MarginPadding> SafeAreaPadding { get; } = new Reactive<MarginPadding>(new MarginPadding());
    public IGraphicsSurface GraphicsSurface { get; } = new HeadlessGraphicsSurface();
    private string headlessClipboard = string.Empty;
    public void Initialize()
    {
        Logger.Verbose("🪟 Headless window initialized");
    }

    public void Create()
    {

    }

    public void Show()
    {

    }

    public void PollEvents()
    {
        Update?.Invoke();
    }

    public void SwapBuffers()
    {

    }

    public void MakeCurrent()
    {

    }

    public void ClearCurrent()
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
    public void StartTextInput()
    {

    }

    public void StopTextInput()
    {

    }

    public void SetTextInputRect(RectangleF rect)
    {

    }

    public string GetClipboardText()
    {
        return headlessClipboard;
    }

    public void SetClipboardText(string text)
    {
        headlessClipboard = text;
    }

    public FileDialogResult FileDialogResult { get; set; } = FileDialogResult.Cancelled;

    public FileDialogOptions? LastFileDialogOptions { get; private set; }

    public void ShowOpenFileDialog(FileDialogOptions options, Action<FileDialogResult> callback)
    {
        LastFileDialogOptions = options;
        callback?.Invoke(FileDialogResult);
    }

    public void ShowSaveFileDialog(FileDialogOptions options, Action<FileDialogResult> callback)
    {
        LastFileDialogOptions = options;
        callback?.Invoke(FileDialogResult);
    }

    public void ShowOpenFolderDialog(FileDialogOptions options, Action<FileDialogResult> callback)
    {
        LastFileDialogOptions = options;
        callback?.Invoke(FileDialogResult);
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
    public event Action<TextInputEvent>? OnTextInput;
    public event Action<TextEditingEvent>? OnTextEditing;
    public event Action<GamepadButtonEvent>? OnGamepadButtonDown;
    public event Action<GamepadButtonEvent>? OnGamepadButtonUp;
    public event Action<GamepadAxisEvent>? OnGamepadAxisMotion;
    public event Action<GamepadConnectedEvent>? OnGamepadConnected;
    public event Action<GamepadDisconnectedEvent>? OnGamepadDisconnected;
    public event Action? RenderRequested;

    public void Dispose()
    {
    }
}
