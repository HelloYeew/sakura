// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sakura.Framework.Platform.Dialogs;
using SDL;
using static SDL.SDL3;

namespace Sakura.Framework.Platform;

/// <summary>
/// Native file dialog interop backed by SDL3. All calls must be made on the thread that owns the
/// SDL event loop (the OS main thread). the result callback is likewise invoked on that thread
/// while SDL pumps events. Higher layers (<see cref="AppHost"/>) are responsible for marshaling
/// onto the appropriate thread.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
internal static unsafe class SDLFileDialog
{
    private enum DialogKind
    {
        OpenFile,
        SaveFile,
        OpenFolder,
    }

    public static void ShowOpenFile(SDL_Window* window, FileDialogOptions options, Action<FileDialogResult> callback)
        => show(DialogKind.OpenFile, window, options, callback);

    public static void ShowSaveFile(SDL_Window* window, FileDialogOptions options, Action<FileDialogResult> callback)
        => show(DialogKind.SaveFile, window, options, callback);

    public static void ShowOpenFolder(SDL_Window* window, FileDialogOptions options, Action<FileDialogResult> callback)
        => show(DialogKind.OpenFolder, window, options, callback);

    private static void show(DialogKind kind, SDL_Window* window, FileDialogOptions? options, Action<FileDialogResult> callback)
    {
        options ??= new FileDialogOptions();

        // The dialog is asynchronous: SDL invokes the callback later, during event pumping. Any
        // native memory we hand it (filters, strings) must stay alive until then, so we bundle it
        // into a context kept alive by a GCHandle passed as userdata, and free it in the callback.
        var context = new DialogContext(callback);

        try
        {
            byte* location = context.AllocUtf8(options.DefaultLocation);

            SDL_DialogFileFilter* filters = null;
            int filterCount = 0;

            if (kind != DialogKind.OpenFolder && options.Filters is { Length: > 0 })
            {
                filterCount = options.Filters.Length;
                filters = (SDL_DialogFileFilter*)context.Alloc((nuint)(sizeof(SDL_DialogFileFilter) * filterCount));

                for (int i = 0; i < filterCount; i++)
                {
                    filters[i].name = context.AllocUtf8(options.Filters[i].Name);
                    filters[i].pattern = context.AllocUtf8(options.Filters[i].ToPattern());
                }
            }

            IntPtr userdata = GCHandle.ToIntPtr(context.Handle);

            switch (kind)
            {
                case DialogKind.OpenFile:
                    SDL_ShowOpenFileDialog(&onResult, userdata, window, filters, filterCount, location, options.AllowMultiple);
                    break;

                case DialogKind.SaveFile:
                    SDL_ShowSaveFileDialog(&onResult, userdata, window, filters, filterCount, location);
                    break;

                case DialogKind.OpenFolder:
                    SDL_ShowOpenFolderDialog(&onResult, userdata, window, location, options.AllowMultiple);
                    break;
            }
        }
        catch
        {
            context.Free();
            throw;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void onResult(IntPtr userdata, byte** filelist, int filter)
    {
        var handle = GCHandle.FromIntPtr(userdata);
        var context = (DialogContext)handle.Target!;

        FileDialogResult result;

        // filelist == null -> an error occurred, *filelist == null (empty list) ->
        // the user cancelled, otherwise a null-terminated array of UTF-8 path strings.
        if (filelist == null)
        {
            result = FileDialogResult.Failed(SDL_GetError() ?? "Unknown file dialog error");
        }
        else
        {
            var paths = new List<string>();

            for (byte** entry = filelist; *entry != null; entry++)
            {
                string? path = Marshal.PtrToStringUTF8((nint)(*entry));
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            result = FileDialogResult.FromPaths(paths);
        }

        try
        {
            context.Invoke(result);
        }
        finally
        {
            context.Free();
        }
    }

    /// <summary>
    /// Owns the managed callback and all native allocations for one dialog invocation, keeping
    /// them alive across the asynchronous SDL call.
    /// </summary>
    private sealed class DialogContext
    {
        public GCHandle Handle;

        private readonly Action<FileDialogResult> callback;

        // Kept separate so each block is released with the matching free function: AllocHGlobal
        // pairs with FreeHGlobal, StringToCoTaskMemUTF8 with FreeCoTaskMem. These are not
        // interchangeable on Windows.
        private readonly List<IntPtr> hGlobalAllocations = new List<IntPtr>();
        private readonly List<IntPtr> coTaskAllocations = new List<IntPtr>();
        private bool freed;

        public DialogContext(Action<FileDialogResult>? callback)
        {
            this.callback = callback ?? (static _ => { });
            Handle = GCHandle.Alloc(this);
        }

        public IntPtr Alloc(nuint bytes)
        {
            IntPtr ptr = Marshal.AllocHGlobal((nint)bytes);
            hGlobalAllocations.Add(ptr);
            return ptr;
        }

        public byte* AllocUtf8(string? value)
        {
            if (value == null)
                return null;

            IntPtr ptr = Marshal.StringToCoTaskMemUTF8(value);
            coTaskAllocations.Add(ptr);
            return (byte*)ptr;
        }

        public void Invoke(FileDialogResult result) => callback(result);

        public void Free()
        {
            if (freed)
                return;

            freed = true;

            foreach (IntPtr ptr in hGlobalAllocations)
                Marshal.FreeHGlobal(ptr);

            foreach (IntPtr ptr in coTaskAllocations)
                Marshal.FreeCoTaskMem(ptr);

            hGlobalAllocations.Clear();
            coTaskAllocations.Clear();

            if (Handle.IsAllocated)
                Handle.Free();
        }
    }
}
