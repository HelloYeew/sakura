// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

#nullable disable

using System;
using System.Reflection;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.BassEngine;
using Sakura.Framework.Audio.Headless;
using Sakura.Framework.Configurations;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;

namespace Sakura.Framework;

public class App : Container, IDisposable
{
    public IWindow Window => Host?.Window;

    protected AppHost Host { get; private set; }

    internal FpsGraph FpsGraph { get; private set; }

    protected IAudioManager AudioManager { get; private set; }
    protected TrackStore TrackStore { get; private set; }
    protected SampleStore SampleStore { get; private set; }

    protected ITextureManager TextureManager { get; private set; }
    protected IFontStore FontStore { get; private set; }

    private Reactive<bool> showFpsGraph;

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

        AudioManager = CreateAudioManager();
        var masterVolume = Host.FrameworkConfigManager.Get<double>(FrameworkSetting.MasterVolume);
        var trackVolume = Host.FrameworkConfigManager.Get<double>(FrameworkSetting.TrackVolume);
        var sampleVolume = Host.FrameworkConfigManager.Get<double>(FrameworkSetting.SampleVolume);
        AudioManager.MasterVolume.BindTo(masterVolume);
        AudioManager.TrackVolume.BindTo(trackVolume);
        AudioManager.SampleVolume.BindTo(sampleVolume);

        Cache(AudioManager);

        var embeddedResourceStorage = new EmbeddedResourceStorage(ResourceAssembly, ResourceRootNamespace);

        TrackStore = new TrackStore(embeddedResourceStorage.GetStorageForDirectory("Tracks"), AudioManager);
        SampleStore = new SampleStore(embeddedResourceStorage.GetStorageForDirectory("Samples"), AudioManager);

        switch (Host.Renderer)
        {
            case GLRenderer:
                TextureManager = new GLTextureManager(GLRenderer.GL, embeddedResourceStorage.GetStorageForDirectory("Textures"), CreateImageLoader());
                FontStore = new GLFontStore(GLRenderer.GL);
                // TODO: This will exposed all framework file resource out, maybe find better way?
                var frameworkFontStorage = Host.FrameworkStorage.GetStorageForDirectory("Fonts");
                var fontStorage = embeddedResourceStorage.GetStorageForDirectory("Fonts");
                fontStorage = new CompositeStorage(frameworkFontStorage, fontStorage);
                FontStore.LoadDefaultFont(fontStorage);
                break;

            case HeadlessRenderer:
                TextureManager = new HeadlessTextureManager();
                FontStore = new HeadlessFontStore((HeadlessTextureManager)TextureManager);
                break;

            default:
                throw new NotSupportedException($"Renderer type {Host.Renderer.GetType().FullName} is not supported.");
        }
        Cache(TextureManager);
        Cache(FontStore);

        Cache<IAudioStore<ITrack>>(TrackStore);
        Cache<IAudioStore<ISample>>(SampleStore);

        Cache(Host);
        Cache(this);
        if (Host.Window != null) Cache(Host.Window);
        if (Host.Storage != null) Cache(Host.Storage);
        Cache(Host.FrameworkConfigManager);

        showFpsGraph = Host.FrameworkConfigManager.Get(FrameworkSetting.ShowFpsGraph, false);
        Add(FpsGraph = new FpsGraph(Host.AppClock)
        {
            Depth = float.MaxValue
        });
        if (!showFpsGraph)
            FpsGraph.Hide();
        showFpsGraph.ValueChanged += value =>
        {
            if (value.NewValue)
                FpsGraph.FadeIn(200, Easing.OutQuint);
            else
                FpsGraph.FadeOut(200, Easing.OutQuint);
        };
    }

    /// <summary>
    /// Create a default <see cref="Storage"/>
    /// </summary>
    protected internal virtual Storage CreateStorage(AppHost host, Storage defaultStorage) => defaultStorage;

    /// <summary>
    /// Create the image loader used for loading textures, defaults to <see cref="ImageSharpImageLoader"/>.
    /// </summary>
    protected virtual IImageLoader CreateImageLoader() => new ImageSharpImageLoader();

    /// <summary>
    /// Create the audio manager used for this app, defaults to <see cref="BassAudioManager"/>.
    /// </summary>
    protected virtual IAudioManager CreateAudioManager()
    {
        if (Host.IsHeadless)
            return new HeadlessAudioManager();
        return new BassAudioManager();
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

    public override bool OnKeyDown(KeyEvent e)
    {
        if (!e.IsRepeat && e.Key == Key.F11 && (e.Modifiers & KeyModifiers.Control) > 0)
        {
            showFpsGraph.Value = !showFpsGraph.Value;
        }
        return base.OnKeyDown(e);
    }
}
