// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Allocation;

/// <summary>
/// A read-only view of a dependency container
/// </summary>
public interface IReadOnlyDependencyContainer
{
    /// <summary>
    /// Retrieves a dependency of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the dependency to retrieve.</typeparam>
    /// <returns>The cached dependency instance.</returns>
    /// <exception cref="System.InvalidOperationException">Thrown when the dependency is not found anywhere in the hierarchy.</exception>
    T Get<T>() where T : class;
}
