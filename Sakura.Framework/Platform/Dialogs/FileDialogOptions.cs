// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Platform.Dialogs;

/// <summary>
/// Configuration for a native file dialog
/// </summary>
public class FileDialogOptions
{
    /// <summary>
    /// The title shown on the dialog window. When null the platform default is used.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The initial directory (or file) the dialog opens at. When null the platform decides
    /// (usually the last-used location). May be an absolute path to a directory or a file.
    /// </summary>
    public string? DefaultLocation { get; set; }

    /// <summary>
    /// The type filters offered to the user. Ignored by folder dialogs.
    /// When null or empty, all files are shown.
    /// </summary>
    public FileDialogFilter[] Filters { get; set; } = Array.Empty<FileDialogFilter>();

    /// <summary>
    /// Whether the user may select more than one item. Only honored by open-file and
    /// open-folder dialogs; save dialogs always return a single path.
    /// </summary>
    public bool AllowMultiple { get; set; }
}
