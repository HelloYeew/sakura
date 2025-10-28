// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Reflection;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.BassEngine;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;

namespace Sakura.Framework;

public class App : Container, IDisposable
{
    public IWindow Window => Host?.Window;

    protected AppHost Host { get; private set; }

    internal FpsGraph FpsGraph { get; private set; }

    protected IAudioManager AudioManager { get; private set; }
    protected TrackStore TrackStore { get; private set; }
    protected SampleStore SampleStore { get; private set; }

    /// <summary>
    /// The Assembly where embedded resources are stored.
    /// This defaults to the Assembly containing your App class (e.g., SampleApp.dll).
    /// </summary>
    protected virtual Assembly ResourceAssembly => GetType().Assembly;

    /// <summary>
    /// The root namespace for your embedded resources.
    /// This defaults to "[YourAppNamespace].Resources" (e.g., "SampleApp.Resources").
    /// You can override this in your App class to a dedicated namespace like "SampleApp.Resource".
    /// </summary>
    protected virtual string ResourceRootNamespace => $"{GetType().Namespace}.Resources";

    internal void SetHost(AppHost host) => Host = host;

    public override void Load()
    {
        base.Load();
        Cache(Host);
        Cache(this);
        if (Host.Window != null) Cache(Host.Window);
        if (Host.Storage != null) Cache(Host.Storage);
        Cache(Host.FrameworkConfigManager);

        AudioManager = new BassAudioManager();
        Cache(AudioManager);

        TrackStore = new TrackStore(Host.Storage.GetStorageForDirectory("Tracks"), AudioManager);
        SampleStore = new SampleStore(Host.Storage.GetStorageForDirectory("Samples"), AudioManager);

        Cache<IAudioStore<ITrack>>(TrackStore);
        Cache<IAudioStore<ISample>>(SampleStore);
    }

    /// <summary>
    /// Create a default <see cref="Storage"/>
    /// </summary>
    /// <param name="host"></param>
    /// <param name="defaultStorage"></param>
    /// <returns></returns>
    protected internal virtual Storage CreateStorage(AppHost host, Storage defaultStorage)
    {
        var resourceAssembly = ResourceAssembly;
        string resourceNamespace = ResourceRootNamespace;

        Logger.Debug($"Using embedded resource assembly: {resourceAssembly.FullName}");
        Logger.Debug($"Using embedded resource root namespace: {resourceNamespace}");

        var resourceStorage = new EmbeddedResourceStorage(resourceAssembly, resourceNamespace);

        var compositeStorage = new CompositeStorage(defaultStorage, resourceStorage);

        foreach (string resource in resourceAssembly.GetManifestResourceNames())
        {
            Logger.Debug($"Embedded resource available: {resource}");
        }

        return compositeStorage;
    }

    public void Dispose()
    {
        (AudioManager as IDisposable)?.Dispose();
    }

    public override void Update()
    {
        base.Update();
        AudioManager?.Update(Clock.ElapsedFrameTime);
    }
}
