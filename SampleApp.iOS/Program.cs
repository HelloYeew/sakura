// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.iOS;

namespace SampleApp.iOS;

public static class Program
{
    public static void Main(string[] args) => SakuraApplication.Run("sample-app", () => new SampleAppApp());
}
