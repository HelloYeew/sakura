// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Audio;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Audio;

/// <summary>
/// A radial-style amplitude visualiser, ported from Renako. Mainly used for testing but worth use in the game sometimes.
/// Reference: https://github.com/HelloYeew/renako/blob/main/Renako.Game/Graphics/Drawables/AudioVisualizer.cs
/// </summary>
public partial class AudioVisualizer : Drawable
{
    /// <summary>
    /// The number of bars rendered.
    /// </summary>
    private const int bars_per_visualiser = 150;

    /// <summary>
    /// The maximum height of a bar, as a fraction of the drawable's height (0..1).
    /// </summary>
    private const float bar_length = 1.0f;

    /// <summary>
    /// How much each bar decays per millisecond (relative to a full bar).
    /// </summary>
    private const float decay_per_millisecond = 0.0024f;

    /// <summary>
    /// The minimum normalised amplitude required to render a bar.
    /// </summary>
    private const float amplitude_dead_zone = 0.001f;

    /// <summary>
    /// Milliseconds between amplitude samples.
    /// </summary>
    private float timeBetweenUpdates = 50;

    /// <summary>
    /// Input amplitude amplification. BASS FFT bins are already roughly 0..1 (and usually
    /// much smaller)
    /// </summary>
    public float Magnitude { get; set; } = 2;

    private readonly float[] frequencyAmplitudes = new float[bars_per_visualiser];
    private readonly float[] temporalAmplitudes = new float[ChannelAmplitudes.AMPLITUDES_SIZE];

    private readonly List<IHasAmplitudes> amplitudeSources = new List<IHasAmplitudes>();

    private double timeUntilNextUpdate;

    private static readonly Color bar_color = ColorExtensions.FromHex("f7f0f4").WithAlpha(0.2f);

    public AudioVisualizer()
    {
        Blending = BlendingMode.Additive;
        Color = bar_color;
    }

    protected internal override VertexTopology Topology => VertexTopology.Quads;

    /// <summary>
    /// Add a new amplitude source (e.g. a playing channel/track) to this visualiser.
    /// </summary>
    public void AddAmplitudeSource(IHasAmplitudes amplitudeSource)
    {
        if (amplitudeSource == null) throw new ArgumentNullException(nameof(amplitudeSource));
        amplitudeSources.Add(amplitudeSource);
    }

    /// <summary>
    /// Clear all amplitude sources.
    /// </summary>
    public void ClearAmplitudeSources() => amplitudeSources.Clear();

    /// <summary>
    /// Replace the current source(s) with a single new one.
    /// </summary>
    public void ChangeSource(IHasAmplitudes source)
    {
        amplitudeSources.Clear();
        AddAmplitudeSource(source);
    }

    /// <summary>
    /// Tie the sample rate to a musical tempo.
    /// </summary>
    public void ChangeSpeedByBpm(float bpm)
    {
        if (bpm > 0)
            timeBetweenUpdates = 60000 / bpm / 30;
    }

    public int SourceCount => amplitudeSources.Count;

    /// <summary>
    /// The current height (0..1) of a given bar; used for assertions in tests.
    /// </summary>
    public float GetBarValue(int index) => frequencyAmplitudes[index];

    private void sampleAmplitudes()
    {
        Array.Clear(temporalAmplitudes, 0, temporalAmplitudes.Length);

        foreach (var source in amplitudeSources)
        {
            var span = source.CurrentAmplitudes.FrequencyAmplitudes.Span;
            for (int i = 0; i < span.Length && i < temporalAmplitudes.Length; i++)
                temporalAmplitudes[i] += span[i];
        }

        for (int i = 0; i < bars_per_visualiser; i++)
        {
            float target = temporalAmplitudes[i % temporalAmplitudes.Length] * Magnitude;
            if (target > frequencyAmplitudes[i])
                frequencyAmplitudes[i] = Math.Min(1f, target);
        }
    }

    public override void Update()
    {
        base.Update();

        // Sample new amplitudes on a fixed cadence.
        timeUntilNextUpdate -= Clock.ElapsedFrameTime;
        if (timeUntilNextUpdate <= 0)
        {
            sampleAmplitudes();
            timeUntilNextUpdate += timeBetweenUpdates;
            if (timeUntilNextUpdate < 0)
                timeUntilNextUpdate = timeBetweenUpdates;
        }

        // Decay every bar toward zero.
        float decayFactor = (float)Clock.ElapsedFrameTime * decay_per_millisecond;
        for (int i = 0; i < bars_per_visualiser; i++)
        {
            // 3% of extra length makes the tail fall off faster near the bottom.
            frequencyAmplitudes[i] -= decayFactor * (frequencyAmplitudes[i] + 0.03f);
            if (frequencyAmplitudes[i] < 0)
                frequencyAmplitudes[i] = 0;
        }

        // Geometry depends on per-frame amplitude state, so rebuild it each frame.
        // DrawInfo invalidation re-runs UpdateTransforms() -> GenerateVertices() next pass.
        Invalidate(InvalidationFlags.DrawInfo);
    }

    protected override void GenerateVertices()
    {
        // Each bar is one quad (4 verts). Allocate/resize the shared vertex buffer.
        int requiredVertices = bars_per_visualiser * 4;
        if (Vertices.Length != requiredVertices)
            Vertices = new Vertex[requiredVertices];

        var color = new Vector4(
            ColorExtensions.SrgbToLinear(Color.R),
            ColorExtensions.SrgbToLinear(Color.G),
            ColorExtensions.SrgbToLinear(Color.B),
            DrawAlpha * (Color.A / 255f)
        );

        float barWidth = 1f / bars_per_visualiser; // in normalised 0..1 local space

        int v = 0;
        for (int j = 0; j < bars_per_visualiser; j++)
        {
            float amplitude = frequencyAmplitudes[j];

            // Below the dead zone we still emit a degenerate (zero-height) quad so the buffer
            // stays a constant size; it contributes nothing visible.
            float barHeight = amplitude < amplitude_dead_zone ? 0f : bar_length * amplitude;

            float left = j * barWidth;
            float right = left + barWidth;
            float bottom = 1f;          // local space: 1.0 is the bottom edge
            float top = 1f - barHeight; // grow upward from the bottom

            Vector2 tl = Vector2.Transform(new Vector2(left, top), ModelMatrix);
            Vector2 tr = Vector2.Transform(new Vector2(right, top), ModelMatrix);
            Vector2 br = Vector2.Transform(new Vector2(right, bottom), ModelMatrix);
            Vector2 bl = Vector2.Transform(new Vector2(left, bottom), ModelMatrix);

            Vertices[v++] = new Vertex { Position = tl, Color = color, TexCoords = new Vector2(0, 0) };
            Vertices[v++] = new Vertex { Position = tr, Color = color, TexCoords = new Vector2(1, 0) };
            Vertices[v++] = new Vertex { Position = br, Color = color, TexCoords = new Vector2(1, 1) };
            Vertices[v++] = new Vertex { Position = bl, Color = color, TexCoords = new Vector2(0, 1) };
        }
    }
}
