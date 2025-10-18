// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Timing;
using Vector2 = Sakura.Framework.Maths.Vector2;

namespace Sakura.Framework.Graphics.Performance;

/// <summary>
/// A drawable that displays a real-time graph of the application's frame times.
/// </summary>
public class FpsGraph : Container
{
    private const int max_history = 120; // Number of frames to show in the graph
    private const int bar_width = 4;    // Width of each bar in the graph
    private readonly Queue<double> frameHistory = new();

    private readonly Drawable[] graphBars = new Drawable[max_history];
    private readonly IClock clock;

    public FpsGraph(IClock clock)
    {
        this.clock = clock;

        RelativeSizeAxes = Axes.None;

        Size = new Vector2(max_history * bar_width, 150);
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
            var bar = new Drawable
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
    }

    public override void Update()
    {
        base.Update();

        // Add the latest frame time to our history
        if(clock != null)
            frameHistory.Enqueue(clock.ElapsedFrameTime);

        // Ensure the history does not exceed our maximum size
        while (frameHistory.Count > max_history)
            frameHistory.Dequeue();

        // Update the visual representation of the graph
        updateGraph();
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
}

