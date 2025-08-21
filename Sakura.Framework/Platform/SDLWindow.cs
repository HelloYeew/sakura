// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Diagnostics.CodeAnalysis;
using Silk.NET.OpenGL;
using Silk.NET.SDL;
using Sakura.Framework.Logging;
using Version = Silk.NET.SDL.Version;

namespace Sakura.Framework.Platform;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SDLWindow : IWindow
{
    // TODO: OpenGL need to be moved to a separate class i.e. IRenderer since we want to support other renderers in the future (Vulkan, DirectX, etc.)
    // All rendering code need to be moved to the separate class as well.
    private static Sdl sdl;
    private static GL gl;
    private static unsafe void* glContext;
    private static unsafe Window* window;

    private IGraphicsSurface graphicsSurface = new SDLGraphicsSurface();

    private bool initialized;
    private bool running;

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

    public event Action Update = delegate { };
    public event Action Suspended = delegate { };
    public event Action Resumed = delegate { };
    public event Action ExitRequested = delegate { };
    public event Action Exited = delegate { };

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

        glContext = sdl.GLCreateContext(window);
        if (glContext == null)
        {
            Logger.Error("Failed to create OpenGL context: " + sdl.GetErrorS());
            throw new Exception("OpenGL context creation failed.");
        }

        sdl.GLMakeCurrent(window, glContext);
        sdl.GLSetSwapInterval(1); // Enable VSync

        Logger.Verbose("SDL window created successfully");

        // TODO: Remove this line after the renderer is moved to a separate class
        gl = GL.GetApi(proc => (nint)sdl.GLGetProcAddress(proc));
        graphicsSurface.GetFunctionAddress = proc => (nint)sdl.GLGetProcAddress(proc);

        running = true;
    }

    public void Run()
    {
        if (!initialized)
            throw new InvalidOperationException("Window must be initialized before running.");

        while (running)
        {
            runFrame();
            // TODO: Remove this to IRenderer class
            render();
        }
    }

    private void runFrame()
    {
        if (!running)
            return;

        handleSdlEvents();
        Update.Invoke();
    }

    public void Close()
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
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
                    running = false;
                    break;

                case EventType.Windowevent:
                    // TODO: Handle window events like resize, minimize, etc.
                    break;

                case EventType.Keydown:
                    // TODO: Handle key down events
                    break;
            }
        }
    }

    private unsafe void render()
    {
        if (window == null)
            return;

        gl.Clear((uint)ClearBufferMask.ColorBufferBit);


        sdl.GLSwapWindow(window);
    }

    private unsafe void setTitle(string newTitle)
    {
        title = newTitle;

        if (window == null)
            return;

        sdl.SetWindowTitle(window, newTitle);
    }

    private unsafe void setResizable(bool isResizable)
    {
        resizable = isResizable;

        if (window == null)
            return;

        if (isResizable)
            windowFlags |= WindowFlags.Resizable;
        else
            windowFlags &= ~WindowFlags.Resizable;

        sdl.SetWindowResizable(window, (SdlBool)(isResizable ? 1 : 0));
    }
}
