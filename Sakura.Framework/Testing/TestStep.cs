// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Testing;

public class TestStep
{
    public string Description { get; set; }
    public Action Action { get; set; }
    public bool IsAssert { get; set; }
}
