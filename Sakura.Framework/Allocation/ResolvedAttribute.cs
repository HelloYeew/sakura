// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Allocation;

/// <summary>
/// Marks a field or property to be automatically populated with a dependency
/// from the nearest <see cref="IReadOnlyDependencyContainer"/>.
/// </summary>
/// <example>
/// Required dependency (throws if not cached anywhere in the hierarchy):
/// <code>
/// [Resolved]
/// private AudioManager audio { get; set; } = null!;
/// </code>
/// Optional dependency (resolves to <c>null</c> instead of throwing when missing):
/// <code>
/// [Resolved(canBeNull: true)]
/// private AudioManager? audio { get; set; }
/// </code>
/// The named form <c>[Resolved(CanBeNull = true)]</c> is also supported.
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ResolvedAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c>, the dependency is resolved via
    /// <see cref="IReadOnlyDependencyContainer.TryGet{T}"/> and is set to <c>null</c>
    /// if it is not cached anywhere in the hierarchy, rather than throwing.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool CanBeNull { get; init; }

    /// <summary>
    /// Creates a <see cref="ResolvedAttribute"/> for a required dependency.
    /// </summary>
    public ResolvedAttribute()
    {
    }

    /// <summary>
    /// Creates a <see cref="ResolvedAttribute"/>, optionally marking the dependency as optional.
    /// </summary>
    /// <param name="canBeNull">When <c>true</c>, a missing dependency resolves to <c>null</c> instead of throwing.</param>
    public ResolvedAttribute(bool canBeNull)
    {
        CanBeNull = canBeNull;
    }
}
