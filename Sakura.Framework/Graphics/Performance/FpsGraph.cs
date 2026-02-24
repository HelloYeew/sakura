// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Performance;

/// <summary>
/// A drawable that displays a real-time graph of the application's frame times.
/// </summary>
public class FpsGraph : Container
{
    private const int max_history = 120; // Number of frames to show in the graph
    private const int bar_width = 4;    // Width of each bar in the graph
    private const int graph_height = 150; // Height of the graph
    private readonly double[] frameHistory = new double[max_history];
    private int currentIndex;
    private int currentCount;

    private readonly Drawable[] graphBars = new Drawable[max_history];
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

        for (int i = 0; i < max_history; i++)
        {
            var bar = new Box
            {
                RelativeSizeAxes = Axes.Y,
                Size = new Vector2(bar_width, 1),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = new Vector2(i * bar_width, 0), // Position each bar next to the previous one
                Color = Color.Green,
                Blending = BlendingMode.Additive
            };
            graphBars[i] = bar;
            Add(bar);
        }

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

        if (Precision.AlmostEqualZero(Alpha))
            return;

        if (clock != null)
        {
            frameHistory[currentIndex] = clock.ElapsedFrameTime;
            currentIndex = (currentIndex + 1) % max_history;

            if (currentCount < max_history)
                currentCount++;
        }

        updateGraph();
        updateFpsText();
    }

    private void updateGraph()
    {
        int startIndex = currentCount == max_history ? currentIndex : 0;

        for (int i = 0; i < max_history; i++)
        {
            var bar = graphBars[i];

            if (i < currentCount)
            {
                int bufferIndex = (startIndex + i) % max_history;
                double frameTime = frameHistory[bufferIndex];

                double fps = frameTime > 0 ? 1000.0 / frameTime : 0;

                bar.Alpha = 1;
                float barHeight = (float)(fps / 120.0);
                barHeight = Math.Clamp(barHeight, 0, 1);
                bar.Size = new Vector2(bar_width, barHeight);

                if (fps < 30)
                    bar.Color = Color.Red;
                else if (fps < 58)
                    bar.Color = Color.Yellow;
                else
                    bar.Color = Color.Green;
            }
            else
            {
                bar.Alpha = 0;
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
}

