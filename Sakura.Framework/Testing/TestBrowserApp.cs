// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using System.Reflection;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Testing;

public class TestBrowserApp : App
{
    private Container testContentContainer;
    private Container testSidebar;
    private Container stepSidebar;
    private FlowContainer testListFlow;
    private FlowContainer stepsFlow;
    private TestScene currentTest;
    private SpriteText hotReloadText;

    private readonly Assembly testAssembly;

    private const int sidebar_width = 150;

    public TestBrowserApp(Assembly testAssembly = null!)
    {
        this.testAssembly = testAssembly ?? Assembly.GetEntryAssembly();
    }

    public override void Load()
    {
        base.Load();

        testContentContainer = new Container
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight
        };
        Add(testContentContainer);
        // testContentContainer.Add(new Box()
        // {
        //     Anchor = Anchor.Centre,
        //     Origin = Anchor.Centre,
        //     RelativeSizeAxes = Axes.Both,
        //     Size = new Vector2(1),
        //     Color = Color.GreenYellow
        // });

        // 2. Left Sidebar (List of Tests)
        testSidebar = new Container
        {
            Size = new Vector2(sidebar_width, 1),
            RelativeSizeAxes = Axes.Y,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        };

        testSidebar.Add(new Box
        {
            RelativeSizeAxes = Axes.Both,
            Color = Color.DarkBlue,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1)
        });

        var leftScroll = new ScrollableContainer
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        };

        testListFlow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Spacing = new Vector2(0, 5),
            Padding = new MarginPadding(2),
            Size = new Vector2(0.98f, 0),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        };

        leftScroll.Add(testListFlow);
        testSidebar.Add(leftScroll);
        Add(testSidebar);

        stepSidebar = new Container
        {
            Size = new Vector2(sidebar_width, 1),
            RelativeSizeAxes = Axes.Y,
            Position = new Vector2(sidebar_width, 0),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
        };
        stepSidebar.Add(new Box
        {
            Size = new Vector2(1),
            RelativeSizeAxes = Axes.Both,
            Color = Color.DarkGreen,
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight
        });

        var rightScroll = new ScrollableContainer
        {
            Size = new Vector2(1),
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight
        };

        stepsFlow = new FlowContainer
        {
            Direction = FlowDirection.Vertical,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Spacing = new Vector2(0, 5),
            Padding = new MarginPadding(2),
            Size = new Vector2(0.98f, 0),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        };

        rightScroll.Add(stepsFlow);
        stepSidebar.Add(rightScroll);
        Add(stepSidebar);

        hotReloadText = new SpriteText
        {
            Text = "Hot Reloaded!",
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Color = Color.LightGreen,
            Depth = float.MaxValue,
            Alpha = 0
        };
        Add(hotReloadText);

        loadTestClasses();

        HotReloadManager.OnHotReload += () =>
        {
            Scheduler.Add(() =>
            {
                if (currentTest != null)
                {
                    Logger.Log("[TestBrowser] 🔄 Code changes detected! Hot Reloading current test...");
                    loadTest(currentTest.GetType());
                }
                hotReloadText.FadeIn(200).Wait(1000).FadeOut(200);
            });
        };
    }

    public override void LoadComplete()
    {
        base.LoadComplete();
        Window.GetPhysicalSize(out int physW, out int physH);
        testContentContainer.Size = new Vector2(physW - 2 * sidebar_width, physH);
        Window.Resized += (w, h) => testContentContainer.Size = new Vector2(w - 2 * sidebar_width, h);
    }

    private void loadTestClasses()
    {
        var testGroups = testAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(TestScene)) && !t.IsAbstract)
            .GroupBy(t => t.Namespace ?? "Unknown")
            .OrderBy(g => g.Key)
            .ToList();

        string assemblyName = testAssembly.GetName().Name ?? string.Empty;

        foreach (var group in testGroups)
        {
            string headerText = group.Key;
            if (!string.IsNullOrEmpty(assemblyName) && headerText.StartsWith(assemblyName))
            {
                headerText = headerText.Substring(assemblyName.Length).TrimStart('.');
                if (headerText.StartsWith("Visuals"))
                    headerText = headerText.Substring("Visuals".Length).TrimStart('.');
            }

            if (string.IsNullOrEmpty(headerText))
                headerText = "Uncategorized";

            testListFlow.Add(new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = headerText,
                Font = FontUsage.Default.With(size: 16),
                Color = Color.Yellow,
                Margin = new MarginPadding
                {
                    Top = 5,
                    Bottom = 5
                }
            });

            foreach (var type in group.OrderBy(t => t.Name))
            {
                var btn = new BrowserButton(type.Name, () => loadTest(type), Color.DarkGray);
                testListFlow.Add(btn);
            }
        }
    }

    private void loadTest(Type testSceneType)
    {
        if (currentTest != null)
        {
            currentTest.RunTearDownMethods();
            testContentContainer.Remove(currentTest);
        }

        AudioManager.StopAll();

        foreach (var child in stepsFlow.Children.ToArray())
        {
            stepsFlow.Remove(child);
        }

        currentTest = (TestScene)Activator.CreateInstance(testSceneType);
        currentTest.RunSetUpMethods();
        testContentContainer.Add(currentTest);

        currentTest.Clock = new FramedClock(Clock, true);

        foreach (var step in currentTest.Steps)
        {
            Color btnColor = step.IsAssert ? Color.DarkRed : Color.DarkBlue;

            var stepBtn = new BrowserButton(step.Description, () =>
            {
                try
                {
                    step.Action?.Invoke();
                    Logger.Log($"[Test] Executed step: {step.Description}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Test] Step failed: {step.Description}", ex);
                }
            }, btnColor);

            stepsFlow.Add(stepBtn);
        }
    }

    private bool isTestListVisible = true;

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Key == Key.BackSlash && e.ControlPressed)
        {
            toggleTestList();
            return true;
        }

        return base.OnKeyDown(e);
    }

    private void toggleTestList()
    {
        isTestListVisible = !isTestListVisible;

        if (isTestListVisible)
        {
            testSidebar.Show();
            stepSidebar.X = sidebar_width;
            testContentContainer.Margin = new MarginPadding { Left = sidebar_width * 2 };
        }
        else
        {
            testSidebar.Hide();
            stepSidebar.X = 0;
            testContentContainer.Margin = new MarginPadding { Left = sidebar_width };
        }
    }

    private class BrowserButton : ClickableContainer
    {
        public BrowserButton(string text, Action action, Color bgColor)
        {
            RelativeSizeAxes = Axes.X;
            Height = 30;
            Width = 1;
            Action = action;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;
            Masking = true;
            Name = text;

            Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = bgColor,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft
            });

            Add(new SpriteText
            {
                Text = text,
                Font = FontUsage.Default.With(size: 15),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft
            });
        }
    }
}
