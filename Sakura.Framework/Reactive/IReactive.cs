// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Reactive;

/// <summary>
/// A non-generic interface for a <see cref="Reactive"/> object, allowing for manipulation of properties
/// that don't depend on the generic type.
/// </summary>
public interface IReactive
{
    /// <summary>
    /// Whether the reactive object is disabled.
    /// If disabled, the value of this reactive object will not change
    /// </summary>
    bool Disabled { get; set; }

    /// <summary>
    /// Whether the current value of this reactive object is the default value.
    /// </summary>
    bool IsDefault { get; }

    /// <summary>
    /// Unbind this reactive object from any other reactive objects that it is bound to.
    /// </summary>
    void UnbindAll();
}

/// <summary>
/// A generic interface for a <see cref="Reactive"/> object.
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IReactive<T> : IReactive
{
    /// <summary>
    /// The event that is raised when the value of this reactive object changes.
    /// </summary>
    event Action<ValueChangedEvent<T>> ValueChanged;

    /// <summary>
    /// Gets or sets the value of this reactive object.
    /// </summary>
    T Value { get; set; }

    /// <summary>
    /// The default value of this reactive object.
    /// </summary>
    T Default { get; }

    /// <summary>
    /// Bind this reactive object to another reactive object to listen for changes.
    /// Any changes to the other reactive object's value will update this reactive object's value.
    /// </summary>
    /// <param name="other"></param>
    void BindTo(IReactive<T> other);

    /// <summary>
    /// Unbind this reactive object from the specified reactive object it is currently bound to.
    /// </summary>
    /// <param name="other">The reactive object to unbind from.</param>
    void UnbindFrom(IReactive<T> other);
}
