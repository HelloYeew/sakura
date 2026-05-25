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

public class FpsGraph : Container, IRemoveFromDrawVisualiser
{
    private Reactive<PerformanceOverlayState> state;

    private FlowContainer displaysFlow;

    private SpriteText limiterText;
    private SpriteText windowModeText;
    private SpriteText executionModeText;

    private FontUsage graphFontUsage = FontUsage.Default.With(size: 14);
    private FontUsage boldGraphFontUsage = FontUsage.Default.With(size: 14, weight: "Bold");

    [Resolved]
    private AppHost host { get; set; }

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

        var currentContextFlow = new Container()
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1, 80),
            RelativeSizeAxes = Axes.X,
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

        displaysFlow.Add(new ThreadStatisticsDisplay("Input", host.InputClock, Color.Gray));
        displaysFlow.Add(new ThreadStatisticsDisplay("Audio", host.AudioClock, Color.Yellow));
        displaysFlow.Add(new ThreadStatisticsDisplay("Update", host.UpdateClock, Color.MediumPurple));
        displaysFlow.Add(new ThreadStatisticsDisplay("Draw", host.DrawClock, Color.Cyan));

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

    private void updateState(PerformanceOverlayState state)
    {
        if (state == PerformanceOverlayState.Hidden)
            this.FadeOut(200, Easing.OutQuint);
        else
            this.FadeIn(200, Easing.OutQuint);

        foreach (var child in displaysFlow.Children)
        {
            if (child is ThreadStatisticsDisplay display)
                display.SetState(state);
        }
    }

    private class ThreadStatisticsDisplay : Container
    {
        private const int max_history = 240;
        private readonly double[] frameHistory = new double[max_history];
        private int currentIndex;
        private int currentCount;

        private readonly string name;
        private readonly IClock clock;
        private readonly Color baseColor;

        private SpriteText statsText;
        private ThreadBarGraph barGraph;

        private PerformanceOverlayState currentState;

        public ThreadStatisticsDisplay(string name, IClock clock, Color baseColor)
        {
            this.name = name;
            this.clock = clock;
            this.baseColor = baseColor;

            AutoSizeAxes = Axes.X;
            Height = 20;

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
            Height = state == PerformanceOverlayState.Expanded ? 100 : 20;
        }

        public override void Update()
        {
            base.Update();

            if (clock != null && clock.IsRunning)
            {
                frameHistory[currentIndex] = clock.ElapsedFrameTime;
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
            for (int i = 0; i < currentCount; i++) sum += frameHistory[i];
            double meanFrameTime = sum / currentCount;

            // jitter via standard deviation
            double varianceSum = 0;
            for (int i = 0; i < currentCount; i++)
            {
                double diff = frameHistory[i] - meanFrameTime;
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
                Vertices = new Vertex[max_history * 6];
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
                    int offset = i * 6;

                    if (i >= display.currentCount)
                    {
                        for (int v = 0; v < 6; v++) Vertices[offset + v] = default;
                        continue;
                    }

                    int bufferIndex = (startIndex + i) % max_history;
                    double frameTime = display.frameHistory[bufferIndex];

                    float barHeightRatio = (float)(frameTime / 16.0);
                    barHeightRatio = Math.Clamp(barHeightRatio, 0.05f, 1f);
                    float barHeight = barHeightRatio * h;

                    Color color = display.baseColor;
                    if (frameTime > 16.6)
                        color = Color.Red;

                    float r = ColorExtensions.SrgbToLinear(color.R);
                    float g = ColorExtensions.SrgbToLinear(color.G);
                    float b = ColorExtensions.SrgbToLinear(color.B);
                    var calculatedColor = new Vector4(r, g, b, DrawAlpha * (color.A / 255f));

                    float left = i * barWidth;
                    float right = left + barWidth;
                    float top = h - barHeight;

                    var pTopLeft = Vector2.Transform(new Vector2(left / w, top / h), finalMatrix);
                    var pTopRight = Vector2.Transform(new Vector2(right / w, top / h), finalMatrix);
                    var pBottomLeft = Vector2.Transform(new Vector2(left / w, 1), finalMatrix);
                    var pBottomRight = Vector2.Transform(new Vector2(right / w, 1), finalMatrix);

                    minX = Math.Min(minX, Math.Min(pTopLeft.X, pBottomRight.X));
                    minY = Math.Min(minY, Math.Min(pTopLeft.Y, pBottomRight.Y));
                    maxX = Math.Max(maxX, Math.Max(pTopLeft.X, pBottomRight.X));
                    maxY = Math.Max(maxY, Math.Max(pTopLeft.Y, pBottomRight.Y));

                    Vertices[offset + 0] = new Vertex
                    {
                        Position = pTopLeft,
                        TexCoords = new Vector2(0, 0),
                        Color = calculatedColor
                    };
                    Vertices[offset + 1] = new Vertex
                    {
                        Position = pTopRight,
                        TexCoords = new Vector2(1, 0),
                        Color = calculatedColor
                    };
                    Vertices[offset + 2] = new Vertex
                    {
                        Position = pBottomRight,
                        TexCoords = new Vector2(1, 1),
                        Color = calculatedColor
                    };
                    Vertices[offset + 3] = new Vertex
                    {
                        Position = pBottomRight,
                        TexCoords = new Vector2(1, 1),
                        Color = calculatedColor
                    };
                    Vertices[offset + 4] = new Vertex
                    {
                        Position = pBottomLeft,
                        TexCoords = new Vector2(0, 1),
                        Color = calculatedColor
                    };
                    Vertices[offset + 5] = new Vertex
                    {
                        Position = pTopLeft,
                        TexCoords = new Vector2(0, 0),
                        Color = calculatedColor
                    };
                }

                DrawRectangle = (minX <= maxX && minY <= maxY)
                    ? new RectangleF(minX, minY, maxX - minX, maxY - minY)
                    : new RectangleF();
            }
        }
    }
}
