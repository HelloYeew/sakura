// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Platform;

public enum RendererType
{
    Automatic,
    Metal,
    Direct3D11,

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    OpenGL
}

/// <summary>
/// Helpers for determining which <see cref="RendererType"/> values make sense on the current platform.
/// </summary>
public static class RendererTypes
{
    /// <summary>
    /// The platform-native renderer types in fallback priority order (most preferred first),
    /// excluding <see cref="RendererType.Automatic"/>. <see cref="RendererType.OpenGL"/> is always the
    /// universal fallback, so it is the last entry on every platform. This is the order the automatic
    /// selection walks when picking a backend.
    /// </summary>
    public static IReadOnlyList<RendererType> GetPlatformRenderers()
    {
        if (RuntimeInfo.IsApple)
            return [RendererType.Metal, RendererType.OpenGL];

        if (RuntimeInfo.IsWindows)
            return [RendererType.Direct3D11, RendererType.OpenGL];

        return [RendererType.OpenGL];
    }

    /// <summary>
    /// The renderer types a user can select on the current platform, in display order:
    /// <see cref="RendererType.Automatic"/> first, followed by each platform-native backend from
    /// <see cref="GetPlatformRenderers"/>. Suitable for populating a settings dropdown — e.g. on macOS
    /// this returns <c>[Automatic, Metal, OpenGL]</c>.
    /// </summary>
    public static IReadOnlyList<RendererType> GetSuitableRenderers()
    {
        var result = new List<RendererType> { RendererType.Automatic };
        result.AddRange(GetPlatformRenderers());
        return result;
    }
}
