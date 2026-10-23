// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.iOS;

namespace Sakura.Framework.Tests.iOS;

public static class Program
{
    // The entry assembly is this one, not the one holding the tests, so they're named.
    public static void Main(string[] args) => SakuraApplication.Run("sakura-framework-tests", () => new TestApp(typeof(TestApp).Assembly));
}
