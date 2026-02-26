// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// Provides access to fonts and manages their lifecycle.
/// </summary>
public interface IFontStore : IDisposable
{
    /// <summary>
    /// Loads the default framework font (e.g. Inter).
    /// </summary>
    void LoadDefaultFont(Storage resourceStorage);

    /// <summary>
    /// Adds a font to the store from a specific storage.
    /// </summary>
    /// <param name="storage">The storage containing the font file.</param>
    /// <param name="filename">The filename of the font.</param>
    /// <param name="alias">Optional alias to refer to this font. If null, uses filename without extension.</param>
    void AddFont(Storage storage, string filename, string alias = null);

    /// <summary>
    /// Retrieves a font matching the specified usage.
    /// </summary>
    Font Get(FontUsage usage);

    /// <summary>
    /// Retrieves a font by direct name.
    /// </summary>
    Font Get(string name);

    TextureAtlas Atlas { get; }
}
