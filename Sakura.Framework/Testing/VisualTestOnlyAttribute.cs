// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Testing;

/// <summary>
/// Marks a test method or class to be skipped when running in the headless test runner.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class VisualTestOnlyAttribute : Attribute
{
    public string Reason { get; }

    public VisualTestOnlyAttribute(string reason = "This test required visual renderer")
    {
        Reason = reason;
    }
}
