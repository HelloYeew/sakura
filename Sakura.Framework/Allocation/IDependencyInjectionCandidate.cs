// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

namespace Sakura.Framework.Allocation;

/// <summary>
/// Marker interface that opts a class into the Sakura DI system.
/// Classes implementing this interface (directly or via inheritance) are processed
/// by the source generator at compile time to produce fast, reflection-free injection code.
/// Types not processed by the generator fall back to reflection automatically.
/// </summary>
public interface IDependencyInjectionCandidate
{
}
