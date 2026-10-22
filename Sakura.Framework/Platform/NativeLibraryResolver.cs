// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sakura.Framework.Platform;

/// <summary>
/// The framework assembly's one DllImport resolver: the runtime takes a single resolver per assembly, so every native
/// library the framework needs moved goes through here, by name. A name not listed is left to the runtime's own search.
/// </summary>
internal static class NativeLibraryResolver
{
    /// <summary>
    /// iOS embeds each of the framework's own libraries as a framework under <c>@rpath</c>, where nothing looks for
    /// it by name.
    /// </summary>
    private static readonly Dictionary<string, string> ios_frameworks = new Dictionary<string, string>
    {
        ["sakura-audio"] = "@rpath/sakura-audio.framework/sakura-audio",
        ["sakura-image"] = "@rpath/sakura-image.framework/sakura-image",
        ["libsakura-metal"] = "@rpath/sakura-metal.framework/sakura-metal",
    };

    private static int installed;

    /// <summary>
    /// Installs the resolver where a platform needs one, once. Called before the first call into each library, so
    /// any of them can be first.
    /// </summary>
    public static void Install()
    {
        if (!OperatingSystem.IsIOS() || Interlocked.Exchange(ref installed, 1) != 0)
            return;

        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, resolve);
    }

    private static IntPtr resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (ios_frameworks.TryGetValue(name, out string? framework) && NativeLibrary.TryLoad(framework, assembly, searchPath, out IntPtr handle))
            return handle;

        return IntPtr.Zero;
    }
}
