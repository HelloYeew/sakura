// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using Sakura.Framework.Configurations;
using Sakura.Framework.Input;
using Silk.NET.OpenGL;
using Silk.NET.SDL;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Reactive;
using Silk.NET.Maths;
using SilkMouseButtonEvent = Silk.NET.SDL.MouseButtonEvent;
using SakuraCursorState = Sakura.Framework.Input.CursorState;
using SakuraMouseButtonEvent = Sakura.Framework.Input.MouseButtonEvent;
using TextEditingEvent = Sakura.Framework.Input.TextEditingEvent;
using TextInputEvent = Sakura.Framework.Input.TextInputEvent;
using Version = Silk.NET.SDL.Version;

namespace Sakura.Framework.Platform;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SDLWindow : IWindow
{
    private static Sdl sdl;
    private static unsafe void* glContext;
    private static unsafe Window* window;
    private PfnEventFilter resizeEventFilter;

    // Graphics surfaces — only one will be active depending on the selected renderer.
    private readonly SDLGraphicsSurface glSurface = new SDLGraphicsSurface();
    private readonly MetalGraphicsSurface metalSurface = new MetalGraphicsSurface();
    private IGraphicsSurface activeSurface;

    private RendererType graphicsApi = RendererType.OpenGL;

    private FrameworkConfigManager windowConfig;

    private readonly MouseState mouseState = new MouseState();

    private bool initialized;

    private string title = "Window";
    private string applicationName = "Sakura Framework App";
    private bool resizable = true;
    private int currentWidth;
    private int currentHeight;
    private int logicalWidth;
    private int logicalHeight;
    private bool? pendingTextInputState;
    private RectangleF? pendingTextInputRect;
    private readonly Lock textInputLock = new Lock();

    private WindowMode windowMode = WindowMode.Windowed;

    // Base flags — no graphics API flag yet; added by SetGraphicsApi() before Create().
    private WindowFlags windowFlags = WindowFlags.AllowHighdpi;

    public string Title
    {
        get => title;
        set => setTitle(value);
    }

    public string ApplicationName
    {
        get => applicationName;
        set => setApplicationName(value);
    }

    public bool Resizable
    {
        get => resizable;
        set => setResizable(value);
    }

    public WindowMode WindowMode
    {
        get => windowMode;
        set => setWindowMode(value);
    }

    public Reactive<WindowMode> WindowModeReactive { get; } = new Reactive<WindowMode>(WindowMode.Windowed);

    public int Width => logicalWidth;
    public int Height => logicalHeight;

    public bool CursorInWindow { get; private set; }
    public IGraphicsSurface GraphicsSurface => activeSurface;

    /// <summary>
    /// Provides the config manager so the window can persist and restore its position, size and mode.
    /// Must be called before <see cref="Create()"/>.
    /// </summary>
    public void SetWindowConfig(FrameworkConfigManager config)
    {
        windowConfig = config;

        var savedMode = config.Get<WindowMode>(FrameworkSetting.WindowMode).Value;
        WindowModeReactive.Value = savedMode;
    }

    /// <summary>
    /// Must be called by <see cref="AppHost"/> before <see cref="Create()"/> to configure
    /// which graphics API flag to apply to the SDL window.
    /// </summary>
    public void SetGraphicsApi(RendererType type)
    {
        graphicsApi = type;

        switch (type)
        {
            case RendererType.Metal:
                // Metal windows need no special SDL window flag on SDL2.
                activeSurface = metalSurface;
                break;

            default:
                // OpenGL
                windowFlags |= WindowFlags.Opengl;
                activeSurface = glSurface;

                // SDL requires GL attributes to be set before SDL_CreateWindow.
                sdl.GLSetAttribute(GLattr.ContextMajorVersion, 3);
                sdl.GLSetAttribute(GLattr.ContextMinorVersion, 3);
                sdl.GLSetAttribute(GLattr.ContextFlags, (int)ContextFlagMask.ForwardCompatibleBit);
                sdl.GLSetAttribute(GLattr.ContextProfileMask, (int)GLprofile.Core);
                sdl.GLSetAttribute(GLattr.FramebufferSrgbCapable, 1);
                break;
        }
    }

    public bool IsActive { get; private set; } = true;
    public bool IsExiting { get; private set; }
    public int DisplayHz { get; private set; } = 60;

    // TODO: This update action also no longer needed since it's handled in host's main loop
    public event Action Update = delegate { };
    public event Action FocusLost = delegate { };
    public event Action FocusGained = delegate { };
    public event Action Minimized = delegate { };
    public event Action Restored = delegate { };
    public event Action ExitRequested = delegate { };
    public event Action Exited = delegate { };

    public event Action<KeyEvent> OnKeyDown = delegate { };
    public event Action<KeyEvent> OnKeyUp = delegate { };
    public event Action<int> DisplayChanged = delegate { };
    public event Action<int, int> Resized = delegate { };

    public event Action<SakuraMouseButtonEvent> OnMouseDown = delegate { };
    public event Action<SakuraMouseButtonEvent> OnMouseUp = delegate { };
    public event Action<MouseEvent> OnMouseMove = delegate { };
    public event Action<ScrollEvent> OnScroll = delegate { };
    public event Action<DragDropFileEvent> OnDragDropFile = delegate { };
    public event Action<DragDropTextEvent> OnDragDropText = delegate { };
    public event Action<TextInputEvent> OnTextInput = delegate { };
    public event Action<TextEditingEvent> OnTextEditing = delegate { };

    public event Action RenderRequested = delegate { };

    public void StartTextInput()
    {
        lock (textInputLock)
        {
            pendingTextInputState = true;
        }
    }

    public void StopTextInput()
    {
        lock (textInputLock)
        {
            pendingTextInputState = false;
        }
    }

    public void SetTextInputRect(RectangleF rect)
    {
        lock (textInputLock)
        {
            pendingTextInputRect = rect;
        }
    }

    public unsafe void Initialize()
    {
        sdl = Sdl.GetApi();

        setApplicationName(applicationName);

        // Make sure SDL video backend is fully initialized
        // To make it support for OpenGL context and process advance hint like profile version
        if (sdl.Init(Sdl.InitVideo) < 0)
        {
            throw new Exception($"Failed to initialize SDL: {sdl.GetErrorS()}");
        }

        Version sdlVersion = new Version();
        sdl.GetVersion(ref sdlVersion);

        byte* sdlRevision = sdl.GetRevision();
        byte* videoDriver = sdl.GetCurrentVideoDriver();

        Logger.Verbose("🪟 SDL initialized");
        Logger.Verbose($"SDL Version: {sdlVersion.Major}.{sdlVersion.Minor}.{sdlVersion.Patch}");
        Logger.Verbose($"SDL Revision: {new string((sbyte*)sdlRevision)}");
        Logger.Verbose($"SDL Video Driver: {new string((sbyte*)videoDriver)}");

        CursorState.ValueChanged += _ => updateSdlCursor();

        initialized = true;
    }

    public unsafe void Create()
    {
        if (Resizable)
        {
            windowFlags |= WindowFlags.Resizable;
        }

        if (RuntimeInfo.IsMacOS && windowMode == WindowMode.Fullscreen)
        {
            // SDL's full screen on MacOS has a lot of issues so we force MacOS to use only window and borderless window modes
            Logger.Warning("Fullscreen mode is not supported on MacOS due to a lot of issues with SDL implementation, falling back to Borderless window mode.");
            windowMode = WindowMode.Borderless;
        }

        switch (windowMode)
        {
            case WindowMode.Borderless:
                windowFlags |= WindowFlags.FullscreenDesktop;
                break;
            case WindowMode.Fullscreen:
                windowFlags |= WindowFlags.Fullscreen;
                break;
        }

        int spawnX = Sdl.WindowposCentered;
        int spawnY = Sdl.WindowposCentered;
        int spawnW = 800;
        int spawnH = 600;

        if (windowConfig != null && windowMode == WindowMode.Windowed)
        {
            int savedX = windowConfig.Get<int>(FrameworkSetting.WindowX).Value;
            int savedY = windowConfig.Get<int>(FrameworkSetting.WindowY).Value;
            int savedW = windowConfig.Get<int>(FrameworkSetting.WindowWidth).Value;
            int savedH = windowConfig.Get<int>(FrameworkSetting.WindowHeight).Value;

            if (savedX != -1 && savedY != -1 && isPositionOnConnectedDisplay(savedX, savedY))
            {
                spawnX = savedX;
                spawnY = savedY;
            }
            else if (savedX != -1 && savedY != -1)
            {
                // fallback to center if the saved position is invalid (e.g. monitor was disconnected)
                Logger.Warning($"Saved window position ({savedX},{savedY}) is not on any connected display. Resetting to center.");
                windowConfig.Get<int>(FrameworkSetting.WindowX).Value = -1;
                windowConfig.Get<int>(FrameworkSetting.WindowY).Value = -1;
            }
            if (savedW > 0 && savedH > 0)
            {
                spawnW = savedW;
                spawnH = savedH;
            }
        }

        window = sdl.CreateWindow(
            title,
            spawnX,
            spawnY,
            spawnW,
            spawnH,
            (uint)windowFlags
        );

        if (window == null)
        {
            Logger.Error("Failed to create SDL window: " + sdl.GetErrorS());
            throw new Exception("SDL window creation failed.");
        }

        resizeEventFilter = new PfnEventFilter(resizeCallback);
        sdl.AddEventWatch(resizeEventFilter, null);

        DisplayHz = getDisplayRefreshRate();
        Logger.Verbose($"Display refresh rate: {DisplayHz} Hz");

        int w, h;
        sdl.GetWindowSize(window, &w, &h);
        logicalWidth = w;
        logicalHeight = h;

        // Physical size is resolved after the graphics context is initialised
        // (GL: GLGetDrawableSize; Metal: GetDrawableSize falls back to logical).
        currentWidth = w;
        currentHeight = h;

        Logger.Verbose("SDL window created successfully");
    }

    /// <summary>
    /// Creates the OpenGL context and wires up the <see cref="SDLGraphicsSurface"/>.
    /// Must be called after <see cref="Create()"/> when the selected renderer is OpenGL.
    /// </summary>
    public unsafe void InitializeGLContext()
    {
        glContext = sdl.GLCreateContext(window);
        if (glContext == null)
        {
            Logger.Error("Failed to create OpenGL context: " + sdl.GetErrorS());
            throw new Exception("OpenGL context creation failed.");
        }

        sdl.GLMakeCurrent(window, glContext);

        // Wire up the GL surface callbacks used by GLRenderer.
        glSurface.GetFunctionAddress = proc => (nint)sdl.GLGetProcAddress(proc);
        glSurface.MakeCurrent = () => sdl.GLMakeCurrent(window, glContext);
        glSurface.ClearCurrent = () => sdl.GLMakeCurrent(window, null);

        // GL drawable size may differ from logical size on HiDPI displays.
        int phyW, phyH;
        sdl.GLGetDrawableSize(window, &phyW, &phyH);
        currentWidth = phyW;
        currentHeight = phyH;

        Logger.Verbose("OpenGL context created successfully");
    }

    /// <summary>
    /// Creates the Metal view via SDL and stores the <c>CAMetalLayer</c> pointer
    /// in the <see cref="MetalGraphicsSurface"/>. Must be called after <see cref="Create()"/>
    /// when the selected renderer is Metal.
    /// </summary>
    public unsafe void InitializeMetalSurface()
    {
        void* view = SDL_Metal_CreateView(window);
        if (view == null)
        {
            Logger.Error("Failed to create Metal view: " + sdl.GetErrorS());
            throw new Exception("Metal view creation failed.");
        }

        void* layer = SDL_Metal_GetLayer(view);
        if (layer == null)
        {
            Logger.Error("Failed to get CAMetalLayer from Metal view.");
            throw new Exception("CAMetalLayer retrieval failed.");
        }

        metalSurface.MetalLayer = (nint)layer;

        Logger.Verbose("Metal surface initialised successfully");
    }

    // SDL2 Metal API — not exposed by Silk.NET.SDL, so P/Invoke directly.
    [DllImport("SDL2")]
    private static extern unsafe void* SDL_Metal_CreateView(void* window);

    [DllImport("SDL2")]
    private static extern unsafe void* SDL_Metal_GetLayer(void* view);

    public void Close()
    {
        IsExiting = true;
    }

    public unsafe void GetDrawableSize(out int width, out int height)
    {
        if (window == null)
        {
            width = 0;
            height = 0;
            return;
        }

        int drawableWidth, drawableHeight;

        if (graphicsApi == RendererType.Metal)
        {
            // Metal: drawable size equals the window's pixel size on HiDPI.
            sdl.GetWindowSizeInPixels(window, &drawableWidth, &drawableHeight);
        }
        else
        {
            sdl.GLGetDrawableSize(window, &drawableWidth, &drawableHeight);
        }

        width = drawableWidth;
        height = drawableHeight;
    }

    public void GetPhysicalSize(out int width, out int height)
    {
        width = currentWidth;
        height = currentHeight;
    }

    private unsafe (float scaleX, float scaleY) getDisplayScale()
    {
        // On high DPI display, the drawable size's width and height
        // and window size's width and height are different.
        // This function returns the scale factor between them.
        // to make the mouse position correct.
        if (window == null) return (1.0f, 1.0f);

        int windowWidth, windowHeight;
        sdl.GetWindowSize(window, &windowWidth, &windowHeight);

        if (windowWidth == 0 || windowHeight == 0) return (1.0f, 1.0f);

        int drawableWidth, drawableHeight;

        if (graphicsApi == RendererType.Metal)
            sdl.GetWindowSizeInPixels(window, &drawableWidth, &drawableHeight);
        else
            sdl.GLGetDrawableSize(window, &drawableWidth, &drawableHeight);

        return ((float)drawableWidth / windowWidth, (float)drawableHeight / windowHeight);
    }

    private unsafe int resizeCallback(void* userData, Event* @event)
    {
        if (@event->Type == (uint)EventType.Windowevent)
        {
            if (@event->Window.Event == (byte)WindowEventID.Resized ||
                @event->Window.Event == (byte)WindowEventID.SizeChanged ||
                @event->Window.Event == (byte)WindowEventID.Exposed)
            {
                handleWindowEvent(@event->Window);
                RenderRequested.Invoke();
            }
        }

        return 1;
    }

    private unsafe void setWindowMode(WindowMode newMode)
    {
        if (windowMode == newMode)
            return;

        if (RuntimeInfo.IsMacOS && newMode == WindowMode.Fullscreen)
        {
            newMode = WindowMode.Borderless;
        }

        windowMode = newMode;
        WindowModeReactive.Value = newMode;

        if (windowConfig != null)
        {
            windowConfig.Get<WindowMode>(FrameworkSetting.WindowMode).Value = newMode;
        }
        else
        {
            Logger.Warning("windowConfig is null — WindowMode change will not be persisted.");
        }


        if (window == null) return;

        switch (newMode)
        {
            case WindowMode.Windowed:
                sdl.SetWindowFullscreen(window, 0);
                sdl.SetWindowBordered(window, SdlBool.True);

                if (windowConfig != null)
                {
                    int savedX = windowConfig.Get<int>(FrameworkSetting.WindowX).Value;
                    int savedY = windowConfig.Get<int>(FrameworkSetting.WindowY).Value;
                    int savedW = windowConfig.Get<int>(FrameworkSetting.WindowWidth).Value;
                    int savedH = windowConfig.Get<int>(FrameworkSetting.WindowHeight).Value;

                    if (savedW > 0 && savedH > 0)
                        sdl.SetWindowSize(window, savedW, savedH);

                    if (savedX != -1 && savedY != -1 && isPositionOnConnectedDisplay(savedX, savedY))
                        sdl.SetWindowPosition(window, savedX, savedY);
                    else
                        sdl.SetWindowPosition(window, Sdl.WindowposCentered, Sdl.WindowposCentered);
                }
                else
                {
                    sdl.SetWindowPosition(window, Sdl.WindowposCentered, Sdl.WindowposCentered);
                }
                break;

            case WindowMode.Borderless:
                sdl.SetWindowFullscreen(window, (uint)WindowFlags.FullscreenDesktop);
                sdl.SetWindowFullscreen(window, (uint)WindowFlags.FullscreenDesktop);
                break;

            case WindowMode.Fullscreen:
                sdl.SetWindowFullscreen(window, (uint)WindowFlags.Fullscreen);
                break;
        }

        int w, h, phyW, phyH;
        sdl.GetWindowSize(window, &w, &h);

        if (graphicsApi == RendererType.Metal)
            sdl.GetWindowSizeInPixels(window, &phyW, &phyH);
        else
            sdl.GLGetDrawableSize(window, &phyW, &phyH);

        logicalWidth = w;
        logicalHeight = h;
        currentWidth = phyW;
        currentHeight = phyH;

        Resized.Invoke(logicalWidth, logicalHeight);

        Logger.Debug($"Window mode changed to {newMode}");
    }

    private unsafe void handleSdlEvents()
    {
        Event sdlEvent;
        while (sdl.PollEvent(&sdlEvent) != 0)
        {
            switch ((EventType)sdlEvent.Type)
            {
                case EventType.Quit:
                    ExitRequested.Invoke();
                    IsExiting = true;
                    break;

                case EventType.Windowevent:
                    handleWindowEvent(sdlEvent.Window);
                    break;

                case EventType.Keydown:
                    handleKeyEvent(sdlEvent.Key, OnKeyDown);
                    break;

                case EventType.Keyup:
                    handleKeyEvent(sdlEvent.Key, OnKeyUp);
                    break;

                case EventType.Mousebuttondown:
                    handleMouseButtonEvent(sdlEvent.Button, OnMouseDown);
                    break;

                case EventType.Mousebuttonup:
                    handleMouseButtonEvent(sdlEvent.Button, OnMouseUp);
                    break;

                case EventType.Mousemotion:
                    handleMouseMotionEvent(sdlEvent.Motion);
                    break;

                case EventType.Mousewheel:
                    handleMouseWheelEvent(sdlEvent.Wheel);
                    break;

                case EventType.Dropfile:
                    handleDropFileEvent(sdlEvent.Drop);
                    break;

                case EventType.Droptext:
                    handleDropTextEvent(sdlEvent.Drop);
                    break;

                case EventType.Textinput:
                    handleTextInputEvent(sdlEvent.Text);
                    break;

                case EventType.Textediting:
                    handleTextEditingEvent(sdlEvent.Edit);
                    break;
            }
        }
    }

    private unsafe void handleWindowEvent(WindowEvent sdlWindowEvent)
    {
        switch ((WindowEventID)sdlWindowEvent.Event)
        {
            case WindowEventID.FocusGained:
                IsActive = true;
                Logger.Debug("Window focus gained");
                FocusGained.Invoke();
                break;

            case WindowEventID.FocusLost:
                IsActive = false;
                Logger.Debug("Window focus lost");
                FocusLost.Invoke();
                break;

            case WindowEventID.Minimized:
                IsActive = false;
                Logger.Debug("Window minimized");
                Minimized.Invoke();
                break;

            case WindowEventID.Restored:
                IsActive = true;
                Logger.Debug("Window restored from minimized state");
                Restored.Invoke();
                break;

            case WindowEventID.DisplayChanged:
                int oldHz = DisplayHz;
                DisplayHz = getDisplayRefreshRate();

                if (oldHz != DisplayHz)
                {
                    Logger.Debug($"Display refresh rate changed from {oldHz} Hz to {DisplayHz} Hz");
                    DisplayChanged.Invoke(DisplayHz);
                }
                break;

            case WindowEventID.SizeChanged:
            case WindowEventID.Resized:
                int drawableWidth, drawableHeight, windowWidth, windowHeight;
                sdl.GetWindowSize(window, &windowWidth, &windowHeight);
                if (graphicsApi == RendererType.Metal)
                    sdl.GetWindowSizeInPixels(window, &drawableWidth, &drawableHeight);
                else
                    sdl.GLGetDrawableSize(window, &drawableWidth, &drawableHeight);
                // SDL sometimes sends multiple resize events with the same size, ignore them
                // to prevent resize event got invoke multiple times
                if (drawableWidth == currentWidth && drawableHeight == currentHeight && windowWidth == logicalWidth && windowHeight == logicalHeight)
                    break;
                currentWidth = drawableWidth;
                currentHeight = drawableHeight;
                logicalWidth = windowWidth;
                logicalHeight = windowHeight;
                Logger.Verbose($"Window resized to {drawableWidth}x{drawableHeight} (logical size: {windowWidth}x{windowHeight})");

                if (windowConfig != null && windowMode == WindowMode.Windowed)
                {
                    windowConfig.Get<int>(FrameworkSetting.WindowWidth).Value = windowWidth;
                    windowConfig.Get<int>(FrameworkSetting.WindowHeight).Value = windowHeight;
                }

                Resized.Invoke(logicalWidth, logicalHeight);
                break;

            case WindowEventID.Moved:
                if (windowConfig != null && windowMode == WindowMode.Windowed)
                {
                    int posX, posY;
                    sdl.GetWindowPosition(window, &posX, &posY);
                    windowConfig.Get<int>(FrameworkSetting.WindowX).Value = posX;
                    windowConfig.Get<int>(FrameworkSetting.WindowY).Value = posY;
                }
                break;

            case WindowEventID.Enter:
                CursorInWindow = true;
                break;

            case WindowEventID.Leave:
                CursorInWindow = false;
                break;
        }
    }

    private void handleKeyEvent(KeyboardEvent keyboardEvent, Action<KeyEvent> action)
    {
        var key = SDLEnumMapping.ToSakuraKey(keyboardEvent.Keysym.Scancode);
        if (key == Key.Unknown)
            return;

        var modifiers = KeyModifiers.None;
        var modState = sdl.GetModState();

        if ((modState & Keymod.Ctrl) > 0) modifiers |= KeyModifiers.Control;
        if ((modState & Keymod.Shift) > 0) modifiers |= KeyModifiers.Shift;
        if ((modState & Keymod.Alt) > 0) modifiers |= KeyModifiers.Alt;

        bool isRepeat = keyboardEvent.Repeat != 0;

        action.Invoke(new KeyEvent(key, modifiers, isRepeat));
    }

    private void handleMouseButtonEvent(SilkMouseButtonEvent buttonEvent, Action<SakuraMouseButtonEvent> action)
    {
        var button = SDLEnumMapping.ToSakuraMouseButton(buttonEvent.Button);
        if (button == MouseButton.Unknown) return;
        mouseState.Position = new Vector2(buttonEvent.X, buttonEvent.Y);
        mouseState.SetPressed(button, buttonEvent.State == 1);
        action.Invoke(new Input.MouseButtonEvent(mouseState.Clone(), button, buttonEvent.Clicks));
    }

    private void handleMouseMotionEvent(MouseMotionEvent motionEvent)
    {
        mouseState.Position = new Vector2(motionEvent.X, motionEvent.Y);
        var delta = new Vector2(motionEvent.Xrel, motionEvent.Yrel);
        OnMouseMove.Invoke(new MouseEvent(mouseState.Clone(), delta));
    }

    private unsafe void handleMouseWheelEvent(MouseWheelEvent wheelEvent)
    {
        int x = 0;
        int y = 0;
        sdl.GetMouseState(&x, &y);
        mouseState.Position = new Vector2(x, y);
        OnScroll.Invoke(new ScrollEvent(mouseState, new Vector2(wheelEvent.X, wheelEvent.Y)));
    }

    private unsafe void handleDropFileEvent(DropEvent dropEvent)
    {
        string filePath = Marshal.PtrToStringUTF8((nint)dropEvent.File);
        sdl.Free(dropEvent.File);
        if (string.IsNullOrEmpty(filePath))
            return;
        int x, y;
        sdl.GetMouseState(&x, &y);
        Vector2 position = new Vector2(x, y);
        OnDragDropFile.Invoke(new DragDropFileEvent(filePath, position));
    }

    private unsafe void handleDropTextEvent(DropEvent dropEvent)
    {
        string text = Marshal.PtrToStringUTF8((nint)dropEvent.File);
        sdl.Free(dropEvent.File);
        if (string.IsNullOrEmpty(text))
            return;
        int x, y;
        sdl.GetMouseState(&x, &y);
        Vector2 position = new Vector2(x, y);
        OnDragDropText.Invoke(new DragDropTextEvent(text, position));
    }

    private unsafe void handleTextInputEvent(Silk.NET.SDL.TextInputEvent textEvent)
    {
        string text = Marshal.PtrToStringUTF8((nint)textEvent.Text);
        if (!string.IsNullOrEmpty(text))
        {
            OnTextInput.Invoke(new TextInputEvent(text));
        }
    }

    private unsafe void handleTextEditingEvent(Silk.NET.SDL.TextEditingEvent editEvent)
    {
        string text = Marshal.PtrToStringUTF8((nint)editEvent.Text);
        OnTextEditing.Invoke(new TextEditingEvent(text ?? string.Empty, editEvent.Start, editEvent.Length));
    }

    public unsafe string GetClipboardText()
    {
        if (sdl.HasClipboardText() == SdlBool.False) return string.Empty;

        byte* ptr = sdl.GetClipboardText();
        if (ptr == null) return string.Empty;

        try
        {
            return Marshal.PtrToStringUTF8((IntPtr)ptr) ?? string.Empty;
        }
        finally
        {
            sdl.Free(ptr);
        }
    }

    public void SetClipboardText(string text)
    {
        sdl.SetClipboardText(text);
    }

    /// <summary>
    /// Returns true if the given screen position falls within the bounds of any connected display.
    /// Used to detect whether a saved window position is still valid after monitor changes.
    /// </summary>
    private static bool isPositionOnConnectedDisplay(int x, int y)
    {
        int numDisplays = sdl.GetNumVideoDisplays();

        for (int i = 0; i < numDisplays; i++)
        {
            Rectangle<int> bounds = default;
            if (sdl.GetDisplayBounds(i, ref bounds) != 0)
                continue;

            // Check if the position is within this display's bounds.
            // Use a small margin (at least 64px of the title bar must be visible).
            const int margin = 64;
            if (x >= bounds.Origin.X - margin &&
                x < bounds.Origin.X + bounds.Size.X &&
                y >= bounds.Origin.Y - margin &&
                y < bounds.Origin.Y + bounds.Size.Y)
                return true;
        }

        return false;
    }

    private unsafe int getDisplayRefreshRate()
    {
        DisplayMode mode = new DisplayMode();
        int displayIndex = sdl.GetWindowDisplayIndex(window);

        if (sdl.GetCurrentDisplayMode(displayIndex, &mode) == 0)
        {
            return mode.RefreshRate;
        }

        return 60;
    }

    /// <summary>
    /// Process all pending window events.
    /// </summary>
    public unsafe void PollEvents()
    {
        bool? targetState;
        RectangleF? targetRect;

        // drain background thread commands safely
        lock (textInputLock)
        {
            targetState = pendingTextInputState;
            pendingTextInputState = null;

            targetRect = pendingTextInputRect;
            pendingTextInputRect = null;
        }

        // apply text input state switches on the main thread
        if (targetState.HasValue)
        {
            if (targetState.Value)
                sdl.StartTextInput();
            else
                sdl.StopTextInput();
        }

        // apply IME composition box positions on the main thread
        if (targetRect.HasValue)
        {
            var rect = targetRect.Value;
            var sdlRect = new Silk.NET.Maths.Rectangle<int>(
                (int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
            sdl.SetTextInputRect(ref sdlRect);
        }

        // normal OS message processing
        handleSdlEvents();
    }

    public unsafe void SwapBuffers()
    {
        if (graphicsApi == RendererType.Metal)
        {
            // Metal: presentation is handled by the renderer's command buffer commit.
            // Nothing to do at the window layer.
        }
        else
        {
            if (window != null)
                sdl.GLSwapWindow(window);
        }
    }

    /// <summary>
    /// Makes the graphics context current on the calling thread.
    /// For OpenGL: binds the GL context.
    /// For Metal: no-op.
    /// </summary>
    public void MakeCurrent()
    {
        if (graphicsApi != RendererType.Metal)
            glSurface.MakeCurrent();
    }

    /// <summary>
    /// Releases the graphics context from the calling thread.
    /// For OpenGL: unbinds the GL context.
    /// For Metal: no-op.
    /// </summary>
    public void ClearCurrent()
    {
        if (graphicsApi != RendererType.Metal)
            glSurface.ClearCurrent();
    }

    /// <summary>
    /// Enable or disable VSync.
    /// </summary>
    public void SetVSync(bool enabled)
    {
        if (graphicsApi != RendererType.Metal)
            sdl.GLSetSwapInterval(enabled ? 1 : 0);
        // Metal VSync is controlled via CAMetalLayer.displaySyncEnabled, will handled in MetalRenderer.
    }

    private unsafe void setTitle(string newTitle)
    {
        title = newTitle;

        if (window != null)
            sdl.SetWindowTitle(window, newTitle);
    }

    private void setApplicationName(string newAppName)
    {
        applicationName = newAppName;

        var result = sdl?.SetHintWithPriority("SDL_APP_NAME", newAppName, HintPriority.Override);
        if (result == SdlBool.False)
            Logger.Warning("Failed to set SDL application name hint to " + newAppName);
    }

    private unsafe void setResizable(bool isResizable)
    {
        resizable = isResizable;

        if (window != null)
            sdl.SetWindowResizable(window, (SdlBool)(isResizable ? 1 : 0));
    }

    #region Cursor

    private bool cursorVisible = true;
    private readonly Dictionary<CursorState, IntPtr> sdlCursors = new Dictionary<CursorState, IntPtr>();

    public bool CursorVisible
    {
        get => cursorVisible;
        set
        {
            cursorVisible = value;
            if (initialized)
            {
                sdl.ShowCursor(value ? 1 : 0);
            }
        }
    }

    public Reactive<SakuraCursorState> CursorState { get; } = new Reactive<SakuraCursorState>(SakuraCursorState.Default);

    private unsafe void updateSdlCursor()
    {
        if (window == null)
            return;

        var cursorState = CursorState.Value;

        if (!sdlCursors.TryGetValue(cursorState, out IntPtr cursorPtr))
        {
            SystemCursor sysCursor;
            switch (cursorState)
            {
                case SakuraCursorState.Pointer:
                    sysCursor = SystemCursor.SystemCursorHand;
                    break;

                case SakuraCursorState.Text:
                    sysCursor = SystemCursor.SystemCursorIbeam;
                    break;

                case SakuraCursorState.Wait:
                    sysCursor = SystemCursor.SystemCursorWait;
                    break;

                case SakuraCursorState.Crosshair:
                    sysCursor = SystemCursor.SystemCursorCrosshair;
                    break;
                case SakuraCursorState.NotAllowed:
                    sysCursor = SystemCursor.SystemCursorNo;
                    break;

                default:
                    sysCursor = SystemCursor.SystemCursorArrow;
                    break;
            }

            Cursor* newCursor = sdl.CreateSystemCursor(sysCursor);
            cursorPtr = (IntPtr)newCursor;
            sdlCursors[cursorState] = cursorPtr;
        }

        sdl.SetCursor((Cursor*)cursorPtr);
    }

    #endregion

    public unsafe void Dispose()
    {
        foreach (IntPtr cursorPtr in sdlCursors.Values)
        {
            sdl.FreeCursor((Cursor*)cursorPtr);
        }
        sdlCursors.Clear();

        if (glContext != null && graphicsApi != RendererType.Metal)
        {
            sdl.GLDeleteContext(glContext);
            glContext = null;
        }

        if (window != null)
        {
            sdl.DestroyWindow(window);
            window = null;
        }
    }
}
