// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using System.Reflection;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Testing;

public class TestBrowserApp : App
{
    private Container testContentContainer;
    private FlowContainer testListFlow;
    private FlowContainer stepsFlow;
    private TestScene currentTest;

    private readonly Assembly testAssembly;

    public TestBrowserApp(Assembly testAssembly = null!)
    {
        this.testAssembly = testAssembly ?? Assembly.GetEntryAssembly();
    }

    public override void Load()
    {
        base.Load();

        testContentContainer = new Container
        {
            Size = new Vector2(1),
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre
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
        var leftSidebar = new Container
        {
            Size = new Vector2(250, 1),
            RelativeSizeAxes = Axes.Y,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        };

        leftSidebar.Add(new Box
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
            Padding = new MarginPadding(10),
            Size = new Vector2(1, 0),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        };

        leftScroll.Add(testListFlow);
        leftSidebar.Add(leftScroll);
        Add(leftSidebar);

        var rightSidebar = new Container
        {
            Size = new Vector2(250, 1),
            RelativeSizeAxes = Axes.Y,
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
        };
        rightSidebar.Add(new Box
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
            Padding = new MarginPadding(10),
            Size = new Vector2(1, 0),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft
        };

        rightScroll.Add(stepsFlow);
        rightSidebar.Add(rightScroll);
        Add(rightSidebar);

        loadTestClasses();
    }

    private void loadTestClasses()
    {
        var testTypes = testAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(TestScene)) && !t.IsAbstract)
            .OrderBy(t => t.Name)
            .ToList();

        foreach (var type in testTypes)
        {
            var btn = new BrowserButton(type.Name, () => loadTest(type), Color.DarkGray);
            testListFlow.Add(btn);
            Logger.Log($"[TestBrowser] Found test: {type.Name}");
        }
    }

    private void loadTest(Type testSceneType)
    {
        if (currentTest != null)
        {
            testContentContainer.Remove(currentTest);
        }

        foreach (var child in stepsFlow.Children.ToArray())
        {
            stepsFlow.Remove(child);
        }

        currentTest = (TestScene)Activator.CreateInstance(testSceneType);
        testContentContainer.Add(currentTest);

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

    private class BrowserButton : ClickableContainer
    {
        public BrowserButton(string text, Action action, Color bgColor)
        {
            RelativeSizeAxes = Axes.X;
            Height = 40;
            Width = 1;
            Action = action;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;
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
                Size = new Vector2(100, 100),
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre
            });
        }
    }
}
