// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;

namespace Sakura.Framework.Input.Bindings;

/// <summary>
/// Supplies the live key bindings for a <see cref="KeyBindingContainer{T}"/>. Implemented by
/// <see cref="KeyBindingStore{T}"/> to provide user-overridden, persisted bindings.
/// </summary>
public interface IKeyBindingSource
{
    /// <summary>
    /// Returns the bindings to use for the given container type. The container's runtime type is
    /// passed so a single store can serve multiple distinct binding sets.
    /// </summary>
    IEnumerable<KeyBinding> GetBindings(Type containerType);
}
