// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// Shortens text that does not fit a width budget, appending an ellipsis to what is left.
/// </summary>
public static class TextTruncation
{
    /// <summary>
    /// The ellipsis used when a caller does not specify one: U+2026 HORIZONTAL ELLIPSIS.
    /// </summary>
    public const string DEFAULT_ELLIPSIS = "…";

    /// <summary>
    /// The outcome of a truncation pass.
    /// </summary>
    public readonly struct Result
    {
        /// <summary>
        /// The shaping of <see cref="Text"/> ready to hand to a drawable, no re-shaping needed.
        /// </summary>
        public ShapedText Shaped { get; init; }

        /// <summary>
        /// The text that <see cref="Shaped"/> covers: the source text when it fit, otherwise a prefix
        /// of it plus the ellipsis.
        /// </summary>
        public string Text { get; init; }

        /// <summary>
        /// Whether the source text had to be shortened.
        /// </summary>
        public bool Truncated { get; init; }
    }

    /// <summary>
    /// Measures <paramref name="text"/> and, if it is wider than <paramref name="maxWidth"/>, returns
    /// the longest prefix that fits once <paramref name="ellipsis"/> is appended. Trailing whitespace
    /// on the prefix is dropped so the ellipsis sits against the last visible character.
    /// </summary>
    /// <param name="store">The store used to shape and therefore measure candidates.</param>
    /// <param name="usage">The font to measure with.</param>
    /// <param name="text">The text to fit.</param>
    /// <param name="dpiScale">Physical-to-logical pixel ratio, as passed to <see cref="IFontStore.Shape"/>.</param>
    /// <param name="maxWidth">
    /// The budget in logical pixels — the same space <see cref="ShapedText.BoundingBox"/> is measured
    /// in. Positive infinity (or NaN) means unlimited, and the text is returned untouched.
    /// </param>
    /// <param name="ellipsis">
    /// Appended to a shortened result. Pass an empty string for a hard cut with no marker.
    /// </param>
    public static Result Apply(IFontStore store, FontUsage usage, string text, float dpiScale, float maxWidth, string ellipsis = DEFAULT_ELLIPSIS)
    {
        ArgumentNullException.ThrowIfNull(store);

        text ??= string.Empty;
        ellipsis ??= string.Empty;

        var full = store.Shape(usage, text, dpiScale);

        // Unlimited budget, already fits, or a store that does not measure (the headless one reports
        // zero for everything) — in every case the text is used as-is.
        if (float.IsNaN(maxWidth) || float.IsPositiveInfinity(maxWidth) || full.BoundingBox.X <= maxWidth)
            return new Result { Shaped = full, Text = text, Truncated = false };

        if (maxWidth <= 0)
            return new Result { Shaped = ShapedText.Empty, Text = string.Empty, Truncated = true };

        var cuts = clusterBoundaries(full, text);

        // Largest cut whose text still fits with the ellipsis attached. cuts[0] is 0, i.e. the
        // ellipsis on its own, so a budget that only fits the ellipsis is covered by the same search.
        int lo = 0;
        int hi = cuts.Count - 1;
        Result best = default;
        bool found = false;

        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            string candidate = compose(text, cuts[mid], ellipsis);
            var shaped = store.Shape(usage, candidate, dpiScale);

            if (shaped.BoundingBox.X <= maxWidth)
            {
                best = new Result { Shaped = shaped, Text = candidate, Truncated = true };
                found = true;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        // Not even the ellipsis alone fits, so nothing is drawn rather than something that overflows.
        if (!found)
            return new Result { Shaped = ShapedText.Empty, Text = string.Empty, Truncated = true };

        return best;
    }

    /// <summary>
    /// The lengths <paramref name="text"/> may be cut at, ascending: zero, the start of every shaped
    /// cluster, and the whole string. Taken from the shaper rather than from UTF-16 indices so a
    /// cluster spanning several code units stays whole. Positions repeat and run right-to-left for
    /// some scripts, hence the de-duplicating sort.
    /// </summary>
    private static List<int> clusterBoundaries(ShapedText shaped, string text)
    {
        var boundaries = new SortedSet<int> { 0, text.Length };

        var glyphs = shaped.Glyphs;
        for (int i = 0; i < glyphs.Count; i++)
        {
            int start = glyphs[i].StartIndex;

            if (start > 0 && start < text.Length)
                boundaries.Add(start);
        }

        return new List<int>(boundaries);
    }

    /// <summary>
    /// The first <paramref name="cut"/> characters of <paramref name="text"/>, trailing whitespace
    /// removed, with <paramref name="ellipsis"/> appended.
    /// </summary>
    private static string compose(string text, int cut, string ellipsis)
        => string.Concat(text.AsSpan(0, cut).TrimEnd(), ellipsis);
}
