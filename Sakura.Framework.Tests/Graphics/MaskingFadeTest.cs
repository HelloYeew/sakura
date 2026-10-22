// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// <see cref="Container.MaskingFade"/>: which fade each nested mask's content gets.
/// </summary>
[TestFixture]
public class ClipStackFadeTest
{
    private readonly ClipStack clips = new ClipStack();

    [SetUp]
    public void SetUp() => clips.Reset();

    /// <summary>
    /// A 100×200 mask with its top-left corner at (10, 20).
    /// </summary>
    private void pushOuter(MarginPadding fade) => clips.Push(new Vector2(60, 120), new Vector2(50, 100), 0, 0, fade);

    [Test]
    public void TheFadeIsMeasuredFromTheMasksEdges()
    {
        pushOuter(new MarginPadding { Top = 40, Bottom = 10 });

        var clip = clips.Current;

        Assert.Multiple(() =>
        {
            Assert.That(clip.FadeEdges.Y, Is.EqualTo(20), "top edge");
            Assert.That(clip.FadeLength.Y, Is.EqualTo(40));
            Assert.That(clip.FadeEdges.W, Is.EqualTo(220), "bottom edge");
            Assert.That(clip.FadeLength.W, Is.EqualTo(10));
            Assert.That(clip.FadeLength.X, Is.Zero, "no fade on the left");
            Assert.That(clip.FadeLength.Z, Is.Zero, "no fade on the right");
        });
    }

    /// <summary>
    /// A rounded cover inside a faded list still fades with the list.
    /// </summary>
    [Test]
    public void ANestedMaskKeepsTheFadeAroundIt()
    {
        pushOuter(new MarginPadding { Top = 40 });
        clips.Push(new Vector2(60, 40), new Vector2(20, 10), 0, 5);

        Assert.Multiple(() =>
        {
            Assert.That(clips.Current.FadeEdges.Y, Is.EqualTo(20), "still the outer mask's top, not the cover's");
            Assert.That(clips.Current.FadeLength.Y, Is.EqualTo(40));
        });
    }

    [Test]
    public void ANestedMasksOwnFadeReplacesOnlyItsSides()
    {
        pushOuter(new MarginPadding { Top = 40, Left = 8 });
        clips.Push(new Vector2(60, 120), new Vector2(20, 10), 0, 0, new MarginPadding { Left = 12 });

        Assert.Multiple(() =>
        {
            Assert.That(clips.Current.FadeEdges.X, Is.EqualTo(40), "the inner mask's left edge");
            Assert.That(clips.Current.FadeLength.X, Is.EqualTo(12));
            Assert.That(clips.Current.FadeEdges.Y, Is.EqualTo(20), "the outer mask's top still");
            Assert.That(clips.Current.FadeLength.Y, Is.EqualTo(40));
        });
    }

    [Test]
    public void PoppingRestoresTheFade()
    {
        pushOuter(new MarginPadding { Top = 40 });
        clips.Push(new Vector2(60, 120), new Vector2(20, 10), 0, 0, new MarginPadding { Top = 4 });
        clips.Pop();

        Assert.That(clips.Current.FadeLength.Y, Is.EqualTo(40));

        clips.Pop();

        Assert.That(clips.Current.FadeLength, Is.EqualTo(new Vector4(0, 0, 0, 0)), "no mask, no fade");
    }
}

/// <summary>
/// <see cref="Container.MaskingFade"/> in pixels, on Vulkan (see <see cref="PixelCapture"/>). The reference
/// rasterizer doesn't mask, so it has no fixture here.
/// </summary>
[TestFixture(PixelCapture.VULKAN)]
public class MaskingFadeTest
{
    private const int width = 40;
    private const int height = 100;
    private const float fade_length = 40;

    private static readonly Vector4 background = new Vector4(0, 0, 0, 1);

    private readonly string rendererKind;

    private ManualClock manual = null!;
    private Container root = null!;
    private PixelCapture capture = null!;

    /// <summary>
    /// The GPU stores 8-bit sRGB, so on top of its quantization a fade sampled at a pixel's center can
    /// differ a little from the curve's value there.
    /// </summary>
    private float tolerance => 0.01f + capture.Quantization;

    public MaskingFadeTest(string rendererKind)
    {
        this.rendererKind = rendererKind;
    }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Logger.Initialize();
        capture = PixelCapture.Create(rendererKind, width, height);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        capture?.Dispose();
        Logger.Shutdown();
    }

    [SetUp]
    public void SetUp()
    {
        manual = new ManualClock { CurrentTime = 1000 };
        root = new Container
        {
            Size = new Vector2(width, height),
            Clock = new FramedClock(manual)
        };

        root.Load();
        root.CompleteLoad();
    }

    private PixelSurface render()
    {
        manual.CurrentTime += 16;
        root.UpdateSubTree();

        capture.Begin(background);
        capture.Renderer.SetRoot(root.GenerateDrawNodeSubtree(0));
        capture.Renderer.Draw(new FramedClock(manual));

        return capture.End();
    }

    /// <summary>
    /// The shader's curve: <paramref name="distance"/> in from an edge that fades over
    /// <paramref name="length"/>, smoothstepped.
    /// </summary>
    private static float faded(float distance, float length)
    {
        float t = Math.Clamp(distance / length, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static Box white(Vector2 position, Vector2 size) => new Box { Position = position, Size = size, Color = Color.White };

    /// <summary>
    /// White over black: each pixel's red channel is the coverage left after the fade.
    /// </summary>
    [Test]
    public void ContentFadesOutTowardsAFadingEdge()
    {
        root.Add(new Container
        {
            Size = new Vector2(width, height),
            Masking = true,
            MaskingFade = new MarginPadding { Top = fade_length },
            Child = white(Vector2.Zero, new Vector2(width, height))
        });

        var screen = render();

        Assert.Multiple(() =>
        {
            foreach (int row in new[] { 0, 10, 20, 30 })
                Assert.That(screen[20, row].X, Is.EqualTo(faded(row + 0.5f, fade_length)).Within(tolerance), $"row {row}");

            Assert.That(screen[20, 60].X, Is.EqualTo(1f).Within(tolerance), "past the fade");
            Assert.That(screen[20, height - 1].X, Is.EqualTo(1f).Within(tolerance), "the bottom doesn't fade");
        });
    }

    /// <summary>
    /// A mask without a fade is the hard edge it always was: the vertices it stamps carry no fade.
    /// </summary>
    [Test]
    public void NoFadeIsAHardEdge()
    {
        root.Add(new Container
        {
            Size = new Vector2(width, height),
            Masking = true,
            Child = white(Vector2.Zero, new Vector2(width, height))
        });

        var screen = render();

        Assert.That(screen[20, 0].X, Is.EqualTo(1f).Within(tolerance));
    }

    /// <summary>
    /// A rounded cover inside a faded list: its own mask doesn't lose the list's fade.
    /// </summary>
    [Test]
    public void ANestedMaskFadesWithTheOneAroundIt()
    {
        root.Add(new Container
        {
            Size = new Vector2(width, height),
            Masking = true,
            MaskingFade = new MarginPadding { Top = fade_length },
            Child = new Container
            {
                Position = new Vector2(0, 10),
                Size = new Vector2(width, 20),
                Masking = true,
                CornerRadius = 4,
                Child = white(Vector2.Zero, new Vector2(width, 20))
            }
        });

        var screen = render();

        Assert.Multiple(() =>
        {
            Assert.That(screen[20, 15].X, Is.EqualTo(faded(15.5f, fade_length)).Within(tolerance));
            Assert.That(screen[20, 25].X, Is.EqualTo(faded(25.5f, fade_length)).Within(tolerance));
        });
    }

    /// <summary>
    /// Fades on two sides multiply where they overlap: the corner fades out with both.
    /// </summary>
    [Test]
    public void FadesOnTwoSidesMultiply()
    {
        root.Add(new Container
        {
            Size = new Vector2(width, height),
            Masking = true,
            MaskingFade = new MarginPadding { Top = fade_length, Left = 20 },
            Child = white(Vector2.Zero, new Vector2(width, height))
        });

        var screen = render();

        Assert.Multiple(() =>
        {
            Assert.That(screen[10, 20].X, Is.EqualTo(faded(10.5f, 20) * faded(20.5f, fade_length)).Within(tolerance), "in both");
            Assert.That(screen[10, 70].X, Is.EqualTo(faded(10.5f, 20)).Within(tolerance), "in the left one only");
        });
    }
}
