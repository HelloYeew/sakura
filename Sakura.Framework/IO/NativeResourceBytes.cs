// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.IO;

namespace Sakura.Framework.IO;

/// <summary>
/// An embedded resource's bytes where they already are, in the loaded assembly, rather than a copy of them. A loaded
/// assembly stays where it is for the process, so the pointer stays valid; on Android, with assembly compression off,
/// that's the APK mapped from storage, which the system can drop from memory and read back as it likes.
/// </summary>
public sealed unsafe class NativeResourceBytes : INativeBytes
{
    private readonly UnmanagedMemoryStream stream;

    public IntPtr Pointer { get; private set; }

    public long Length { get; private set; }

    private NativeResourceBytes(UnmanagedMemoryStream stream)
    {
        this.stream = stream;
        Pointer = (IntPtr)stream.PositionPointer;
        Length = stream.Length - stream.Position;
    }

    /// <summary>
    /// The bytes of <paramref name="stream"/> from where it is, without a copy, if it reads straight out of memory, as
    /// <see cref="System.Reflection.Assembly.GetManifestResourceStream(string)"/> does; null for any other stream.
    /// Takes the stream over when it does.
    /// </summary>
    public static NativeResourceBytes? TryFrom(Stream stream)
        => stream is UnmanagedMemoryStream memory && memory.Length - memory.Position > 0 ? new NativeResourceBytes(memory) : null;

    public void Dispose()
    {
        Pointer = IntPtr.Zero;
        Length = 0;
        stream.Dispose();
    }
}
