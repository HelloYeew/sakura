// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Allocation;

/// <summary>
/// Marks a method that should be run to load dependencies.
/// This method will be invoked during the <see cref="Graphics.Drawables.Drawable.Load"/> phase.
/// It can accept an <see cref="IReadonlyDependencyContainer"/> as a parameter to retrieve dependencies from a parent.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class BackgroundDependencyLoaderAttribute : Attribute
{

}
