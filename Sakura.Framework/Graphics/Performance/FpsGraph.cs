// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Diagnostics.CodeAnalysis;
using Sakura.Framework.Allocation;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.Headless;
using Sakura.Framework.Audio.SdlEngine;
using Sakura.Framework.Configurations;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Extensions.ObjectExtensions;
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
using Sakura.Framework.Statistic;
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
    private const float overlay_width = 420;

    private const float header_height = 40;

    private const float column_name = 56;
    private const float column_fps = 54;
    private const float column_time = 160;
    private const float column_budget = 54;
    private const float column_load = 58;

    private const float column_spacing = 3;

    private const float row_height_compact = 19;
    private const float row_height_expanded = 54;

    private const double text_refresh_ms = 100;

    /// <summary>
    /// Color of the device wait's spread (+- one, amber)
    /// </summary>
    private static readonly Color spread_color = Color.FromArgb(230, 240, 190, 100);

    private Reactive<PerformanceOverlayState> state;

    private FlowContainer displaysFlow;
    private Container header;
    private SpriteText contextText;
    private SpriteText backendText;
    private ThreadStatisticsDisplay[] displays;

    private double lastBackendRefresh;

    private static readonly FontUsage header_font = FontUsage.Default.With(size: 13);

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

        displaysFlow.Add(header = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(overlay_width, header_height),
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = Color.Black,
                    Alpha = 0.75f
                },
                new FlowContainer
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Direction = FlowDirection.Vertical,
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = 5, Right = 5, Top = 2 },
                    Spacing = new Vector2(0, 1),
                    Children = new Drawable[]
                    {
                        contextText = new SpriteText
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Font = header_font,
                            Color = Color.White,
                            Text = "..."
                        },
                        backendText = new SpriteText
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Font = header_font,
                            Color = Color.LightGray,
                            Text = "..."
                        }
                    }
                }
            }
        });

        displays = new[]
        {
            new ThreadStatisticsDisplay("Input", host.InputClock, Color.LimeGreen, host, () => host.InputFrameStatistics),
            new ThreadStatisticsDisplay("Audio", host.AudioClock, Color.Yellow, host, () => host.AudioFrameStatistics),
            new ThreadStatisticsDisplay("Update", host.UpdateClock, Color.Violet, host, () => host.UpdateFrameStatistics),
            new ThreadStatisticsDisplay("Draw", host.DrawClock, Color.Cyan, host, () => host.DrawFrameStatistics),
        };

        foreach (var display in displays)
            displaysFlow.Add(display);

        state = host.FrameworkConfigManager.Get(FrameworkSetting.ShowFpsGraph, PerformanceOverlayState.Hidden);
        state.ValueChanged += e => updateState(e.NewValue);

        if (state.Value == PerformanceOverlayState.Hidden)
            Hide();

        updateState(state.Value);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        updateContextText();
        updateBackendText();

        host.FrameLimiter.ValueChanged += _ => updateContextText();
        host.ExecutionMode.ValueChanged += _ => updateContextText();
        host.Window.WindowModeReactive.ValueChanged += _ => updateContextText();
        host.Window.Resized += (_, _) => updateContextText();
        host.Window.DisplayChanged += _ => updateContextText();
    }

    public override void Update()
    {
        base.Update();

        if (state?.Value == PerformanceOverlayState.Hidden)
            return;

        // The audio backend's output latency is a live device figure, unlike everything else on this
        // line, so it needs re-reading. Twice a second is plenty for a number that moves when the
        // device buffer is renegotiated.
        if (Clock.CurrentTime - lastBackendRefresh >= 500)
        {
            updateBackendText();
            lastBackendRefresh = Clock.CurrentTime;
        }
    }

    private void updateContextText()
    {
        if (contextText.IsNull())
            return;

        int displayHz = host.Window?.DisplayHz ?? 0;
        string hz = displayHz > 0 ? $" @{displayHz}Hz" : string.Empty;

        string execution = host.ExecutionMode.Value.ToString();
        string windowMode = host.Window?.WindowModeReactive.Value.ToString() ?? "None";

        contextText.Text = $"{host.FrameLimiter.Value} · {execution} · "
                           + $"{windowMode} {host.Window?.Width}x{host.Window?.Height}{hz}";
    }

    private void updateBackendText()
    {
        if (backendText.IsNull())
            return;

        double latency = host.AudioManager?.OutputLatencyMs ?? 0;
        string latencyText = latency > 0 ? $" {latency:F1}ms out" : string.Empty;

        backendText.Text = $"{getRendererText()} · {getAudioText()}{latencyText}";
    }

    private string getRendererText()
    {
        var configured = host.FrameworkConfigManager.Get<RendererType>(FrameworkSetting.RendererType).Value;
        string actual = host.Renderer?.GetType().Name ?? "None";

        if (actual.EndsWith("Renderer"))
            actual = actual[..^"Renderer".Length];

        string text = $"{getWindowType()}+{actual}";
        return configured == RendererType.Automatic ? $"{text} (auto)" : text;
    }

    private string getWindowType()
    {
        var type = host.Window?.GetType();
        string windowType;

        if (type.IsNotNull() && type.BaseType.IsNotNull())
        {
            windowType = type.BaseType.Name;
        }
        else
        {
            windowType = type?.Name ?? "None";
        }

        if (windowType.EndsWith("Window"))
            windowType = windowType[..^"Window".Length];

        return windowType;
    }

    private string getAudioText()
    {
        var configured = host.FrameworkConfigManager.Get<AudioBackend>(FrameworkSetting.AudioBackend).Value;
        string actual = host.AudioManager?.GetType().Name ?? "None";

        if (actual.EndsWith("AudioManager"))
            actual = actual[..^"AudioManager".Length];

        if (host.AudioManager is HeadlessAudioManager)
            return "Headless";

        if (host.AudioManager is SDLAudioManager sdl)
        {
            if (sdl.UsesNativeMixEngine)
            {
                actual += " native";
            }
            else if (configured == AudioBackend.SDLManaged)
            {
                actual += " managed";
            }
            else
            {
                actual += " fallback";
            }
        }

        return configured == AudioBackend.Automatic ? $"{actual} (auto)" : actual;
    }

    private void updateState(PerformanceOverlayState newState)
    {
        if (newState == PerformanceOverlayState.Hidden)
            this.FadeOut(200, Easing.OutQuint);
        else
            this.FadeIn(200, Easing.OutQuint);

        foreach (var display in displays)
            display.SetState(newState);
    }

    private sealed partial class ThreadStatisticsDisplay : Container
    {
        /// <summary>
        /// Bars in the graph, each covers <see cref="bucket_ms"/> of wall time
        /// </summary>
        private const int max_history = 120;

        private const double graph_span_ms = 2000;
        private const double bucket_ms = graph_span_ms / max_history;

        /// <summary>
        /// Slices the missed-deadline count is kept in, one rotated out per text refresh. Ten at
        /// 100ms each means the figure shown covers the last second.
        /// </summary>
        private const int miss_slice_count = 10;

        /// <summary>
        /// Frames the device wait's spread is taken over. At display rate this is roughly a second,
        /// long enough that a single late frame moves it without a steady stream of them hiding.
        /// </summary>
        private const int blocked_spread_window = 128;

        private readonly string name;
        private readonly IFrameBasedClock clock;
        private readonly Color baseColor;
        private readonly AppHost host;
        private readonly Func<ThreadFrameStatistics> getStatistics;

        private readonly ThreadFrameSample[] drainBuffer = new ThreadFrameSample[ThreadFrameStatistics.CAPACITY];

        /// <summary>
        /// Read the position in the thread's ring. Negative until the first drain seeds it, so the overlay
        /// starts from the present rather than replaying half a second of history it was not shown for.
        /// </summary>
        private long cursor = -1;

        private readonly GraphBucket[] history = new GraphBucket[max_history];
        private int historyIndex;
        private int historyCount;

        private GraphBucket pendingBucket;
        private double pendingBucketElapsed;

        private long dataVersion;
        private long lastGraphVersion;

        private double windowBusySum;
        private double windowBlockedSum;
        private int windowFrames;
        private double latestBudget;

        private readonly int[] missSlices = new int[miss_slice_count];
        private int missSliceIndex;

        /// <summary>
        /// The last <see cref="blocked_spread_window"/> frames' device waits, for their spread.
        /// </summary>
        private readonly double[] blockedRing = new double[blocked_spread_window];
        private int blockedRingIndex;
        private int blockedRingCount;

        private double displayBusy;
        private double displayBlocked;

        private double lastTextRefresh;

        private readonly SpriteText fpsText;
        private readonly SpriteText busyText;
        private readonly SpriteText blockedText;
        private readonly SpriteText blockedSpreadText;
        private readonly FlowContainer timeFlow;
        private readonly SpriteText budgetText;
        private readonly SpriteText loadText;
        private readonly ThreadBarGraph barGraph;

        private PerformanceOverlayState currentState;

        /// <summary>
        /// Budget to measure against when the thread is unthrottled
        /// </summary>
        private double fallbackBudgetMs => host.Window?.DisplayHz > 0 ? 1000.0 / host.Window.DisplayHz : 1000.0 / 60;

        public ThreadStatisticsDisplay(string name, IFrameBasedClock clock, Color baseColor, AppHost host, Func<ThreadFrameStatistics> getStatistics)
        {
            this.name = name;
            this.clock = clock;
            this.baseColor = baseColor;
            this.host = host;
            this.getStatistics = getStatistics;

            Anchor = Anchor.TopRight;
            Origin = Anchor.TopRight;
            Size = new Vector2(overlay_width, row_height_compact);

            Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = Color.Black,
                Alpha = 0.75f
            });

            Add(barGraph = new ThreadBarGraph(this)
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0.5f
            });

            // separator
            Add(new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 1,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Color = Color.White,
                Alpha = 0.25f
            });

            var font = FontUsage.Default.With(size: 14);

            Add(new FlowContainer
            {
                Direction = FlowDirection.Horizontal,
                AutoSizeAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Padding = new MarginPadding { Left = 5, Right = 5, Top = 1 },
                Spacing = new Vector2(column_spacing, 0),
                Children = new Drawable[]
                {
                    column(column_name, new SpriteText
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Text = name,
                        Font = FontUsage.Default.With(size: 14, weight: "Bold"),
                        Color = baseColor
                    }),
                    column(column_fps, fpsText = rightAligned(font, Color.White)),
                    column(column_time, timeFlow = new FlowContainer
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Direction = FlowDirection.Horizontal,
                        AutoSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            busyText = new SpriteText
                            {
                                Anchor = Anchor.TopLeft,
                                Origin = Anchor.TopLeft,
                                Font = font,
                                Color = Color.White,
                                Text = string.Empty
                            },

                            blockedText = new SpriteText
                            {
                                Anchor = Anchor.TopLeft,
                                Origin = Anchor.TopLeft,
                                Font = font,
                                Color = baseColor,
                                Text = string.Empty
                            },
                            blockedSpreadText = new SpriteText
                            {
                                Anchor = Anchor.TopLeft,
                                Origin = Anchor.TopLeft,
                                Font = font,
                                Color = spread_color,
                                Text = string.Empty
                            },
                            new SpriteText
                            {
                                Anchor = Anchor.TopLeft,
                                Origin = Anchor.TopLeft,
                                Font = font,
                                Color = Color.LightGray,
                                Text = "ms"
                            }
                        }
                    }),
                    column(column_budget, budgetText = rightAligned(font, Color.LightGray)),
                    column(column_load, loadText = rightAligned(font, Color.White)),
                }
            });

            return;

            static SpriteText rightAligned(FontUsage font, Color color) => new SpriteText
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Font = font,
                Color = color,
                Text = string.Empty
            };

            static Container column(float width, Drawable content) => new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Size = new Vector2(width, 17),
                Child = content
            };
        }

        public void SetState(PerformanceOverlayState state)
        {
            currentState = state;

            if (state == PerformanceOverlayState.Expanded)
            {
                barGraph.Show();
                Size = new Vector2(overlay_width, row_height_expanded);
            }
            else
            {
                barGraph.Hide();
                Size = new Vector2(overlay_width, row_height_compact);
            }
        }

        public override void Update()
        {
            base.Update();

            if (currentState == PerformanceOverlayState.Hidden)
                return;

            var statistics = getStatistics();

            if (statistics == null)
                return;

            if (cursor < 0)
                cursor = statistics.TotalFrames;

            int count = statistics.Drain(drainBuffer, ref cursor, out _);

            for (int i = 0; i < count; i++)
                record(in drainBuffer[i]);

            // Rebuilt only when a bar has actually been committed. propagateToParent is false because
            // the graph's bounds never change with its data; only its own vertices need regenerating.
            if (currentState == PerformanceOverlayState.Expanded && dataVersion != lastGraphVersion)
            {
                barGraph.Invalidate(InvalidationFlags.DrawInfo, false);
                lastGraphVersion = dataVersion;
            }

            if (Clock.CurrentTime - lastTextRefresh >= text_refresh_ms)
            {
                updateText();
                lastTextRefresh = Clock.CurrentTime;
            }
        }

        private void record(in ThreadFrameSample sample)
        {
            windowBusySum += sample.BusyMilliseconds;
            windowBlockedSum += sample.BlockedMilliseconds;
            windowFrames++;
            latestBudget = sample.BudgetMilliseconds;

            if (sample.MissedDeadline)
                missSlices[missSliceIndex]++;

            blockedRing[blockedRingIndex] = sample.BlockedMilliseconds;
            blockedRingIndex = (blockedRingIndex + 1) % blocked_spread_window;

            if (blockedRingCount < blocked_spread_window)
                blockedRingCount++;

            pendingBucket.Busy = Math.Max(pendingBucket.Busy, sample.BusyMilliseconds);
            pendingBucket.Blocked = Math.Max(pendingBucket.Blocked, sample.BlockedMilliseconds);
            pendingBucket.GCMilliseconds = Math.Max(pendingBucket.GCMilliseconds, sample.GCMilliseconds);
            pendingBucket.Budget = sample.BudgetMilliseconds;
            pendingBucket.Missed |= sample.MissedDeadline;

            pendingBucketElapsed += sample.ElapsedMilliseconds;

            if (pendingBucketElapsed < bucket_ms)
                return;

            pendingBucket.Inactive = host.Window?.IsActive == false;

            history[historyIndex] = pendingBucket;
            historyIndex = (historyIndex + 1) % max_history;

            if (historyCount < max_history)
                historyCount++;

            dataVersion++;

            pendingBucket = default;
            pendingBucketElapsed = 0;
        }

        private void updateText()
        {
            if (windowFrames > 0)
            {
                displayBusy = windowBusySum / windowFrames;
                displayBlocked = windowBlockedSum / windowFrames;

                windowBusySum = 0;
                windowBlockedSum = 0;
                windowFrames = 0;
            }

            int misses = 0;

            foreach (int slice in missSlices)
                misses += slice;

            missSliceIndex = (missSliceIndex + 1) % miss_slice_count;
            missSlices[missSliceIndex] = 0;

            bool throttled = latestBudget > 0;
            double budget = throttled ? latestBudget : fallbackBudgetMs;

            double fps = clock?.FramesPerSecond ?? 0;

            fpsText.Text = fps > 0 ? $"{fps:F0}fps" : "--fps";

            bool blocking = displayBlocked >= 0.05;
            double spread = blocking ? blockedSpread() : 0;

            busyText.Text = formatTime(displayBusy);

            blockedText.Text = blocking ? $"+({formatTime(displayBlocked)}" : string.Empty;
            blockedSpreadText.Text = blocking ? $"±{formatTime(spread)})" : string.Empty;

            budgetText.Text = $"/{(throttled ? string.Empty : "~")}{budget:F1}ms";

            double load = budget > 0 ? displayBusy / budget * 100 : 0;

            loadText.Text = (budget > 0 ? (load < 1 ? $"{load:F2}%" : $"{load:F0}%") : "-")
                            + (misses > 0 ? $" ({misses})" : string.Empty);
            loadText.Color = misses > 0 ? Color.Red : Color.White;
        }

        private static string formatTime(double milliseconds) => milliseconds switch
        {
            >= 100 => $"{milliseconds:F1}",
            >= 1 => $"{milliseconds:F2}",
            _ => $"{milliseconds:F3}"
        };

        /// <summary>
        /// Standard deviation of the device wait over <see cref="blockedRing"/>.
        /// </summary>
        private double blockedSpread()
        {
            if (blockedRingCount == 0)
                return 0;

            double sum = 0;

            for (int i = 0; i < blockedRingCount; i++)
                sum += blockedRing[i];

            double mean = sum / blockedRingCount;
            double sumOfSquaredDeviations = 0;

            for (int i = 0; i < blockedRingCount; i++)
            {
                double deviation = blockedRing[i] - mean;
                sumOfSquaredDeviations += deviation * deviation;
            }

            return Math.Sqrt(sumOfSquaredDeviations / blockedRingCount);
        }

        private struct GraphBucket
        {
            public double Busy;
            public double Blocked;
            [SuppressMessage("ReSharper", "InconsistentNaming")]
            public double GCMilliseconds;
            public double Budget;
            public bool Missed;
            public bool Inactive;
        }

        private sealed partial class ThreadBarGraph : Drawable
        {
            /// <summary>
            /// Six each for the inactive-window wash, the busy segment, and the GC marker.
            /// </summary>
            private const int vertices_per_bucket = 18;

            private readonly ThreadStatisticsDisplay display;

            public ThreadBarGraph(ThreadStatisticsDisplay display)
            {
                this.display = display;
                Blending = BlendingMode.Additive;
                Vertices = new Vertex[max_history * vertices_per_bucket];
            }

            protected internal override VertexTopology Topology => VertexTopology.Triangles;

            // Bars carry non-uniform per-vertex colors; the base color-only fast path
            // would flatten them, so fall back to a full regeneration.
            protected override void UpdateDrawColor() => UpdateTransforms();

            protected override void GenerateVertices()
            {
                float w = DrawSize.X > 0 ? DrawSize.X : 1;
                float h = DrawSize.Y > 0 ? DrawSize.Y : 1;

                // Runs once per committed bar, so it must stay cheap. The model matrix is affine, so
                // decompose it once: p' = origin + x * basisX + y * basisY, two multiply-adds per vertex
                // instead of a full 4x4 transform.
                var matrix = ModelMatrix;
                Vector2 origin = Vector2.Transform(new Vector2(0, 0), matrix);
                Vector2 unitX = Vector2.Transform(new Vector2(1, 0), matrix);
                Vector2 unitY = Vector2.Transform(new Vector2(0, 1), matrix);

                float bxX = unitX.X - origin.X, bxY = unitX.Y - origin.Y;
                float byX = unitY.X - origin.X, byY = unitY.Y - origin.Y;

                Vector2 map(float x, float y) => new Vector2(
                    origin.X + x * bxX + y * byX,
                    origin.Y + x * bxY + y * byY);

                var busyColor = toLinear(display.baseColor, DrawAlpha);
                var inactiveColor = new Vector4(0, 0, 0.5f, DrawAlpha * 0.4f);

                var missColor = toLinear(Color.Red, DrawAlpha);
                var gcColor = toLinear(Color.Orange, DrawAlpha);
                var gcHeavyColor = toLinear(Color.Red, DrawAlpha);

                float barWidth = w / max_history;
                float markerHeight = 3f / h;
                int start = display.historyCount == max_history ? display.historyIndex : 0;
                double fallbackBudget = display.fallbackBudgetMs;

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;

                for (int i = 0; i < max_history; i++)
                {
                    int offset = i * vertices_per_bucket;

                    if (i >= display.historyCount)
                    {
                        for (int v = 0; v < vertices_per_bucket; v++)
                            Vertices[offset + v] = default;

                        continue;
                    }

                    var bucket = display.history[(start + i) % max_history];

                    double budget = bucket.Budget > 0 ? bucket.Budget : fallbackBudget;

                    if (budget <= 0)
                        budget = 1000.0 / 60;

                    float left = i * barWidth / w;
                    float right = (i + 1) * barWidth / w;

                    if (bucket.Inactive)
                        quad(offset, left, right, 0, 1, inactiveColor);
                    else
                        for (int v = 0; v < 6; v++)
                            Vertices[offset + v] = default;

                    // Scaled so the top of the graph is the budget
                    float busyRatio = (float)Math.Clamp(bucket.Busy / budget, 0, 1);

                    quad(offset + 6, left, right, 1 - busyRatio, 1, bucket.Missed ? missColor : busyColor);

                    // The device wait is deliberately not stacked here. It routinely runs longer than
                    // the budget (Metal paces the drawable acquire to the display whatever the frame
                    // limiter asks for), so stacking it turned the draw graph into a solid block that
                    // hid the only thing the graph is for: the shape of our own work over time. The
                    // figure itself is on the row, where it does not crowd anything out.
                    if (bucket.GCMilliseconds > 0)
                        quad(offset + 12, left, right, 0, markerHeight, bucket.GCMilliseconds >= 1 ? gcHeavyColor : gcColor);
                    else
                        for (int v = 0; v < 6; v++) Vertices[offset + 12 + v] = default;
                }

                DrawRectangle = minX <= maxX && minY <= maxY
                    ? new RectangleF(minX, minY, maxX - minX, maxY - minY)
                    : new RectangleF();
                return;

                void quad(int offset, float left, float right, float top, float bottom, Vector4 color)
                {
                    if (bottom - top <= 0)
                    {
                        for (int v = 0; v < 6; v++)
                            Vertices[offset + v] = default;

                        return;
                    }

                    var topLeft = map(left, top);
                    var topRight = map(right, top);
                    var bottomLeft = map(left, bottom);
                    var bottomRight = map(right, bottom);

                    minX = Math.Min(minX, Math.Min(topLeft.X, bottomRight.X));
                    minY = Math.Min(minY, Math.Min(topLeft.Y, bottomRight.Y));
                    maxX = Math.Max(maxX, Math.Max(topLeft.X, bottomRight.X));
                    maxY = Math.Max(maxY, Math.Max(topLeft.Y, bottomRight.Y));

                    Vertices[offset + 0] = new Vertex { Position = topLeft, Color = color };
                    Vertices[offset + 1] = new Vertex { Position = topRight, Color = color };
                    Vertices[offset + 2] = new Vertex { Position = bottomRight, Color = color };
                    Vertices[offset + 3] = new Vertex { Position = bottomRight, Color = color };
                    Vertices[offset + 4] = new Vertex { Position = bottomLeft, Color = color };
                    Vertices[offset + 5] = new Vertex { Position = topLeft, Color = color };
                }
            }

            private static Vector4 toLinear(Color color, float alpha) => new Vector4(
                ColorExtensions.SrgbToLinear(color.R),
                ColorExtensions.SrgbToLinear(color.G),
                ColorExtensions.SrgbToLinear(color.B),
                alpha * (color.A / 255f));
        }
    }
}
