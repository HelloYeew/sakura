// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Threading;
using System.Threading.Tasks;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Audio;

/// <summary>
/// Draws a <see cref="Framework.Audio.Waveform"/> across this drawable's width: the left channel above the
/// center line, the right below, and each column tinted by which band dominates it.
/// </summary>
/// <remarks>
/// <para>
/// The whole waveform always maps onto the full width, so a timeline shows a section of it by making
/// the graph as wide as the song at its zoom level and scrolling it inside a masking container. Only
/// the columns inside the masking bounds are built, so a graph tens of thousands of pixels wide costs
/// what the visible part does. Rotate it for a vertical timeline.
/// </para>
/// <para>
/// <see cref="Drawable.Color"/> is the base color. <see cref="LowColor"/>, <see cref="MidColor"/> and
/// <see cref="HighColor"/> blend in by how strong that band is at a point against the loudest point
/// in the song; unset, they leave the base color alone.
/// </para>
/// </remarks>
public partial class WaveformGraph : Drawable
{
    private Waveform? waveform;

    /// <summary>
    /// The waveform to draw. Not disposed by this drawable.
    /// </summary>
    public Waveform? Waveform
    {
        get => waveform;
        set
        {
            if (waveform == value)
                return;

            waveform = value;
            queueRegeneration();
        }
    }

    private float resolution = 1;

    /// <summary>
    /// Points drawn per unit of width. Lower is cheaper and smoother.
    /// </summary>
    public float Resolution
    {
        get => resolution;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            if (resolution == value)
                return;

            resolution = value;
            queueRegeneration();
        }
    }

    private Color? lowColor;

    public Color? LowColor
    {
        get => lowColor;
        set
        {
            lowColor = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    private Color? midColor;

    public Color? MidColor
    {
        get => midColor;
        set
        {
            midColor = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    private Color? highColor;

    public Color? HighColor
    {
        get => highColor;
        set
        {
            highColor = value;
            Invalidate(InvalidationFlags.DrawInfo);
        }
    }

    /// <summary>
    /// Fired on the update thread each time a new set of points has been put in place.
    /// </summary>
    public event Action? Regenerated;

    private Waveform.Point[] resampled = Array.Empty<Waveform.Point>();
    private float maxLow;
    private float maxMid;
    private float maxHigh;

    private bool regenerationQueued;
    private int generatedPointCount = -1;
    private CancellationTokenSource? cancelSource;

    private RectangleF? culledAgainst;

    protected internal override VertexTopology Topology => VertexTopology.Quads;

    private void queueRegeneration() => regenerationQueued = true;

    public override void Update()
    {
        base.Update();

        int required = requiredPointCount;

        if (regenerationQueued || required != generatedPointCount)
        {
            regenerationQueued = false;
            regenerate(required);
        }

        // The vertices only cover what was inside the masking bounds when they were built. A mask that
        // moves on its own (a resizing parent) leaves this drawable's own transform alone, so nothing
        // else would rebuild them.
        if (!Nullable.Equals(CurrentMaskingBounds, culledAgainst))
            Invalidate(InvalidationFlags.DrawInfo);
    }

    private int requiredPointCount => (int)Math.Max(0, Math.Ceiling(DrawWidth * Resolution));

    private void regenerate(int pointCount)
    {
        cancelGeneration();

        generatedPointCount = pointCount;

        var source = waveform;

        if (source == null || pointCount < 2)
        {
            apply(Array.Empty<Waveform.Point>());
            return;
        }

        var cancellation = cancelSource = new CancellationTokenSource();
        var token = cancellation.Token;

        // Taken here, on the update thread: the property creates the scheduler on first use.
        var scheduler = Scheduler;

        Task.Run(async () =>
        {
            try
            {
                var smoothed = await source.GenerateResampledAsync(pointCount, token).ConfigureAwait(false);
                var points = await smoothed.GetPointsAsync().ConfigureAwait(false);

                scheduler.Add(() =>
                {
                    if (!token.IsCancellationRequested)
                        apply(points);
                });
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void apply(Waveform.Point[] points)
    {
        resampled = points;
        maxLow = maxMid = maxHigh = 0;

        foreach (var point in points)
        {
            maxLow = Math.Max(maxLow, point.LowIntensity);
            maxMid = Math.Max(maxMid, point.MidIntensity);
            maxHigh = Math.Max(maxHigh, point.HighIntensity);
        }

        Invalidate(InvalidationFlags.DrawInfo);
        Regenerated?.Invoke();
    }

    private void cancelGeneration()
    {
        cancelSource?.Cancel();
        cancelSource?.Dispose();
        cancelSource = null;
    }

    protected override void GenerateVertices()
    {
        var points = resampled;

        // The whole graph's bounds, whatever is culled below, so masking and input see its real extent.
        Vector2 origin = Vector2.Transform(Vector2.Zero, ModelMatrix);
        Vector2 alongX = Vector2.Transform(new Vector2(1, 0), ModelMatrix) - origin;
        Vector2 alongY = Vector2.Transform(new Vector2(0, 1), ModelMatrix) - origin;

        DrawRectangle = boundsOf(origin, origin + alongX, origin + alongY, origin + alongX + alongY);
        culledAgainst = CurrentMaskingBounds;

        if (points.Length < 2)
        {
            SetVertexCount(0);
            return;
        }

        // A quad per gap between points, and only for gaps whose column reaches into the mask. The
        // transform is affine, so the column at u runs from origin + u·alongX to that plus alongY.
        int segments = points.Length - 1;
        float step = 1f / segments;

        int first = 0;
        int last = segments - 1;

        if (CurrentMaskingBounds is { } mask)
        {
            first = segments;
            last = -1;

            for (int i = 0; i < segments; i++)
            {
                if (!columnVisible(origin, alongX, alongY, i * step, (i + 1) * step, mask))
                    continue;

                first = Math.Min(first, i);
                last = i;
            }
        }

        int count = Math.Max(0, last - first + 1);
        SetVertexCount(count * 4);

        if (count == 0)
            return;

        var baseColor = Color;
        float alpha = DrawAlpha;
        int v = 0;

        for (int i = first; i <= last; i++)
        {
            var point = points[i];
            var next = points[i + 1];

            float left = i * step;
            float right = (i + 1) * step;

            // Local space is the unit square: 0.5 is the centre line, the left channel grows up from
            // it and the right channel down.
            var topLeft = Vector2.Transform(new Vector2(left, 0.5f - point.AmplitudeLeft * 0.5f), ModelMatrix);
            var topRight = Vector2.Transform(new Vector2(right, 0.5f - next.AmplitudeLeft * 0.5f), ModelMatrix);
            var bottomRight = Vector2.Transform(new Vector2(right, 0.5f + next.AmplitudeRight * 0.5f), ModelMatrix);
            var bottomLeft = Vector2.Transform(new Vector2(left, 0.5f + point.AmplitudeRight * 0.5f), ModelMatrix);

            var color = toLinear(columnColor(baseColor, point), alpha);

            Vertices[v++] = new Vertex { Position = topLeft, Color = color, TexCoords = new Vector2(0, 0) };
            Vertices[v++] = new Vertex { Position = topRight, Color = color, TexCoords = new Vector2(1, 0) };
            Vertices[v++] = new Vertex { Position = bottomRight, Color = color, TexCoords = new Vector2(1, 1) };
            Vertices[v++] = new Vertex { Position = bottomLeft, Color = color, TexCoords = new Vector2(0, 1) };
        }
    }

    /// <summary>
    /// The base colour pulled towards each band's colour by how strong that band is here, in
    /// order: mids, then highs, then lows on top so kicks read clearly.
    /// </summary>
    private Color columnColor(Color baseColor, Waveform.Point point)
    {
        var color = baseColor;

        if (midColor is { } mid && maxMid > 0)
            color = ColorExtensions.Lerp(color, mid, point.MidIntensity / maxMid);

        if (highColor is { } high && maxHigh > 0)
            color = ColorExtensions.Lerp(color, high, point.HighIntensity / maxHigh);

        if (lowColor is { } low && maxLow > 0)
            color = ColorExtensions.Lerp(color, low, point.LowIntensity / maxLow);

        return color;
    }

    private static Vector4 toLinear(Color color, float alpha) => new Vector4(
        ColorExtensions.SrgbToLinear(color.R),
        ColorExtensions.SrgbToLinear(color.G),
        ColorExtensions.SrgbToLinear(color.B),
        alpha * (color.A / 255f));

    private static bool columnVisible(Vector2 origin, Vector2 alongX, Vector2 alongY, float from, float to, RectangleF mask)
    {
        var a = origin + alongX * from;
        var b = origin + alongX * to;

        var bounds = boundsOf(a, b, a + alongY, b + alongY);

        return bounds.X <= mask.X + mask.Width && bounds.X + bounds.Width >= mask.X
                                               && bounds.Y <= mask.Y + mask.Height && bounds.Y + bounds.Height >= mask.Y;
    }

    private static RectangleF boundsOf(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        float minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        float maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        float maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));

        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        cancelGeneration();
    }
}
