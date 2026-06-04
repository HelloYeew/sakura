// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using Sakura.Framework.Graphics.Rendering;

namespace Sakura.Framework.Platform;

/// <summary>
/// Extended surface contract for OpenGL rendering.
/// <see cref="GLRenderer"/> casts
/// <see cref="IGraphicsSurface"/> to this interface during <c>Initialize</c>.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public interface IOpenGLGraphicsSurface : IGraphicsSurface
{
    /// <summary>
    /// Returns the address of an OpenGL function by name.
    /// Backed by <c>SDL_GL_GetProcAddress</c> on SDL platforms.
    /// </summary>
    Func<string, nint> GetFunctionAddress { get; }

    /// <summary>
    /// Makes the OpenGL context current on the calling thread.
    /// </summary>
    Action MakeCurrent { get; }

    /// <summary>
    /// Releases the OpenGL context from the calling thread so another thread can claim it.
    /// </summary>
    Action ClearCurrent { get; }
}
