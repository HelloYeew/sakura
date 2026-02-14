// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.IO;
using Sakura.Framework.Logging;

namespace Sakura.Framework.Platform;

/// <summary>
/// A <see cref="NativeStorage"/> that cleans up its contents upon disposal.
/// </summary>
public class TemporaryStorage : NativeStorage, IDisposable
{
    public TemporaryStorage(string path, AppHost host = null) : base(path, host)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            Logger.Verbose($"Created temporary storage directory at {path}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (ExistsDirectory(string.Empty))
            {
                DeleteDirectory(string.Empty);
                Logger.Verbose($"Cleaned up temporary storage at {BasePath}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to clean up temporary storage at {BasePath}", ex);
        }
    }
}
