using System;
using Sakura.Framework;
using Sakura.Framework.Platform;

namespace MyApp.Desktop;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using (AppHost host = new DesktopAppHost("my-app", new HostOptions()))
        using (App app = new MyAppApp())
            host.Run(app);
    }
}
