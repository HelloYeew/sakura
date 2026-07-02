// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Text;

/// <summary>
/// A font weight that can be written either as a <see cref="FontWeights"/> value
/// (<see cref="FontWeights.Bold"/>) or as a raw weight name string (<c>"Bold"</c>, or a custom name a
/// third-party family might use).
/// </summary>
public readonly struct FontWeight : IEquatable<FontWeight>
{
    private readonly string? name;

    /// <summary>
    /// The weight name (e.g. "Regular", "Bold"). Defaults to "Regular" when unset.
    /// </summary>
    public string Name => name ?? nameof(FontWeights.Regular);

    public FontWeight(string? name)
    {
        this.name = name;
    }

    public FontWeight(FontWeights weight)
    {
        name = weight.ToString();
    }

    public static implicit operator FontWeight(string? name) => new FontWeight(name);

    public static implicit operator FontWeight(FontWeights weight) => new FontWeight(weight);

    public bool Equals(FontWeight other) => Name == other.Name;

    public override bool Equals(object? obj) => obj is FontWeight other && Equals(other);

    public override int GetHashCode() => Name.GetHashCode();

    public override string ToString() => Name;
}
