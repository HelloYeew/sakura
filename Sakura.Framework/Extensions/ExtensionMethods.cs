// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics;
using System.IO;

namespace Sakura.Framework.Extensions;

public static class ExtensionMethods
{
    /// <summary>
    /// Trim DirectorySeparatorChar from the end of the path.
    /// </summary>
    /// <remarks>
    /// Trims both <see cref="Path.DirectorySeparatorChar"/> and <see cref="Path.AltDirectorySeparatorChar"/>.
    /// </remarks>
    /// <param name="path">The path string to trim.</param>
    /// <returns>The path with DirectorySeparatorChar trimmed.</returns>
    public static string TrimDirectorySeparator(this string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Checks whether the provided URL is a safe protocol to execute a system <see cref="Process.Start()"/> call with.
    /// </summary>
    /// <remarks>
    /// For now, http://, https:// and mailto: are supported.
    /// More protocols can be added if a use case comes up.
    /// </remarks>
    /// <param name="url">The URL to check.</param>
    /// <returns>Whether the URL is safe to open.</returns>
    public static bool CheckIsValidUrl(this string url)
    {
        return url.StartsWith("https://", StringComparison.Ordinal)
               || url.StartsWith("http://", StringComparison.Ordinal)
               || url.StartsWith("mailto:", StringComparison.Ordinal);
    }
}
