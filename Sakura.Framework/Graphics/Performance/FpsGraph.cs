// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Configurations;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Reactive;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Performance;

public enum PerformanceOverlayState
{
    Hidden,
    Compact,
    Expanded
}

public partial class FpsGraph : Container, IRemoveFromDrawVisualiser
{
    private Reactive<PerformanceOverlayState> state;

    private FlowContainer displaysFlow;
    private Container currentContextFlow;

    private SpriteText limiterText;
    private SpriteText windowModeText;
    private SpriteText executionModeText;
    private SpriteText rendererText;

    private FontUsage graphFontUsage = FontUsage.Default.With(size: 14);
    private FontUsage boldGraphFontUsage = FontUsage.Default.With(size: 14, weight: "Bold");

    private const float extended_width = 400;
    private const float compact_width = 340;

    [Resolved]
    private AppHost host { get; set; }

    [Resolved]
    private IWindow window { get; set; }

    public FpsGraph()
    {
        RelativeSizeAxes = Axes.None;
        AutoSizeAxes = Axes.Both;
        Anchor = Anchor.BottomRight;
        Origin = Anchor.BottomRight;
        Position = new Vector2(-10, -10);
    }

    public override void Load()
    {
        base.Load();

        Add(displaysFlow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            AutoSizeAxes = Axes.Both,
            Padding = new MarginPadding(5),
            Spacing = new Vector2(0, 2)
        });

        currentContextFlow = new Container()
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(extended_width, 100),
            Children = new Drawable[]
            {
                new Box()
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.Black,
                    Alpha = 0.75f
                },
                new FlowContainer()
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Direction = FlowDirection.Vertical,
                    RelativeSizeAxes = Axes.Both,
                    Spacing = new Vector2(0, 2),
                    Children = new Drawable[]
                    {
                        new FlowContainer()
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Direction = FlowDirection.Horizontal,
                            Size = new Vector2(1, 20),
                            RelativeSizeAxes = Axes.X,
                            Spacing = new Vector2(10, 0),
                            Children = new Drawable[]
                            {
                                new SpriteText()
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Size = new Vector2(200, 10),
                                    Color = Color.White,
                                    Font = boldGraphFontUsage,
                                    Text = "FrameLimiter"
                                },
                                limiterText = new SpriteText()
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Size = new Vector2(200, 10),
                                    Color = Color.White,
                                    Font = graphFontUsage,
                                    Text = "N/A"
                                }
                            }
                        },
                        new FlowContainer()
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Direction = FlowDirection.Horizontal,
                            Size = new Vector2(1, 20),
                            RelativeSizeAxes = Axes.X,
                            Spacing = new Vector2(10, 0),
                            Children = new Drawable[]
                            {
                                new SpriteText()
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Size = new Vector2(200, 10),
                                    Color = Color.White,
                                    Font = boldGraphFontUsage,
                                    Text = "WindowMode"
                                },
                                windowModeText = new SpriteText()
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Size = new Vector2(200, 10),
                                    Color = Color.White,
                                    Font = graphFontUsage,
                                    Text = "N/A"
                                }
                            }
                        },
                        new FlowContainer()
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Direction = FlowDirection.Horizontal,
                            Size = new Vector2(1, 20),
                            RelativeSizeAxes = Axes.X,
                            Spacing = new Vector2(10, 0),
                            Children = new Drawable[]
                            {
                                new SpriteText()
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Size = new Vector2(200, 10),
                                    Color = Color.White,
                                    Font = boldGraphFontUsage,
                                    Text = "ExecutionMode"
                                },
                                executionModeText = new SpriteText()
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Size = new Vector2(200, 10),
                                    Color = Color.White,
                                    Font = graphFontUsage,
                                    Text = "N/A"
                                }
                            }
                        },
                        new FlowContainer()
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Direction = FlowDirection.Horizontal,
                            Size = new Vector2(1, 20),
                            RelativeSizeAxes = Axes.X,
                            Spacing = new Vector2(10, 0),
                            Children = new Drawable[]
                            {
                                new SpriteText()
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Size = new Vector2(200, 10),
                                    Color = Color.White,
                                    Font = boldGraphFontUsage,
                                    Text = "Renderer"
                                },
                                rendererText = new SpriteText()
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Size = new Vector2(200, 10),
                                    Color = Color.White,
                                    Font = graphFontUsage,
                                    Text = "N/A"
                                }
                            }
                        }
                    }
                }
            }
        };

        displaysFlow.Add(currentContextFlow);

        displaysFlow.Add(new ThreadStatisticsDisplay("Input", host.InputClock, Color.LimeGreen, host, host.GetInputTargetHz));
        displaysFlow.Add(new ThreadStatisticsDisplay("Audio", host.AudioClock, Color.Yellow, host, host.GetAudioTargetHz));
        displaysFlow.Add(new ThreadStatisticsDisplay("Update", host.UpdateClock, Color.Purple, host, host.GetUpdateTargetHz));
        displaysFlow.Add(new ThreadStatisticsDisplay("Draw", host.DrawClock, Color.Cyan, host, host.GetDrawTargetHz));

        state = host.FrameworkConfigManager.Get(FrameworkSetting.ShowFpsGraph, PerformanceOverlayState.Hidden);
        state.ValueChanged += e => updateState(e.NewValue);
        if (state.Value == PerformanceOverlayState.Hidden)
            Hide();
        updateState(state.Value);
    }

    private string getRendererText()
    {
        var configured = host.FrameworkConfigManager.Get<RendererType>(FrameworkSetting.RendererType).Value;
        string actual = host.Renderer?.GetType().Name ?? "None";

        if (actual.EndsWith("Renderer"))
            actual = actual[..^"Renderer".Length];

        return configured == RendererType.Automatic ? $"Automatic ({actual})" : actual;
    }

    public override void LoadComplete()
    {
        base.LoadComplete();

        windowModeText.Text = $"{host.Window?.WindowModeReactive.Value} ({host.Window?.Width}x{host.Window?.Height})";
        executionModeText.Text = $"{host.ExecutionMode.Value}";
        limiterText.Text = $"{host.FrameLimiter.Value}";
        rendererText.Text = getRendererText();

        host.FrameLimiter.ValueChanged += value =>
        {
            limiterText.Text = $"{value.NewValue}";
        };
        host.Window.WindowModeReactive.ValueChanged += value =>
        {
            windowModeText.Text = $"{value.NewValue} ({host.Window.Width}x{host.Window.Height})";
        };
        host.Window.Resized += (w, h) =>
        {
            windowModeText.Text = $"{host.Window.WindowModeReactive.Value} ({w}x{h})";
        };
        host.ExecutionMode.ValueChanged += value =>
        {
            executionModeText.Text = $"{value.NewValue}";
        };
    }

    private void updateState(PerformanceOverlayState newState)
    {
        if (newState == PerformanceOverlayState.Hidden)
            this.FadeOut(200, Easing.OutQuint);
        else
            this.FadeIn(200, Easing.OutQuint);

        if (newState == PerformanceOverlayState.Compact)
        {
            displaysFlow.Width = compact_width;
            currentContextFlow.Width = compact_width;
        }
        else
        {
            displaysFlow.Width = extended_width;
            currentContextFlow.Width = extended_width;
        }

        foreach (var child in displaysFlow.Children)
        {
            if (child is ThreadStatisticsDisplay display)
                display.SetState(newState);
        }
    }

    private partial class ThreadStatisticsDisplay : Container
    {
        private const int max_history = 240;
        private readonly FrameData[] frameHistory = new FrameData[max_history];
        private readonly int[] lastGcCounts = new int[3];
        private int currentIndex;
        private int currentCount;
        private readonly IWindow window;

        private readonly string name;
        private readonly IFrameBasedClock clock;
        private readonly Color baseColor;
        private readonly Func<double> getTargetHz;

        private readonly AppHost host;
        private double lastRecordedTime;
        private double lastVisualUpdateTime;
        private long dataVersion;
        private long lastGraphVersion;
        private double bucketMaxTime;
        private double bucketSumTime;
        private int bucketFrameCount;
        private int bucketHighestGc = -1;

        private const int jitter_ring_size = 128;
        private readonly double[] jitterRing = new double[jitter_ring_size];
        private int jitterRingIndex;
        private double jitterRingSum;
        private double jitterRingSumOfSquares;
        private int jitterRingCount;

        private SpriteText statsText;
        private ThreadBarGraph barGraph;
        private Box textBackground;

        private PerformanceOverlayState currentState;

        public ThreadStatisticsDisplay(string name, IFrameBasedClock clock, Color baseColor, AppHost host, Func<double> getTargetHz)
        {
            this.name = name;
            this.clock = clock;
            this.baseColor = baseColor;
            this.host = host;
            this.getTargetHz = getTargetHz;

            for (int i = 0; i < max_history; i++)
            {
                frameHistory[i] = new FrameData { GcGeneration = -1 };
            }

            for (int i = 0; i < 3; i++)
                lastGcCounts[i] = GC.CollectionCount(i);

            Anchor = Anchor.TopRight;
            Origin = Anchor.TopRight;
            Size = new Vector2(extended_width - 5, 20);

            Add(textBackground = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.Black,
                Alpha = 0.75f
            });

            Add(barGraph = new ThreadBarGraph(this)
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0.8f
            });

            Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Padding = new MarginPadding
                {
                    Left = 5,
                    Right = 5,
                },
                Children = new Drawable[]
                {
                    new FlowContainer
                    {
                        Direction = FlowDirection.Horizontal,
                        AutoSizeAxes = Axes.Both,
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Spacing = new Vector2(10, 0),
                        Children = new Drawable[]
                        {
                            new SpriteText
                            {
                                Text = name,
                                Font = FontUsage.Default.With(size: 16, weight: "Bold"),
                                Color = baseColor,
                                Margin = new MarginPadding { Top = 2 }
                            },
                            statsText = new SpriteText
                            {
                                Text = "Waiting...",
                                Font = FontUsage.Default.With(size: 16),
                                Color = Color.White,
                                Margin = new MarginPadding { Top = 2 }
                            }
                        }
                    }
                }
            });
        }

        public void SetState(PerformanceOverlayState state)
        {
            currentState = state;

            if (state == PerformanceOverlayState.Compact)
            {
                barGraph.Hide();
                AutoSizeAxes = Axes.Y;
                Width = compact_width - 5;
            }
            else
            {
                barGraph.Show();
                AutoSizeAxes = Axes.None;
                Size = new Vector2(extended_width - 5, 100);
            }
        }

        public override void Update()
        {
            base.Update();

            if (clock != null && clock.IsRunning)
            {
                if (clock.CurrentTime > lastRecordedTime)
                {
                    int highestGcGen = -1;
                    for (int i = 0; i < 3; i++)
                    {
                        int currentCount = GC.CollectionCount(i);
                        if (currentCount > lastGcCounts[i])
                        {
                            highestGcGen = i;
                            lastGcCounts[i] = currentCount;
                        }
                    }

                    double rawFrame = clock.ElapsedFrameTime;
                    double evicted = jitterRing[jitterRingIndex];
                    jitterRingSum += rawFrame - evicted;
                    jitterRingSumOfSquares += rawFrame * rawFrame - evicted * evicted;
                    jitterRing[jitterRingIndex] = rawFrame;
                    jitterRingIndex = (jitterRingIndex + 1) % jitter_ring_size;
                    if (jitterRingCount < jitter_ring_size)
                        jitterRingCount++;

                    bucketHighestGc = Math.Max(bucketHighestGc, highestGcGen);
                    bucketMaxTime = Math.Max(bucketMaxTime, rawFrame);
                    bucketSumTime += rawFrame;
                    bucketFrameCount++;

                    lastRecordedTime = clock.CurrentTime;

                    double bucketThreshold = 0;
                    switch (host.FrameLimiter.Value)
                    {
                        case FrameSync.VSync:
                        case FrameSync.Limit2x:
                            bucketThreshold = 0;
                            break;
                        case FrameSync.Limit4x:
                            bucketThreshold = 0.5;
                            break;
                        case FrameSync.Limit8x:
                        case FrameSync.Unlimited:
                            bucketThreshold = 1.0;
                            break;
                    }

                    if (bucketSumTime >= bucketThreshold && bucketFrameCount > 0)
                    {
                        frameHistory[currentIndex] = new FrameData
                        {
                            ElapsedTime = bucketSumTime / bucketFrameCount,
                            MaxElapsedTime = bucketMaxTime,
                            IsActive = host.Window?.IsActive ?? true,
                            GcGeneration = bucketHighestGc
                        };

                        currentIndex = (currentIndex + 1) % max_history;
                        if (currentCount < max_history) currentCount++;
                        dataVersion++;

                        bucketMaxTime = 0;
                        bucketSumTime = 0;
                        bucketFrameCount = 0;
                        bucketHighestGc = -1;
                    }
                }

                // Rebuild the graph whenever new data has been committed so it scrolls smoothly.
                // propagateToParent is false because the graph's bounds never change with its data;
                // only this drawable's vertices need regenerating.
                if (currentState == PerformanceOverlayState.Expanded && dataVersion != lastGraphVersion)
                {
                    barGraph.Invalidate(InvalidationFlags.DrawInfo, false);
                    lastGraphVersion = dataVersion;
                }

                // throttle stats text to 10Hz (unreadable faster)
                if (Clock.CurrentTime - lastVisualUpdateTime >= 100.0)
                {
                    updateStats();
                    lastVisualUpdateTime = Clock.CurrentTime;
                }
            }
        }

        private void updateStats()
        {
            if (currentCount == 0) return;

            // FPS
            double sum = 0;
            for (int i = 0; i < currentCount; i++)
                sum += frameHistory[i].ElapsedTime;
            double meanFrameTime = sum / currentCount;
            double fps = meanFrameTime > 0.0001 ? 1000.0 / meanFrameTime : 0;

            // Jitter from the raw ring using the one-pass computational variance formula:
            // Var(x) = E[x²] - E[x]²
            // This operates on raw (pre-bucket) frame times, so spikes are never averaged away.
            double jitter = 0;
            if (jitterRingCount > 0)
            {
                int n = jitterRingCount < jitter_ring_size ? jitterRingCount : jitter_ring_size;
                double mean = jitterRingSum / n;
                double variance = jitterRingSumOfSquares / n - mean * mean;
                // Clamp to 0 to guard against tiny floating-point negatives.
                jitter = Math.Sqrt(Math.Max(0, variance));
            }

            double targetHz = getTargetHz();
            string hzText = targetHz is > 0 and < 10_000 ? $"{targetHz:0}hz" : "∞hz";
            statsText.Text = $"{fps,4:F0}fps ({meanFrameTime,4:F2}ms ±{jitter,4:F2}ms) {hzText,5}";
        }

        private partial class ThreadBarGraph : Drawable
        {
            private readonly ThreadStatisticsDisplay display;

            public ThreadBarGraph(ThreadStatisticsDisplay display)
            {
                this.display = display;
                Blending = BlendingMode.Additive;
                Vertices = new Vertex[max_history * 18];
            }

            protected internal override VertexTopology Topology => VertexTopology.Triangles;

            protected override void GenerateVertices()
            {
                var finalMatrix = ModelMatrix;
                float w = DrawSize.X > 0 ? DrawSize.X : 1;
                float h = DrawSize.Y > 0 ? DrawSize.Y : 1;

                // rebuild runs once per committed data point (i.e. at update rate when expanded),
                // so it must stay cheap. The model matrix is affine, so decompose it once:
                // p' = origin + x * basisX + y * basisY — two multiply-adds per vertex instead of
                // a full 4x4 matrix transform.
                Vector2 origin = Vector2.Transform(new Vector2(0, 0), finalMatrix);
                Vector2 unitX = Vector2.Transform(new Vector2(1, 0), finalMatrix);
                Vector2 unitY = Vector2.Transform(new Vector2(0, 1), finalMatrix);
                float bxX = unitX.X - origin.X, bxY = unitX.Y - origin.Y;
                float byX = unitY.X - origin.X, byY = unitY.Y - origin.Y;

                Vector2 map(float x, float y) => new Vector2(
                    origin.X + x * bxX + y * byX,
                    origin.Y + x * bxY + y * byY);

                float barWidth = w / max_history;
                int startIndex = display.currentCount == max_history ? display.currentIndex : 0;

                // per-rebuild constants, hoisted out of the bar loop
                var blueBgColor = new Vector4(0, 0, 0.5f, DrawAlpha * 0.4f);

                Color color = display.baseColor;
                var calculatedColor = new Vector4(
                    ColorExtensions.SrgbToLinear(color.R),
                    ColorExtensions.SrgbToLinear(color.G),
                    ColorExtensions.SrgbToLinear(color.B),
                    DrawAlpha * (color.A / 255f)
                );

                var gcYellow = new Vector4(
                    ColorExtensions.SrgbToLinear(Color.Yellow.R),
                    ColorExtensions.SrgbToLinear(Color.Yellow.G),
                    ColorExtensions.SrgbToLinear(Color.Yellow.B),
                    DrawAlpha);

                var gcRed = new Vector4(
                    ColorExtensions.SrgbToLinear(Color.Red.R),
                    ColorExtensions.SrgbToLinear(Color.Red.G),
                    ColorExtensions.SrgbToLinear(Color.Red.B),
                    DrawAlpha);

                float dotHeight = 3f / h;

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;

                for (int i = 0; i < max_history; i++)
                {
                    int offset = i * 18; // 18 vertices per frame (background: 6, bar: 6, GC: 6)

                    if (i >= display.currentCount)
                    {
                        for (int v = 0; v < 18; v++) Vertices[offset + v] = default;
                        continue;
                    }

                    int bufferIndex = (startIndex + i) % max_history;
                    FrameData frame = display.frameHistory[bufferIndex];

                    float left = i * barWidth;
                    float right = left + barWidth;

                    // inactive background
                    if (!frame.IsActive)
                    {
                        var bgTopLeft = map(left / w, 0);
                        var bgTopRight = map(right / w, 0);
                        var bgBottomLeft = map(left / w, 1);
                        var bgBottomRight = map(right / w, 1);

                        Vertices[offset + 0] = new Vertex { Position = bgTopLeft, Color = blueBgColor };
                        Vertices[offset + 1] = new Vertex { Position = bgTopRight, Color = blueBgColor };
                        Vertices[offset + 2] = new Vertex { Position = bgBottomRight, Color = blueBgColor };
                        Vertices[offset + 3] = new Vertex { Position = bgBottomRight, Color = blueBgColor };
                        Vertices[offset + 4] = new Vertex { Position = bgBottomLeft, Color = blueBgColor };
                        Vertices[offset + 5] = new Vertex { Position = bgTopLeft, Color = blueBgColor };
                    }
                    else
                    {
                        for (int v = 0; v < 6; v++) Vertices[offset + v] = default;
                    }

                    // performance bar
                    float barHeightRatio = (float)(frame.MaxElapsedTime / 33.3);
                    barHeightRatio = Math.Clamp(barHeightRatio, 0.02f, 1f);
                    float barHeight = barHeightRatio * h;
                    float top = h - barHeight;

                    var pTopLeft = map(left / w, top / h);
                    var pTopRight = map(right / w, top / h);
                    var pBottomLeft = map(left / w, 1);
                    var pBottomRight = map(right / w, 1);

                    minX = Math.Min(minX, Math.Min(pTopLeft.X, pBottomRight.X));
                    minY = Math.Min(minY, Math.Min(pTopLeft.Y, pBottomRight.Y));
                    maxX = Math.Max(maxX, Math.Max(pTopLeft.X, pBottomRight.X));
                    maxY = Math.Max(maxY, Math.Max(pTopLeft.Y, pBottomRight.Y));

                    Vertices[offset + 6] = new Vertex { Position = pTopLeft, TexCoords = new Vector2(0, 0), Color = calculatedColor };
                    Vertices[offset + 7] = new Vertex { Position = pTopRight, TexCoords = new Vector2(1, 0), Color = calculatedColor };
                    Vertices[offset + 8] = new Vertex { Position = pBottomRight, TexCoords = new Vector2(1, 1), Color = calculatedColor };
                    Vertices[offset + 9] = new Vertex { Position = pBottomRight, TexCoords = new Vector2(1, 1), Color = calculatedColor };
                    Vertices[offset + 10] = new Vertex { Position = pBottomLeft, TexCoords = new Vector2(0, 1), Color = calculatedColor };
                    Vertices[offset + 11] = new Vertex { Position = pTopLeft, TexCoords = new Vector2(0, 0), Color = calculatedColor };

                    // GC event dt
                    if (frame.GcGeneration >= 0)
                    {
                        var calcGcColor = frame.GcGeneration == 2 ? gcRed : gcYellow;

                        var gcTopLeft = map(left / w, 0);
                        var gcTopRight = map(right / w, 0);
                        var gcBottomLeft = map(left / w, dotHeight);
                        var gcBottomRight = map(right / w, dotHeight);

                        Vertices[offset + 12] = new Vertex { Position = gcTopLeft, Color = calcGcColor };
                        Vertices[offset + 13] = new Vertex { Position = gcTopRight, Color = calcGcColor };
                        Vertices[offset + 14] = new Vertex { Position = gcBottomRight, Color = calcGcColor };
                        Vertices[offset + 15] = new Vertex { Position = gcBottomRight, Color = calcGcColor };
                        Vertices[offset + 16] = new Vertex { Position = gcBottomLeft, Color = calcGcColor };
                        Vertices[offset + 17] = new Vertex { Position = gcTopLeft, Color = calcGcColor };
                    }
                    else
                    {
                        for (int v = 0; v < 6; v++) Vertices[offset + 12 + v] = default;
                    }
                }

                DrawRectangle = (minX <= maxX && minY <= maxY)
                    ? new RectangleF(minX, minY, maxX - minX, maxY - minY)
                    : new RectangleF();
            }
        }
    }

    private struct FrameData
    {
        public double ElapsedTime;
        public double MaxElapsedTime;
        public bool IsActive;
        public int GcGeneration;
    }
}
