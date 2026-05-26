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

public class FpsGraph : Container
{
    private Reactive<PerformanceOverlayState> state;

    private FlowContainer displaysFlow;
    private Container currentContextFlow;

    private SpriteText limiterText;
    private SpriteText windowModeText;
    private SpriteText executionModeText;

    private FontUsage graphFontUsage = FontUsage.Default.With(size: 14);
    private FontUsage boldGraphFontUsage = FontUsage.Default.With(size: 14, weight: "Bold");

    private const float extended_width = 350;
    private const float compact_width = 300;

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
            Size = new Vector2(extended_width, 80),
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
                        }
                    }
                }
            }
        };

        displaysFlow.Add(currentContextFlow);

        displaysFlow.Add(new ThreadStatisticsDisplay("Input", host.InputClock, Color.Gray, window));
        displaysFlow.Add(new ThreadStatisticsDisplay("Audio", host.AudioClock, Color.Yellow, window));
        displaysFlow.Add(new ThreadStatisticsDisplay("Update", host.UpdateClock, Color.MediumPurple, window));
        displaysFlow.Add(new ThreadStatisticsDisplay("Draw", host.DrawClock, Color.Cyan, window));

        state = host.FrameworkConfigManager.Get(FrameworkSetting.ShowFpsGraph, PerformanceOverlayState.Hidden);
        state.ValueChanged += e => updateState(e.NewValue);
        if (state.Value == PerformanceOverlayState.Hidden)
            Hide();
        updateState(state.Value);
    }

    public override void LoadComplete()
    {
        base.LoadComplete();
        windowModeText.Text = $"{host.Window.WindowModeReactive.Value} ({host.Window.Width}x{host.Window.Height})";
        executionModeText.Text = $"{host.ExecutionMode.Value}";

        limiterText.Text = $"{host.FrameLimiter.Value}";
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

    private class ThreadStatisticsDisplay : Container
    {
        private const int max_history = 240;
        private readonly FrameData[] frameHistory = new FrameData[max_history];
        private readonly int[] lastGcCounts = new int[3];
        private int currentIndex;
        private int currentCount;
        private readonly IWindow window;

        private readonly string name;
        private readonly IClock clock;
        private readonly Color baseColor;

        private SpriteText statsText;
        private ThreadBarGraph barGraph;

        private PerformanceOverlayState currentState;

        public ThreadStatisticsDisplay(string name, IClock clock, Color baseColor, IWindow window)
        {
            this.name = name;
            this.clock = clock;
            this.baseColor = baseColor;
            this.window = window;

            for (int i = 0; i < max_history; i++)
            {
                frameHistory[i] = new FrameData
                {
                    GcGeneration = -1
                };
            }

            for (int i = 0; i < 3; i++)
                lastGcCounts[i] = GC.CollectionCount(i);

            Anchor = Anchor.TopRight;
            Origin = Anchor.TopRight;

            Size = new Vector2(extended_width-5, 20);

            Add(barGraph = new ThreadBarGraph(this)
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0.8f
            });

            Add(new FlowContainer
            {
                Direction = FlowDirection.Horizontal,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(10, 0),
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
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
            });
        }

        public void SetState(PerformanceOverlayState state)
        {
            currentState = state;

            if (state == PerformanceOverlayState.Compact)
            {
                barGraph.Hide();
                AutoSizeAxes = Axes.Both;
                Size = Vector2.Zero;
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

                frameHistory[currentIndex] = new FrameData
                {
                    ElapsedTime = clock.ElapsedFrameTime,
                    IsActive = window.IsActive,
                    GcGeneration = highestGcGen
                };

                currentIndex = (currentIndex + 1) % max_history;
                if (currentCount < max_history) currentCount++;

                updateStats();
                barGraph.Invalidate(InvalidationFlags.DrawInfo);
            }
        }

        private void updateStats()
        {
            if (currentCount == 0) return;

            // mean (average frame time)
            double sum = 0;
            for (int i = 0; i < currentCount; i++)
            {
                sum += frameHistory[i].ElapsedTime;
            }
            double meanFrameTime = sum / currentCount;

            // jitter via standard deviation
            double varianceSum = 0;
            for (int i = 0; i < currentCount; i++)
            {
                double diff = frameHistory[i].ElapsedTime - meanFrameTime;
                varianceSum += diff * diff;
            }
            double stdDev = Math.Sqrt(varianceSum / currentCount);

            // safeguard against division by zero for FPS
            double fps = meanFrameTime > 0.0001 ? 1000.0 / meanFrameTime : 0;

            statsText.Text = $"{fps,4:F0}fps ({meanFrameTime,4:F2}ms ±{stdDev,4:F2}ms)";
        }

        private class ThreadBarGraph : Drawable
        {
            private readonly ThreadStatisticsDisplay display;

            public ThreadBarGraph(ThreadStatisticsDisplay display)
            {
                this.display = display;
                Blending = BlendingMode.Additive;
                Vertices = new Vertex[max_history * 18];
            }

            protected override void GenerateVertices()
            {
                var finalMatrix = ModelMatrix;
                float w = DrawSize.X > 0 ? DrawSize.X : 1;
                float h = DrawSize.Y > 0 ? DrawSize.Y : 1;

                float barWidth = w / max_history;
                int startIndex = display.currentCount == max_history ? display.currentIndex : 0;

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
                        var blueBgColor = new Vector4(0, 0, 0.5f, DrawAlpha * 0.4f);
                        var bgTopLeft = Vector2.Transform(new Vector2(left / w, 0), finalMatrix);
                        var bgTopRight = Vector2.Transform(new Vector2(right / w, 0), finalMatrix);
                        var bgBottomLeft = Vector2.Transform(new Vector2(left / w, 1), finalMatrix);
                        var bgBottomRight = Vector2.Transform(new Vector2(right / w, 1), finalMatrix);

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
                    float barHeightRatio = (float)(frame.ElapsedTime / 33.3);
                    barHeightRatio = Math.Clamp(barHeightRatio, 0.02f, 1f);
                    float barHeight = barHeightRatio * h;
                    float top = h - barHeight;

                    Color color = display.baseColor;
                    var calculatedColor = new Vector4(
                        ColorExtensions.SrgbToLinear(color.R),
                        ColorExtensions.SrgbToLinear(color.G),
                        ColorExtensions.SrgbToLinear(color.B),
                        DrawAlpha * (color.A / 255f)
                    );

                    var pTopLeft = Vector2.Transform(new Vector2(left / w, top / h), finalMatrix);
                    var pTopRight = Vector2.Transform(new Vector2(right / w, top / h), finalMatrix);
                    var pBottomLeft = Vector2.Transform(new Vector2(left / w, 1), finalMatrix);
                    var pBottomRight = Vector2.Transform(new Vector2(right / w, 1), finalMatrix);

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
                        Color gcColor = frame.GcGeneration == 2 ? Color.Red : Color.Yellow;
                        var calcGcColor = new Vector4(
                            ColorExtensions.SrgbToLinear(gcColor.R),
                            ColorExtensions.SrgbToLinear(gcColor.G),
                            ColorExtensions.SrgbToLinear(gcColor.B),
                            DrawAlpha
                        );

                        float dotHeight = 3f / h;

                        var gcTopLeft = Vector2.Transform(new Vector2(left / w, 0), finalMatrix);
                        var gcTopRight = Vector2.Transform(new Vector2(right / w, 0), finalMatrix);
                        var gcBottomLeft = Vector2.Transform(new Vector2(left / w, dotHeight), finalMatrix);
                        var gcBottomRight = Vector2.Transform(new Vector2(right / w, dotHeight), finalMatrix);

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
        public bool IsActive;
        public int GcGeneration;
    }
}
