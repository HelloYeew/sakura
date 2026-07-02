// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Text;

namespace Sakura.Framework.Extensions.DefaultFontWeightsExtensions;

public static class DefaultFontWeightsExtensions
{
    /// <summary>
    /// Maps a named weight to its numeric value on the OpenType <c>wght</c> variation axis
    /// (Thin=100 … Regular=400 … Black=900). Used to drive variable fonts; static fonts ignore it and
    /// are still resolved by name.
    /// </summary>
    public static float ToWeightValue(this FontWeights weight) => weight switch
    {
        FontWeights.Thin => 100f,
        FontWeights.ExtraLight => 200f,
        FontWeights.Light => 300f,
        FontWeights.Regular => 400f,
        FontWeights.Medium => 500f,
        FontWeights.SemiBold => 600f,
        FontWeights.Bold => 700f,
        FontWeights.ExtraBold => 800f,
        FontWeights.Black => 900f,
        _ => 400f
    };

    /// <summary>
    /// Maps a weight name (case-insensitive, e.g. "Bold") to its numeric <c>wght</c> value, returning
    /// 400 (Regular) when the name is unrecognized.
    /// </summary>
    public static float ToWeightValue(string weightName)
    {
        return Enum.TryParse<FontWeights>(weightName, ignoreCase: true, out var weight)
            ? weight.ToWeightValue()
            : 400f;
    }
}
