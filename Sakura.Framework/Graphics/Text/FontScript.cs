// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// A writing system a font family can be registered for, so the fallback chain can prefer a
/// different family per script rather than "whichever registered font happens to have the glyph".
/// </summary>
/// <remarks>
/// Registering a family for a script is a "claim", not a restriction. It only affects the order
/// of the fallback chain walked for glyphs the primary font is missing. A family remains usable as a
/// primary font for any text, so a family claimed for <see cref="Japanese"/> still renders Latin when
/// a <see cref="FontUsage"/> asks for it by name.
/// </remarks>
public enum FontScript
{
    /// <summary>
    /// Not script-specific. As a claim, the family applies to every script (after any claim made for
    /// the specific script being resolved); as a classification, the script could not be determined.
    /// </summary>
    Any,

    Latin,
    Cyrillic,
    Greek,

    /// <summary>
    /// Unified CJK ideographs, whose language cannot be determined from the codepoint alone — 漢 is
    /// the same character in Japanese, Korean and both Chinese variants while being drawn differently
    /// in each. Resolved through the app's CJK claims and <see cref="IFontStore.HanScript"/>; a single
    /// label can override it with <see cref="FontUsage.Script"/>.
    /// </summary>
    Han,

    /// <summary>
    /// Hiragana and katakana. Kanji in the same text classify as <see cref="Han"/>, so a family
    /// claiming <see cref="Japanese"/> is also consulted for <see cref="Han"/>.
    /// </summary>
    Japanese,

    /// <summary>
    /// Hangul.
    /// </summary>
    Korean,

    ChineseSimplified,
    ChineseTraditional,
    Thai,
    Arabic,
    Hebrew,
    Devanagari,

    /// <summary>
    /// Pictographs and emoji. Classified from codepoint ranges rather than the Unicode script
    /// property, which reports most emoji as <c>Common</c>.
    /// </summary>
    Emoji,

    /// <summary>
    /// Registration-time sentinel: claim every non-Latin script the family actually covers that no
    /// earlier claim has taken. Only meaningful as an argument to
    /// <see cref="IFontStore.AddFontFamily"/> / <see cref="IFontStore.AddFallbackFamily"/>; never
    /// returned by classification, and treated as <see cref="Any"/> if it reaches a chain lookup.
    /// </summary>
    Auto
}

/// <summary>
/// Classification of codepoints into <see cref="FontScript"/>, and the script relationships the
/// fallback chain and run segmentation are built on.
/// </summary>
public static class FontScripts
{
    /// <summary>
    /// The CJK scripts, in the order the framework's own families were historically registered.
    /// A claim for any of these is consulted when resolving <see cref="FontScript.Han"/>.
    /// </summary>
    public static readonly FontScript[] CJK_SCRIPTS =
    {
        FontScript.Han,
        FontScript.ChineseSimplified,
        FontScript.ChineseTraditional,
        FontScript.Japanese,
        FontScript.Korean
    };

    /// <summary>
    /// One codepoint that any family genuinely covering the script must have. Used to verify a claim
    /// (and to derive claims for <see cref="FontScript.Auto"/>) with a single <c>FT_Get_Char_Index</c>
    /// rather than by walking the whole character map.
    /// </summary>
    /// <remarks>
    /// <see cref="FontScript.Any"/> and <see cref="FontScript.Emoji"/> are deliberately absent: the
    /// former makes no coverage promise, and emoji coverage is too fragmented across fonts for one
    /// codepoint to stand in for it.
    /// </remarks>
    private static readonly (FontScript Script, uint Probe)[] script_probes =
    {
        (FontScript.Latin, 'A'),
        (FontScript.Cyrillic, 'А'), // U+0410 CYRILLIC CAPITAL LETTER A
        (FontScript.Greek, 'α'),
        (FontScript.Han, '漢'),
        (FontScript.ChineseSimplified, '汉'),
        (FontScript.ChineseTraditional, '漢'),
        (FontScript.Japanese, 'あ'),
        (FontScript.Korean, '한'),
        (FontScript.Thai, 'ก'),
        (FontScript.Arabic, 'ا'),
        (FontScript.Hebrew, 'א'),
        (FontScript.Devanagari, 'अ')
    };

    /// <summary>
    /// The scripts <see cref="FontScript.Auto"/> considers, in claim order. Latin is excluded on
    /// purpose: nearly every font covers it, so auto-claiming it would let a Japanese or Thai family
    /// supply Latin glyphs for a label that asked for another family, which is the over-claiming this
    /// whole mechanism exists to avoid.
    /// </summary>
    private static readonly FontScript[] auto_claimable =
    {
        FontScript.Japanese,
        FontScript.Korean,
        FontScript.Han,
        FontScript.Thai,
        FontScript.Arabic,
        FontScript.Hebrew,
        FontScript.Devanagari,
        FontScript.Cyrillic,
        FontScript.Greek
    };

    public static IReadOnlyList<FontScript> AutoClaimable => auto_claimable;

    /// <summary>
    /// Maps the Unicode script property (as HarfBuzz reports it) onto the scripts the fallback chain
    /// distinguishes. Anything absent resolves to <see cref="FontScript.Any"/> and is served by the
    /// generic tail of the chain.
    /// </summary>
    private static readonly Dictionary<uint, FontScript> script_map = new Dictionary<uint, FontScript>
    {
        [HarfBuzzSharp.Script.Latin] = FontScript.Latin,
        [HarfBuzzSharp.Script.Cyrillic] = FontScript.Cyrillic,
        [HarfBuzzSharp.Script.Greek] = FontScript.Greek,
        [HarfBuzzSharp.Script.Han] = FontScript.Han,
        [HarfBuzzSharp.Script.Hiragana] = FontScript.Japanese,
        [HarfBuzzSharp.Script.Katakana] = FontScript.Japanese,
        [HarfBuzzSharp.Script.Hangul] = FontScript.Korean,
        [HarfBuzzSharp.Script.Bopomofo] = FontScript.ChineseTraditional,
        [HarfBuzzSharp.Script.Thai] = FontScript.Thai,
        [HarfBuzzSharp.Script.Arabic] = FontScript.Arabic,
        [HarfBuzzSharp.Script.Hebrew] = FontScript.Hebrew,
        [HarfBuzzSharp.Script.Devanagari] = FontScript.Devanagari
    };

    /// <summary>
    /// The probe codepoint for <paramref name="script"/>, or 0 when the script makes no coverage
    /// promise a single codepoint can verify.
    /// </summary>
    public static uint ProbeFor(FontScript script)
    {
        foreach ((var candidate, uint probe) in script_probes)
        {
            if (candidate == script)
                return probe;
        }

        return 0;
    }

    /// <summary>
    /// Classifies <paramref name="codepoint"/> for font selection.
    /// </summary>
    /// <param name="codepoint">The codepoint to classify.</param>
    /// <param name="current">
    /// The script of the text preceding it. Characters the Unicode script property calls
    /// <c>Common</c> or <c>Inherited</c> — spaces, digits, ASCII punctuation, 、, combining marks,
    /// emoji variation selectors — carry no script of their own and continue this one, so a comma in
    /// a Japanese sentence stays with the Japanese font instead of bouncing back to the primary.
    /// </param>
    /// <param name="languageHint">
    /// The language the text is known to be in, when the caller knows (<see cref="FontUsage.Script"/>).
    /// Only consulted for <see cref="FontScript.Han"/>, the one case a codepoint cannot settle.
    /// </param>
    public static FontScript FromCodepoint(uint codepoint, FontScript current = FontScript.Any, FontScript? languageHint = null)
    {
        var script = classify(codepoint, current);

        // A hint only disambiguates the ambiguous case. Letting it override kana or Hangul would let
        // a mislabelled string render in the wrong language's font, which is worse than ignoring it.
        if (script == FontScript.Han && languageHint.HasValue && IsCJK(languageHint.Value))
            return languageHint.Value;

        return script;
    }

    private static FontScript classify(uint codepoint, FontScript current)
    {
        // ASCII is the overwhelming majority of what gets shaped, and it needs no native call:
        // letters are Latin, everything else (digits, punctuation, whitespace) continues the run.
        if (codepoint < 0x80)
        {
            if ((codepoint >= 'A' && codepoint <= 'Z') || (codepoint >= 'a' && codepoint <= 'z'))
                return FontScript.Latin;

            return inherit(current);
        }

        // Checked before the script property, which reports most emoji as Common and would therefore
        // hand them to whichever font the surrounding text uses.
        if (isEmoji(codepoint))
            return FontScript.Emoji;

        uint unicodeScript = HarfBuzzSharp.UnicodeFunctions.Default.GetScript(codepoint);

        if (script_map.TryGetValue(unicodeScript, out var mapped))
            return mapped;

        // Common / Inherited / Unknown, plus every script the chain does not distinguish.
        if (unicodeScript == HarfBuzzSharp.Script.Common || unicodeScript == HarfBuzzSharp.Script.Inherited)
            return inherit(current);

        return FontScript.Any;
    }

    /// <summary>
    /// Continues the preceding run's script. An emoji run is not continued: the following space or
    /// digit belongs with the text around it, not with the emoji font.
    /// </summary>
    private static FontScript inherit(FontScript current) => current == FontScript.Emoji ? FontScript.Any : current;

    /// <summary>
    /// Whether <paramref name="codepoint"/> is a pictograph. Covers the emoji blocks proper plus the
    /// older symbol/dingbat ranges emoji presentation reaches into. Deliberately excludes the
    /// text-first characters that live in those ranges' neighbourhood (©, ®, ™, ASCII digits used in
    /// keycaps), which read better in the text font.
    /// </summary>
    private static bool isEmoji(uint codepoint)
        => codepoint is >= 0x1F000 and <= 0x1FAFF // Mahjong/dominoes/cards through Symbols Extended-A
            or >= 0x2600 and <= 0x27BF // Miscellaneous Symbols + Dingbats
            or >= 0x2B00 and <= 0x2BFF; // Miscellaneous Symbols and Arrows (★, ⭐)

    public static bool IsCJK(FontScript script)
    {
        foreach (var cjk in CJK_SCRIPTS)
        {
            if (cjk == script)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether two scripts can share one HarfBuzz shaping run when the same font serves both.
    /// </summary>
    /// <remarks>
    /// A run is shaped with properties guessed from its contents, so a run must not mix a complex
    /// script with anything else — Thai marks shaped as Latin position wrongly. Within a group the
    /// distinction does not affect shaping, which keeps Japanese text (constantly alternating kana and
    /// kanji) at one run per font instead of one per character class.
    /// </remarks>
    public static bool ShareShapingRun(FontScript a, FontScript b) => a == b || shapingGroup(a) == shapingGroup(b);

    /// <summary>
    /// 0 = simple LTR alphabetic, 1 = CJK, and one group per script needing its own shaping
    /// properties.
    /// </summary>
    private static int shapingGroup(FontScript script)
    {
        switch (script)
        {
            case FontScript.Any:
            case FontScript.Latin:
            case FontScript.Cyrillic:
            case FontScript.Greek:
                return 0;

            case FontScript.Han:
            case FontScript.Japanese:
            case FontScript.Korean:
            case FontScript.ChineseSimplified:
            case FontScript.ChineseTraditional:
                return 1;

            default:
                return 2 + (int)script;
        }
    }
}
