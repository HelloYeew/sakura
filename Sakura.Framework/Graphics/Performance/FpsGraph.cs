// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Platform;
using Sakura.Framework.Timing;
using Sakura.Framework.Utilities;
using Vector2 = Sakura.Framework.Maths.Vector2;

namespace Sakura.Framework.Graphics.Performance;

/// <summary>
/// A drawable that displays a real-time graph of the application's frame times.
/// </summary>
public class FpsGraph : Container
{
    private const int max_history = 120; // Number of frames to show in the graph
    private const int bar_width = 4;    // Width of each bar in the graph
    private const int graph_height = 150; // Height of the graph
    private readonly Queue<double> frameHistory = new();

    private readonly Drawable[] graphBars = new Drawable[max_history];
    private readonly IClock clock;
    private SpriteText fpsText;
    private SpriteText limiterText;

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
            Color = Color.Red
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
                Color = Color.Green
            };
            graphBars[i] = bar;
            Add(bar);
        }

        Add(new Container()
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            Size = new Vector2(max_history * bar_width, 100),
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
                    Alpha = 0.75f
                },
                new FlowContainer()
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Direction = FlowDirection.Vertical,
                    Spacing = new Vector2(0, 30),
                    Size = new Vector2(1, 1),
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        fpsText = new SpriteText()
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Size = new Vector2(80, 20),
                            Color = Color.White,
                        },
                        limiterText = new SpriteText()
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Size = new Vector2(200, 20),
                            Color = Color.White
                        }
                    }
                }
            }
        });
    }

    public override void LoadComplete()
    {
        base.LoadComplete();

        limiterText.Text = $"FrameLimiter: {host.FrameLimiter.Value}";
        host.FrameLimiter.ValueChanged += value =>
        {
            limiterText.Text = $"FrameLimiter: {value.NewValue}";
        };
    }

    public override void Update()
    {
        base.Update();

        if (Precision.AlmostEqualZero(Alpha))
            return;

        // Add the latest frame time to our history
        if(clock != null)
            frameHistory.Enqueue(clock.ElapsedFrameTime);

        // Ensure the history does not exceed our maximum size
        while (frameHistory.Count > max_history)
            frameHistory.Dequeue();

        // Update the visual representation of the graph
        updateGraph();
        updateFpsText();
    }

    private void updateGraph()
    {
        double[] historyArray = frameHistory.ToArray();

        for (int i = 0; i < max_history; i++)
        {
            var bar = graphBars[i];

            if (i < historyArray.Length)
            {
                double frameTime = historyArray[i];
                double fps = frameTime > 0 ? 1000.0 / frameTime : 0;

                // Make the bar visible
                bar.Alpha = 1;

                // Calculate bar height as a fraction of the graph's total height (0 to 1).
                float barHeight = (float)(fps / 120.0); // Assume 120fps is a good max for the graph height
                barHeight = Math.Clamp(barHeight, 0, 1);
                bar.Size = new Vector2(bar_width, barHeight);

                if (fps < 30)
                    bar.Color = Color.Red;
                else if (fps < 58) // Leave a gap for less tolerance on 60hz monitors
                    bar.Color = Color.Yellow;
                else
                    bar.Color = Color.Green;
            }
            else
            {
                // Hide bars that don't have data yet
                bar.Alpha = 0;
            }
        }
    }

    private void updateFpsText()
    {
        if (frameHistory.Count == 0)
        {
            fpsText.Text = "FPS: N/A";
            return;
        }

        double latestFrameTime = 0;
        foreach (double ft in frameHistory)
            latestFrameTime = ft;
        double fps = latestFrameTime > 0 ? 1000.0 / latestFrameTime : 0;
        fpsText.Text = $"FPS: {fps:F1}";
    }
}

