// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.IO;

namespace Sakura.Framework.Platform;

/// <summary>
/// A <see cref="FileStream"/> that always flushes the stream to disk when disposed.
/// </summary>
/// <remarks>
/// This adds a considerable overhead, but required to avoid files being potentially written to disk in a corrupted state.
/// See https://stackoverflow.com/questions/49260358/what-could-cause-an-xml-file-to-be-filled-with-null-characters/52751216#52751216.
/// </remarks>
public class FlushingStream : FileStream
{
    public FlushingStream(string path, FileMode mode, FileAccess access)
        : base(path, mode, access)
    {
    }

    private bool finalFlushRun;

    protected override void Dispose(bool disposing)
    {
        // Dispose maybe called multiple times, so we need to ensure that we only run the final flush once.
        if (!finalFlushRun)
        {
            finalFlushRun = true;

            try
            {
                Flush(true);
            }
            catch
            {
                // On some platform may fail due to a lower level file access issue.
                // Just ignore the exception here as we don't want to throw in disposal.
            }
        }

        base.Dispose(disposing);
    }
}
