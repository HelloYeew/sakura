// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Diagnostics.CodeAnalysis;
using Sakura.Framework.Input;
using Silk.NET.SDL;
using Sakura.Framework.Logging;
using Version = Silk.NET.SDL.Version;

namespace Sakura.Framework.Platform;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SDLWindow : IWindow
{
    private static Sdl sdl;
    private static unsafe void* glContext;
    private static unsafe Window* window;

    private IGraphicsSurface graphicsSurface = new SDLGraphicsSurface();

    private bool initialized;

    private string title = "Window";
    private bool resizable = true;

    private WindowFlags windowFlags = WindowFlags.Opengl | WindowFlags.AllowHighdpi;

    public string Title
    {
        get => title;
        set => setTitle(value);
    }

    public bool Resizable
    {
        get => resizable;
        set => setResizable(value);
    }

    public IGraphicsSurface GraphicsSurface => graphicsSurface;

    public bool IsActive { get; private set; } = true;
    public bool IsExiting { get; private set; }
    public int DisplayHz { get; private set; } = 60;

    // TODO: This update action also no longer needed since it's handled in host's main loop
    public event Action Update = delegate { };
    public event Action Suspended = delegate { };
    public event Action Resumed = delegate { };
    public event Action ExitRequested = delegate { };
    public event Action Exited = delegate { };

    public event Action<KeyEvent> OnKeyDown = delegate { };
    public event Action<KeyEvent> OnKeyUp = delegate { };

    public unsafe void Initialize()
    {
        sdl = Sdl.GetApi();

        Version sdlVersion = new Version();
        sdl.GetVersion(ref sdlVersion);

        byte* sdlRevision = sdl.GetRevision();
        byte* videoDriver = sdl.GetCurrentVideoDriver();

        Logger.Verbose("🪟 SDL initialized");
        Logger.Verbose($"SDL Version: {sdlVersion.Major}.{sdlVersion.Minor}.{sdlVersion.Patch}");
        Logger.Verbose($"SDL Revision: {new string((sbyte*)sdlRevision)}");
        Logger.Verbose($"SDL Video Driver: {new string((sbyte*)videoDriver)}");

        initialized = true;
    }

    public unsafe void Create()
    {
        if (Resizable)
        {
            windowFlags |= WindowFlags.Resizable;
        }

        window = sdl.CreateWindow(
            title,
            Sdl.WindowposCentered, // X position
            Sdl.WindowposCentered, // Y position
            800, // Width
            600, // Height
            (uint)windowFlags
        );

        if (window == null)
        {
            Logger.Error("Failed to create SDL window: " + sdl.GetErrorS());
            throw new Exception("SDL window creation failed.");
        }

        // TODO: This should move to the renderer class but still need to figure out how to pass the context to the renderer
        glContext = sdl.GLCreateContext(window);
        if (glContext == null)
        {
            Logger.Error("Failed to create OpenGL context: " + sdl.GetErrorS());
            throw new Exception("OpenGL context creation failed.");
        }

        sdl.GLMakeCurrent(window, glContext);

        DisplayHz = getDisplayRefreshRate();
        Logger.Verbose($"Display refresh rate: {DisplayHz} Hz");

        Logger.Verbose("SDL window created successfully");

        graphicsSurface.GetFunctionAddress = proc => (nint)sdl.GLGetProcAddress(proc);
    }

    public void Close()
    {
        IsExiting = true;
    }

    public unsafe void Dispose()
    {
        if (glContext != null)
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
            }
        }
    }

    private void handleWindowEvent(WindowEvent sdlWindowEvent)
    {
        switch ((WindowEventID)sdlWindowEvent.Event)
        {
            case WindowEventID.FocusGained:
                IsActive = true;
                Resumed.Invoke();
                break;

            case WindowEventID.FocusLost:
                IsActive = false;
                Suspended.Invoke();
                break;
        }
    }

    private void handleKeyEvent(KeyboardEvent keyboardEvent, Action<KeyEvent> action)
    {
        var key = SDLKeyMapping.ToSakuraKey(keyboardEvent.Keysym.Scancode);
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
    public void PollEvents()
    {
        handleSdlEvents();
    }

    public void SwapBuffers()
    {
        swapBuffers();
    }

    /// <summary>
    /// Swap the front and back buffers to present the rendered frame.
    /// </summary>
    private unsafe void swapBuffers()
    {
        if (window != null)
            sdl.GLSwapWindow(window);
    }

    /// <summary>
    /// Enable or disable VSync.
    /// </summary>
    /// <param name="enabled">True to enable VSync, false to disable.</param>
    public void SetVSync(bool enabled)
    {
        sdl.GLSetSwapInterval(enabled ? 1 : 0);
    }

    private unsafe void setTitle(string newTitle)
    {
        title = newTitle;

        if (window != null)
            sdl.SetWindowTitle(window, newTitle);
    }

    private unsafe void setResizable(bool isResizable)
    {
        resizable = isResizable;

        if (window != null)
            sdl.SetWindowResizable(window, (SdlBool)(isResizable ? 1 : 0));
    }
}
