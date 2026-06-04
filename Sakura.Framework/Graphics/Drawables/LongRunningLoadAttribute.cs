// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// Denotes a component that performs long-running tasks (e.g., network downloads or massive disk I/O) upon load.
/// Components with this attribute must strictly be loaded via asynchronous methods to avoid deadlocks.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class LongRunningLoadAttribute : Attribute
{
}
