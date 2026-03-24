// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests;

public class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using (AppHost host = new DesktopAppHost("sakura-framework-tests"))
        using (App app = new TestApp(typeof(Program).Assembly))
            host.Run(app);
    }
}
