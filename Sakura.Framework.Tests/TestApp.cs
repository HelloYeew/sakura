// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Reflection;
using Sakura.Framework.Graphics.Cursor;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests;

public class TestApp : TestBrowserApp
{
    public TestApp(Assembly testAssembly = null!) : base(testAssembly)
    {
    }

    public override void Load()
    {
        base.Load();
        Add(new CursorContainer());
        Window.CursorVisible = true;
    }

    protected override Assembly ResourceAssembly => typeof(TestApp).Assembly;
    protected override string ResourceRootNamespace => "Sakura.Framework.Tests.Resources";
}
