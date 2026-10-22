// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.IO;

namespace Sakura.Framework.Platform;

/// <summary>
/// A <see cref="FileStream"/> that writes to a temporary file first, and then moves it to the final destination.
/// </summary>
public class SafeWriteStream : FileStream
{
    private readonly string temporaryPath;
    private readonly string finalPath;
    private readonly Storage storage;

    public SafeWriteStream(string temporaryPath, string finalPath, Storage storage) : base(
        storage.GetFullPath(temporaryPath, true), FileMode.Create, FileAccess.Write)
    {
        this.temporaryPath = temporaryPath;
        this.finalPath = finalPath;
        this.storage = storage;
    }

    private bool isDisposed;

    protected override void Dispose(bool disposing)
    {
        // Don't perform any custom logic when arriving via the finaliser.
        // We assume that all usages of `SafeWriteStream` correctly follow a local disposal pattern.
        if (!disposing)
        {
            base.Dispose(false);
            return;
        }

        if (!isDisposed)
        {
            // this was added to work around some hardware writing zeroes to a file
            // before writing actual content, causing corrupt files to exist on disk.
            // see: https://github.com/ppy/osu-framework/issues/5231
            // Down to the disk before the rename, too, so a phone losing power just after can't be left with the new name
            // on an empty file. Not on Apple's platforms, where .NET's flush to the disk is very expensive.
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsIOS())
            {
                try
                {
                    Flush(true);
                }
                catch
                {
                    // this may fail due to a lower level file access issue.
                    // we don't want to throw in disposal though.
                }
            }
        }

        base.Dispose(true);

        if (!isDisposed)
        {
            // In one step, over the old file: deleting it first left a moment with no file at all, and a process ended
            // in it lost the file.
            storage.Move(temporaryPath, finalPath);

            isDisposed = true;
        }
    }
}
