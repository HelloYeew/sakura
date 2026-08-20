// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// Supplies the fonts to try, in order, for glyphs a primary font is missing — per script, so a
/// family claimed for one writing system is not consulted for another.
/// </summary>
/// <remarks>
/// <see cref="Font.ProcessText"/> takes this rather than a flat font list because it classifies each
/// codepoint as it segments the text, and only the store knows which families were claimed for the
/// resulting script. Implementations are expected to cache each chain, since a chain is asked for on
/// every layout but only walked for a codepoint the primary font lacks.
/// </remarks>
public interface IFontFallbackSource
{
    /// <summary>
    /// The fallback fonts for <paramref name="script"/>, most preferred first.
    /// </summary>
    IEnumerable<Font> GetFallbacks(FontScript script);
}
