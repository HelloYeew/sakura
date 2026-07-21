// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;

namespace Sakura.Framework.Platform.Dialogs;

/// <summary>
/// A single entry in a file dialog's type filter, e.g. "Images (*.png, *.jpg)"
/// </summary>
public readonly struct FileDialogFilter
{
    /// <summary>
    /// The human-readable name of this filter shown to the user (e.g. "Images").
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// The file extensions this filter matches, without a leading dot (e.g. "png", "jpg").
    /// A single entry of "*" matches every file.
    /// </summary>
    public readonly string[] Extensions;

    /// <summary>
    /// Creates a filter matching the given <paramref name="extensions"/>.
    /// </summary>
    /// <param name="name">The human-readable name shown to the user.</param>
    /// <param name="extensions">
    /// One or more extensions without a leading dot (a leading dot is stripped if present).
    /// Use "*" to match every file.
    /// </param>
    public FileDialogFilter(string? name, params string[]? extensions)
    {
        Name = name ?? string.Empty;
        Extensions = extensions ?? Array.Empty<string>();
    }

    /// <summary>
    /// A filter that matches every file.
    /// </summary>
    public static FileDialogFilter AllFiles(string name = "All files") => new FileDialogFilter(name, "*");

    /// <summary>
    /// Builds the SDL pattern string for this filter: extensions joined by ';', with any leading
    /// dots and surrounding whitespace stripped, and empty entries dropped (e.g. "png;jpg").
    /// </summary>
    public string ToPattern()
    {
        var cleaned = Extensions
                      .Where(e => !string.IsNullOrWhiteSpace(e))
                      .Select(e => e.Trim().TrimStart('.'))
                      .Where(e => e.Length > 0);

        return string.Join(";", cleaned);
    }
}
