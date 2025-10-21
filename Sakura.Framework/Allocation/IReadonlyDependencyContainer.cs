// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Allocation;

/// <summary>
/// A container that contains and can retrieve dependencies.
/// </summary>
public interface IReadOnlyDependencyContainer
{
    /// <summary>
    /// Retrieves a dependency of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the dependency to retrieve.</typeparam>
    /// <returns>>The retrieved dependency, or null if not found.</returns>
    T Get<T>() where T : class;

    /// <summary>
    /// Inject dependencies into the field and properties of the given instance
    /// that are marked with <see cref="ResolvedAttribute"/>.
    /// </summary>
    /// <param name="instance">Instance to inject dependencies into.</param>
    /// <typeparam name="T">Type of the instance.</typeparam>
    void Inject<T>(T instance) where T : class;
}
