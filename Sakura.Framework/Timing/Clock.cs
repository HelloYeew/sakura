// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Diagnostics;

namespace Sakura.Framework.Timing;

/// <summary>
/// A high-precision clock implementation.
/// </summary>
public class Clock : IClock
{
    private readonly Stopwatch stopwatch = new Stopwatch();
    private double lastTime;
    private int frames;
    private double fpsAccumulator;

    public double CurrentTime { get; private set; }
    public double ElapsedFrameTime { get; private set; }
    public double FramesPerSecond { get; private set; }
    public bool IsRunning => stopwatch.IsRunning;

    public Clock(bool start = false)
    {
        if (start)
            Start();
    }

    public void Start()
    {
        stopwatch.Start();
        lastTime = stopwatch.Elapsed.TotalMilliseconds;
    }

    public void Stop() => stopwatch.Stop();

    public void Update()
    {
        double currentTimeMs = stopwatch.Elapsed.TotalMilliseconds;
        ElapsedFrameTime = currentTimeMs - lastTime;
        CurrentTime = currentTimeMs;
        lastTime = currentTimeMs;

        frames++;
        fpsAccumulator += ElapsedFrameTime;
        if (fpsAccumulator >= 1000)
        {
            FramesPerSecond = frames / (fpsAccumulator / 1000.0);
            frames = 0;
            fpsAccumulator -= 1000;
        }
    }
}
