// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Platform;

public class HostOptions
{
    /// <summary>
    /// THe friendly name of the application that will use to display in the title or as a display name.
    /// </summary>
    /// <remarks>
    /// If empty, the framework will use a default name based on the name in the app name.
    /// </remarks>
    public string FriendlyAppName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a portable installation. Will cause all application files to be placed alongside the executable, rather than in the standard data directory.
    /// </summary>
    public bool PortableInstallation { get; set; }
}
