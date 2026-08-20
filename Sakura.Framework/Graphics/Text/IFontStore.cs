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
    /// <param name="script">
    /// The writing system this family should serve when another font is missing a glyph — the family a
    /// Japanese label falls back to, for instance. Claiming a script does not restrict the family: it
    /// stays usable as a primary font for any text. <see cref="FontScript.Auto"/> derives the claims
    /// from what the font covers; null (the default) registers the family without claiming anything, so
    /// only a <see cref="FontUsage"/> naming it directly will reach it.
    /// </param>
    void AddFontFamily(Storage storage, string family, bool hasItalics = false, FontScript? script = null);

    /// <summary>
    /// Adds a font family to be used as a fallback for every script, after any family claimed for the
    /// specific script being resolved and before the framework's own bundled families.
    /// </summary>
    void AddFallbackFamily(string familyName);

    /// <summary>
    /// Claims <paramref name="script"/> for an already-registered family, so glyphs belonging to that
    /// script resolve here before reaching the framework's bundled families. A family may hold several
    /// claims. Pass <see cref="FontScript.Auto"/> to derive them from the font's own coverage.
    /// </summary>
    void AddFallbackFamily(string familyName, FontScript script);

    /// <summary>
    /// Claims <paramref name="script"/> for a family ahead of every existing claim, so the last caller
    /// wins. Use it to override a claim made elsewhere; <see cref="AddFallbackFamily(string,FontScript)"/>
    /// is the normal way to make one.
    /// </summary>
    void SetScriptFamily(FontScript script, string familyName);

    /// <summary>
    /// Inserts a script-agnostic fallback family at a specific position among the application's own
    /// fallbacks.
    /// </summary>
    /// <remarks>
    /// Priority is decided by script claim first and position second (see
    /// <see cref="AddFallbackFamily(string,FontScript)"/>), so this only breaks ties between families
    /// registered for no particular script. It cannot be used to overtake a claim.
    /// </remarks>
    void InsertFallbackFamily(int index, string familyName);

    /// <summary>
    /// Which language's forms to prefer for unified CJK ideographs, which no codepoint can attribute to
    /// a language on its own. Defaults to the OS UI language, and is consulted only after the
    /// application's own CJK claims, so an application shipping a single CJK family never needs to set
    /// it. A single label can override it through <see cref="FontUsage.Script"/>.
    /// </summary>
    FontScript HanScript { get; set; }

    /// <summary>
    /// Clears all currently registered fallback families.
    /// </summary>
    void ClearFallbackFamilies();

    /// <summary>
    /// Retrieves all registered fallback fonts configured for the requested usage (Weight/Italics),
    /// ordered for the script the usage names, or script-agnostically when it names none.
    /// </summary>
    IEnumerable<Font> GetFallbacks(FontUsage usage);

    /// <summary>
    /// Retrieves the fallback fonts for the requested usage, ordered for glyphs belonging to
    /// <paramref name="script"/>.
    /// </summary>
    IEnumerable<Font> GetFallbacks(FontUsage usage, FontScript script);

    /// <summary>
    /// Retrieves the per-script fallback chains for the requested usage, as
    /// <see cref="Font.ProcessText"/> consumes them while segmenting text.
    /// </summary>
    IFontFallbackSource GetFallbackSource(FontUsage usage);

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
    /// Shapes <paramref name="text"/> for the given usage and pixel scale, reusing an existing result
    /// when one matches.
    /// </summary>
    ShapedText Shape(FontUsage usage, string text, float dpiScale);

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
