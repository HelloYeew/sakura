// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Platform;

namespace Sakura.Framework;

public class App : Container, IDisposable
{
    public IWindow Window => Host?.Window;

    protected AppHost Host { get; private set; }

    internal FpsGraph FpsGraph { get; private set; }

    internal void SetHost(AppHost host) => Host = host;

    public override void Load()
    {
        base.Load();

        Add(FpsGraph = new FpsGraph(Host.AppClock)
        {
            Depth = float.MaxValue
        });
        FpsGraph.Hide();

        Cache(Host);
        Cache(this);
        if (Host.Window != null) Cache(Host.Window);
        if (Host.Storage != null) Cache(Host.Storage);
    }

    /// <summary>
    /// Create a default <see cref="Storage"/> that
    /// </summary>
    /// <param name="host"></param>
    /// <param name="defaultStorage"></param>
    /// <returns></returns>
    protected internal virtual Storage CreateStorage(AppHost host, Storage defaultStorage) => defaultStorage;

    public void Dispose()
    {
        // TODO release managed resources here
    }
}
