// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Allocation;

/// <summary>
/// Marks a <see cref="BackgroundDependencyLoaderAttribute"/> method parameter as optional.
/// <para>
/// When applied, the parameter is resolved via <see cref="IReadOnlyDependencyContainer.TryGet{T}"/>
/// and is passed <c>null</c> if the dependency is not cached anywhere in the hierarchy, rather than
/// throwing. A parameter declared as a nullable reference type (e.g. <c>Foo?</c>) is treated as
/// optional as well, even without this attribute.
/// </para>
/// <example>
/// <code>
/// [BackgroundDependencyLoader]
/// private void load([CanBeNull] AudioManager audio)
/// {
///     audio?.DoSomething();
/// }
/// </code>
/// </example>
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CanBeNullAttribute : Attribute
{
}
