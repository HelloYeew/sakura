// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics.CodeAnalysis;

namespace Sakura.Framework.Platform;

public enum GraphicsSurfaceType
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    OpenGL,
    Metal,
}
