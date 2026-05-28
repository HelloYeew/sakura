// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Platform;
using Silk.NET.OpenGL;

namespace Sakura.Framework.Graphics.Video;

public class VideoSprite : Drawable, IDisposable
{
    private VideoDecoder decoder;
    private readonly string filePath;

    private readonly string videoCacheKey = string.Empty;

    private IRenderer renderer;
    private ITextureManager textureManager;

    public bool IsPlaying { get; private set; }
    public double CurrentTime => currentVideoTime;
    public double Duration => decoder.Duration;

    public double OriginalWidth => decoder.Width;
    public double OriginalHeight => decoder.Height;

    private double currentVideoTime;

    public VideoSprite(string filePath)
    {
        this.filePath = filePath;
    }

    [BackgroundDependencyLoader]
    private void load(AppHost host, ITextureManager textureManager)
    {
        renderer = host.Renderer;
        this.textureManager = textureManager;
    }

    public override void Load()
    {
        base.Load();

        decoder = new VideoDecoder(filePath);
        decoder.Start();

        Alpha = 0;
        IsPlaying = true;
    }

    public void Play() => IsPlaying = true;

    public void Pause() => IsPlaying = false;

    public void Stop()
    {
        IsPlaying = false;
        Seek(0);
    }

    public void Seek(double timeMs)
    {
        if (decoder == null) return;

        currentVideoTime = Math.Clamp(timeMs, 0, Duration);
        decoder.Seek(currentVideoTime);
    }

    public override void Update()
    {
        base.Update();

        if (!IsLoaded || decoder == null) return;

        if (IsPlaying)
        {
            currentVideoTime += Clock.ElapsedFrameTime;

            // Auto-loop behavior
            if (currentVideoTime >= Duration)
            {
                Seek(0);
            }
        }

        // Drain frames up to the current video time
        while (decoder.TryPeekNextFrame(out var nextFrame) && nextFrame.Timestamp <= currentVideoTime)
        {
            if (decoder.TryGetNextFrame(out var frameToDisplay))
            {
                var capturedFrame = frameToDisplay;

                renderer.ScheduleToDrawThread(() =>
                {
                    unsafe
                    {
                        if (Texture == null)
                        {
                            Texture = textureManager.FromPixelData(decoder.Width, decoder.Height, capturedFrame.PixelData, videoCacheKey);
                            Alpha = 1f;
                        }
                        else
                        {
                            var gl = GLRenderer.GL;
                            gl.BindTexture(TextureTarget.Texture2D, Texture.GlTexture.Handle);
                            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

                            fixed (byte* ptr = capturedFrame.PixelData)
                            {
                                gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
                                    (uint)decoder.Width, (uint)decoder.Height,
                                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
                            }
                            gl.BindTexture(TextureTarget.Texture2D, 0);
                        }
                    }
                    capturedFrame.Dispose();
                });
            }
        }
    }

    public void Dispose()
    {
        decoder?.Dispose();

        renderer?.ScheduleToDrawThread(() =>
        {
            if (Texture?.GlTexture != null)
            {
                Texture.GlTexture.Dispose();
            }

            Texture?.Dispose();
        });
    }
}
