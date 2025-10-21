// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Allocation;

/// <summary>
/// Marks a field or property to be automatically populated with a dependency
/// from the nearest <see cref="IReadOnlyDependencyContainer"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ResolvedAttribute : Attribute
{
}
