// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;

namespace Sakura.Framework.Platform.Dialogs;

/// <summary>
/// The outcome of a native file dialog
/// </summary>
public readonly struct FileDialogResult
{
    /// <summary>
    /// Whether the user picked at least one path. <c>false</c> when the user canceled the dialog
    /// or the platform reported an error (see <see cref="Error"/>).
    /// </summary>
    public bool Successful => Paths.Count > 0;

    /// <summary>
    /// The selected paths. Empty when the dialog was canceled or failed. For save and
    /// single-select dialogs this contains at most one entry.
    /// </summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>
    /// The platform error message when the dialog failed, or null when it was canceled or succeeded.
    /// A non-null value distinguishes a failure from a user cancellation (both have no paths).
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// The first selected path, or null when nothing was selected. Convenient for single-select
    /// and save dialogs.
    /// </summary>
    public string? Path => Paths.Count > 0 ? Paths[0] : null;

    private FileDialogResult(IReadOnlyList<string>? paths, string? error)
    {
        Paths = paths ?? Array.Empty<string>();
        Error = error;
    }

    /// <summary>
    /// A cancelled result (no paths, no error).
    /// </summary>
    public static FileDialogResult Cancelled { get; } = new FileDialogResult(Array.Empty<string>(), null);

    /// <summary>
    /// Creates a successful result from the given selected paths. An empty list is treated as a
    /// cancellation.
    /// </summary>
    public static FileDialogResult FromPaths(IReadOnlyList<string> paths) => new FileDialogResult(paths, null);

    /// <summary>
    /// Creates a failed result carrying a platform error message.
    /// </summary>
    public static FileDialogResult Failed(string error) => new FileDialogResult(Array.Empty<string>(), error);
}
