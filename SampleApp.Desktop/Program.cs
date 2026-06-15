// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework;
using Sakura.Framework.Platform;

namespace SampleApp.Desktop;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using (AppHost host = new DesktopAppHost("sample-app", new HostOptions()))
        using (App app = new SampleAppApp())
            host.Run(app);
    }
}

