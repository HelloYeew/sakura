// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Textures;
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
    /// Adds a single font file to the store under one lookup key. This is a low-level primitive: it does
    /// not build the <c>{family}-{weight}</c> keys that <see cref="Get(FontUsage)"/> resolves, and it does
    /// not expand a variable font into its weights. For loading an application/game font family, prefer
    /// <see cref="AddFontFamily"/>, which handles both variable and static families and registers every
    /// weight/italic key for you. Use <see cref="AddFont"/> directly only when you need manual control over
    /// a specific key (e.g. a single-weight icon font addressed by codepoint/ligature).
    /// </summary>
    /// <param name="storage">The storage containing the font file.</param>
    /// <param name="filename">The filename of the font.</param>
    /// <param name="alias">Optional alias to refer to this font. If null, uses filename without extension.</param>
    void AddFont(Storage storage, string filename, string alias = null);

    /// <summary>
    /// Adds a whole font family, preferring a single OpenType variable file
    /// (<c>{family}-VF.ttf</c> / <c>{family}[wght].ttf</c>) when present and otherwise falling back to
    /// per-weight static files (<c>{family}-{weight}.ttf</c>). Every <c>{family}-{weight}</c> key and the
    /// bare family name are registered, so <see cref="Get(FontUsage)"/> resolves any requested weight.
    /// Prefer this over <see cref="AddFont"/> for game/application fonts — a raw <c>AddFont</c> only
    /// registers a single filename key and will not resolve variable-font weights.
    /// </summary>
    /// <param name="storage">The storage containing the font file(s).</param>
    /// <param name="family">The family name (e.g. "Nunito"), used to locate files and build lookup keys.</param>
    /// <param name="hasItalics">Whether to also load the italic variant of the family.</param>
    void AddFontFamily(Storage storage, string family, bool hasItalics = false);

    /// <summary>
    /// Adds a font family to be used as a fallback.
    /// </summary>
    void AddFallbackFamily(string familyName);

    /// <summary>
    /// Inserts a fallback family at a specific priority level.
    /// </summary>
    void InsertFallbackFamily(int index, string familyName);

    /// <summary>
    /// Clears all currently registered fallback families.
    /// </summary>
    void ClearFallbackFamilies();

    /// <summary>
    /// Retrieves all registered fallback fonts configured for the requested usage (Weight/Italics).
    /// </summary>
    IEnumerable<Font> GetFallbacks(FontUsage usage);

    /// <summary>
    /// Retrieves a font matching the specified usage.
    /// </summary>
    Font Get(FontUsage usage);

    /// <summary>
    /// Retrieves a font by direct name.
    /// </summary>
    Font Get(string name);

    /// <summary>
    /// Retrieves the <see cref="FontVariation"/> (variable-font axis coordinates) that should be
    /// applied for the requested usage. Static fonts ignore it; variable fonts render the matching
    /// weight/fill/optical-size instance.
    /// </summary>
    FontVariation GetVariation(FontUsage usage);

    /// <summary>
    /// A version number that increments whenever the font store's cache is updated.
    /// Will increment mostly when <see cref="ClearCaches"/> is called.
    /// </summary>
    int CacheVersion { get; }

    /// <summary>
    /// Clear internal caches of the font.
    /// </summary>
    void ClearCaches();

    TextureAtlas Atlas { get; }
}
