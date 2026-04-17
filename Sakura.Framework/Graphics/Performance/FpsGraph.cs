// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Rendering.Vertex;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Graphics.Performance;

/// <summary>
/// A drawable that displays a real-time graph of the application's frame times.
/// </summary>
public class FpsGraph : Container, IRemoveFromDrawVisualiser
{
    private const int max_history = 120; // Number of frames to show in the graph
    private const int bar_width = 4;    // Width of each bar in the graph
    private const int graph_height = 150; // Height of the graph
    private readonly double[] frameHistory = new double[max_history];
    private int currentIndex;
    private int currentCount;

    private FpsBarGraph barGraph;
    private readonly IClock clock;
    private SpriteText fpsText;
    private SpriteText limiterText;
    private SpriteText windowModeText;

    private FontUsage graphFontUsage = FontUsage.Default.With(size: 20);
    private FontUsage boldGraphFontUsage = FontUsage.Default.With(size: 20, weight: "Bold");

    [Resolved]
    private AppHost host { get; set; }

    public FpsGraph(IClock clock)
    {
        this.clock = clock;

        RelativeSizeAxes = Axes.None;

        Size = new Vector2(max_history * bar_width, graph_height);
        Anchor = Anchor.BottomRight;
        Origin = Anchor.BottomRight;
        Position = new Vector2(-10, -10);

        Add(new Box()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(1, 1),
            RelativeSizeAxes = Axes.Both,
            Color = Color.Black,
            Alpha = 0.4f,
            Blending = BlendingMode.Alpha
        });
    }

    public override void Load()
    {
        base.Load();

        barGraph = new FpsBarGraph(this)
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1)
        };
        Add(barGraph);

        Add(new Container()
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Size = new Vector2(max_history * bar_width, 125),
            Position = new Vector2(0, -graph_height-10),
            Padding = new MarginPadding(5),
            Children = new Drawable[]
            {
                new Box()
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(1, 1),
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.Black,
                    Alpha = 0.8f
                },
                new FlowContainer()
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Direction = FlowDirection.Vertical,
                    Size = new Vector2(1, 1),
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new FlowContainer()
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Direction = FlowDirection.Horizontal,
                            Size = new Vector2(1, 30),
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
                                    Text = "FPS"
                                },
                                fpsText = new SpriteText()
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
                            Size = new Vector2(1, 30),
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
                            Size = new Vector2(1, 30),
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
                    }
                }
            }
        });
    }

    public override void LoadComplete()
    {
        base.LoadComplete();
        windowModeText.Text = $"{host.Window.WindowModeReactive.Value} ({host.Window.Width}x{host.Window.Height})";

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
    }

    public override void Update()
    {
        base.Update();

        if (clock != null)
        {
            frameHistory[currentIndex] = clock.ElapsedFrameTime;
            currentIndex = (currentIndex + 1) % max_history;

            if (currentCount < max_history)
                currentCount++;

            if (DrawAlpha > 0)
            {
                barGraph.Invalidate(InvalidationFlags.DrawInfo);
                updateFpsText();
            }
        }
    }

    private void updateFpsText()
    {
        if (currentCount == 0)
        {
            fpsText.Text = "N/A";
            return;
        }

        int lastIndex = (currentIndex - 1 + max_history) % max_history;
        double latestFrameTime = frameHistory[lastIndex];

        double fps = latestFrameTime > 0 ? 1000.0 / latestFrameTime : 0;
        fpsText.Text = $"{fps:F1}";

        if (fps < 30)
            fpsText.Color = Color.Red;
        else if (fps < 58)
            fpsText.Color = Color.LightYellow;
        else
            fpsText.Color = Color.LightGreen;
    }

    private class FpsBarGraph : Drawable
    {
        private readonly FpsGraph graph;

        public FpsBarGraph(FpsGraph graph)
        {
            this.graph = graph;
            Blending = BlendingMode.Additive;
            Vertices = new Vertex[max_history * 6];
        }

        protected override void GenerateVertices()
        {
            var finalMatrix = ModelMatrix;
            float w = DrawSize.X > 0 ? DrawSize.X : 1;
            float h = DrawSize.Y > 0 ? DrawSize.Y : 1;

            int startIndex = graph.currentCount == max_history ? graph.currentIndex : 0;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < max_history; i++)
            {
                int offset = i * 6;

                if (i >= graph.currentCount)
                {
                    for (int v = 0; v < 6; v++)
                        Vertices[offset + v] = default;
                    continue;
                }

                int bufferIndex = (startIndex + i) % max_history;
                double frameTime = graph.frameHistory[bufferIndex];
                double fps = frameTime > 0 ? 1000.0 / frameTime : 0;

                float barHeightRatio = (float)(fps / 120.0);
                barHeightRatio = Math.Clamp(barHeightRatio, 0f, 1f);
                float barHeight = barHeightRatio * h;

                Color color = fps < 30 ? Color.Red : fps < 58 ? Color.Yellow : Color.Green;

                float rLinear = ColorExtensions.SrgbToLinear(color.R);
                float gLinear = ColorExtensions.SrgbToLinear(color.G);
                float bLinear = ColorExtensions.SrgbToLinear(color.B);
                var calculatedColor = new Vector4(rLinear, gLinear, bLinear, DrawAlpha * (color.A / 255f));

                float left = i * bar_width;
                float right = left + bar_width;
                float top = h - barHeight;

                var pTopLeft = Vector2.Transform(new Vector2(left / w, top / h), finalMatrix);
                var pTopRight = Vector2.Transform(new Vector2(right / w, top / h), finalMatrix);
                var pBottomLeft = Vector2.Transform(new Vector2(left / w, h / h), finalMatrix);
                var pBottomRight = Vector2.Transform(new Vector2(right / w, h / h), finalMatrix);

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

            if (minX <= maxX && minY <= maxY)
                DrawRectangle = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            else
                DrawRectangle = new RectangleF();
        }
    }
}
