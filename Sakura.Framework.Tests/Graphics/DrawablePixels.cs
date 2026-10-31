// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// A tree of drawables on a clock set by hand, drawn to pixels through <see cref="PixelCapture"/>: what a pixel test of a
/// drawable sets up. A test adds to <see cref="Root"/> and reads <see cref="Render"/>'s result.
/// </summary>
public sealed class DrawablePixels : IDisposable
{
    private readonly PixelCapture capture;
    private readonly int width;
    private readonly int height;
    private ManualClock manual = null!;

    /// <summary>
    /// What's drawn. <see cref="Reset"/> starts a new one.
    /// </summary>
    public Container Root { get; private set; } = null!;

    /// <summary>
    /// How far a channel may stray from the exact value on this renderer.
    /// </summary>
    public float Tolerance => 1e-3f + capture.Quantization;

    /// <summary>
    /// Whether this is the CPU reference rasterizer, which interpolates vertex colors exactly at pixel centres.
    /// </summary>
    public bool IsReferenceRasterizer { get; }

    public DrawablePixels(string rendererKind, int width, int height)
    {
        this.width = width;
        this.height = height;

        capture = PixelCapture.Create(rendererKind, width, height);
        IsReferenceRasterizer = rendererKind == PixelCapture.HEADLESS;

        Reset();
    }

    public void Reset()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        Root = new Container
        {
            Size = new Vector2(width, height),
            Clock = new FramedClock(manual)
        };

        Root.Load();
        Root.CompleteLoad();
    }

    /// <summary>
    /// One update of the tree, as a frame does.
    /// </summary>
    public void Frame()
    {
        manual.CurrentTime += 16;
        Root.UpdateSubTree();
    }

    /// <summary>
    /// A frame, drawn over <paramref name="background"/> (linear color; black if not given).
    /// </summary>
    public PixelSurface Render(Vector4? background = null)
    {
        Frame();

        capture.Begin(background ?? new Vector4(0, 0, 0, 1));
        capture.Renderer.SetRoot(Root.GenerateDrawNodeSubtree(0));
        capture.Renderer.Draw(new FramedClock(manual));

        return capture.End();
    }

    public void Dispose() => capture.Dispose();
}
