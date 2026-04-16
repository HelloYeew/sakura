// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
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
    private Container headerContainer;
    private ScrollableContainer stepScrollContainer;

    private readonly Assembly testAssembly;

    private const int sidebar_width = 150;

    private bool isAutoRunEnabled;
    private int currentAutoRunStep;
    private const int header_height = 40;

    private bool isWaitingForStep;
    private double stepWaitStartTime;

    public TestBrowserApp(Assembly testAssembly = null!)
    {
        this.testAssembly = testAssembly ?? Assembly.GetEntryAssembly();
    }

    public override void Load()
    {
        TestScene.IsVisualRunner = true;
        base.Load();

        testContentContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Margin = new MarginPadding
            {
                Top = header_height,
                Left = sidebar_width * 2
            }
        };
        Add(testContentContainer);

        // Top header
        headerContainer = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = header_height,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(1, header_height),
            Depth = -1
        };

        headerContainer.Add(new Box
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Color = Color.DarkSlateGray,
        });

        var headerFlow = new FlowContainer
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Size = new Vector2(0, 1),
            Direction = FlowDirection.Horizontal,
            RelativeSizeAxes = Axes.Y,
            AutoSizeAxes = Axes.X,
            Spacing = new Vector2(5, 0),
            Padding = new MarginPadding(5)
        };

        headerFlow.Add(new HeaderButton("Restart", () =>
        {
            if (currentTest != null)
                loadTest(currentTest.GetType());
        }, Color.Transparent));

        var autoRunText = new SpriteText
        {
            Text = "Auto Run: OFF",
            Font = FontUsage.Default.With(size: 15),
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre
        };

        headerFlow.Add(new HeaderButton("", () =>
        {
            isAutoRunEnabled = !isAutoRunEnabled;
            autoRunText.Text = $"Auto Run: {(isAutoRunEnabled ? "ON" : "OFF")}";
            autoRunText.Color = isAutoRunEnabled ? Color.GreenYellow : Color.White;

            if (isAutoRunEnabled && currentTest != null)
            {
                currentAutoRunStep = 0;
                runNextStep();
            }
        }, Color.DarkSlateBlue)
        {
            Child = autoRunText
        });

        headerContainer.Add(headerFlow);
        Add(headerContainer);

        // Left Sidebar (List of Tests)
        testSidebar = new Container
        {
            Size = new Vector2(sidebar_width, 1),
            RelativeSizeAxes = Axes.Y,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Y = header_height,
            Padding = new MarginPadding
            {
                Bottom = header_height
            }
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
            Position = new Vector2(sidebar_width, header_height),
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Padding = new MarginPadding
            {
                Bottom = header_height
            }
        };

        stepSidebar.Add(new Box
        {
            Size = new Vector2(1),
            RelativeSizeAxes = Axes.Both,
            Color = Color.DarkGreen,
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight
        });

        stepScrollContainer = new ScrollableContainer
        {
            Size = new Vector2(1),
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            AutoHideScrollbars = true
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

        stepScrollContainer.Add(stepsFlow);
        stepSidebar.Add(stepScrollContainer);
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
                Font = FontUsage.Default.With(size: 14),
                Color = Color.Yellow,
                Margin = new MarginPadding
                {
                    Top = 5,
                    Bottom = 5
                }
            });

            foreach (var type in group.OrderBy(t => t.Name))
            {
                var button = new TestBrowserButton(type.Name, () => loadTest(type), Color.DarkGray);
                testListFlow.Add(button);
            }
        }
    }

    private void loadTest(Type testSceneType)
    {
        if (currentTest != null)
        {
            currentTest.RunOneTimeTearDownMethods();
            testContentContainer.Remove(currentTest);
        }

        AudioManager.StopAll();

        foreach (var child in stepsFlow.Children.ToArray())
        {
            stepsFlow.Remove(child);
        }

        currentTest = (TestScene)Activator.CreateInstance(testSceneType);
        testContentContainer.Add(currentTest);

        currentTest.Clock = new FramedClock(Clock, true);
        currentTest.CurrentStepContext = StepContext.OneTimeSetUp;
        currentTest.RunOneTimeSetUpMethods();

        var allMethods = testSceneType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var method in allMethods)
        {
            // [Ignore] and [Explicit]
            var ignoreAttr = method.GetCustomAttribute<IgnoreAttribute>();
            if (ignoreAttr != null)
            {
                currentTest.AddLabel($"[Ignored] {method.Name} - {ignoreAttr.Reason}");
                continue;
            }

            if (method.GetCustomAttribute<ExplicitAttribute>() != null)
            {
                currentTest.AddLabel($"[Explicit] {method.Name} (Skipped)");
                continue;
            }

            var testAttr = method.GetCustomAttribute<TestAttribute>();
            var testCases = method.GetCustomAttributes<TestCaseAttribute>().ToArray();
            var testCaseSources = method.GetCustomAttributes<TestCaseSourceAttribute>().ToArray();

            // [Test]
            if (testAttr != null && testCases.Length == 0 && testCaseSources.Length == 0)
            {
                currentTest.AddLabel(method.Name);
                currentTest.CurrentStepContext = StepContext.SetUp;
                currentTest.RunSetUpMethods();
                currentTest.CurrentStepContext = StepContext.Test;
                method.Invoke(currentTest, null);
                currentTest.CurrentStepContext = StepContext.TearDown;
                currentTest.RunTearDownMethods();
            }

            // [TestCase]
            if (testCases.Length > 0)
            {
                foreach (var testCase in testCases)
                {
                    string argsString = string.Join(", ", testCase.Arguments.Select(a => a?.ToString() ?? "null"));
                    currentTest.AddLabel($"{method.Name}({argsString})");

                    currentTest.CurrentStepContext = StepContext.SetUp;
                    currentTest.RunSetUpMethods();
                    currentTest.CurrentStepContext = StepContext.Test;
                    method.Invoke(currentTest, testCase.Arguments);
                    currentTest.CurrentStepContext = StepContext.TearDown;
                    currentTest.RunTearDownMethods();
                }
            }

            // [TestCaseSource]
            if (testCaseSources.Length > 0)
            {
                foreach (var sourceAttr in testCaseSources)
                {
                    var sourceType = sourceAttr.SourceType ?? testSceneType;
                    IEnumerable sourceData = getTestCaseSourceData(sourceType, sourceAttr.SourceName, currentTest);

                    if (sourceData != null)
                    {
                        foreach (var data in sourceData)
                        {
                            object[] args = data as object[] ?? new object[] { data };
                            string argsString = string.Join(", ", args.Select(a => a?.ToString() ?? "null"));

                            currentTest.AddLabel($"{method.Name}({argsString})");
                            currentTest.CurrentStepContext = StepContext.SetUp;
                            currentTest.RunSetUpMethods();
                            currentTest.CurrentStepContext = StepContext.Test;
                            method.Invoke(currentTest, args);
                            currentTest.CurrentStepContext = StepContext.TearDown;
                            currentTest.RunTearDownMethods();
                        }
                    }
                }
            }
        }

        foreach (var step in currentTest.Steps)
        {
            if (step.IsLabel)
            {
                stepsFlow.Add(new SpriteText
                {
                    Text = step.Description,
                    Font = FontUsage.Default.With(size: 14),
                    Color = Color.Yellow,
                    Margin = new MarginPadding
                    {
                        Top = 10,
                        Bottom = 5
                    },
                    Name = step.Description
                });
                continue;
            }

            bool isWait = step.WaitTime > 0 || step.WaitCondition != null;

            Color buttonColor = Color.DarkBlue;

            if (step.IsAssert)
                buttonColor = Color.DarkRed;
            else if (isWait)
                buttonColor = Color.DarkCyan;

            switch (step.Context)
            {
                case StepContext.OneTimeSetUp:
                    buttonColor = buttonColor.Darken(0.3f);
                    break;
                case StepContext.SetUp:
                    buttonColor = buttonColor.Darken(0.3f);
                    break;
                case StepContext.TearDown:
                    buttonColor = buttonColor.Darken(0.3f);
                    break;
            }

            TestStepButton stepButton = null!;

            stepButton = new TestStepButton(step.Description, () =>
            {
                try
                {
                    stepButton.Flash();
                    if (isWait)
                    {
                        if (step.WaitCondition != null && !step.WaitCondition())
                        {
                            throw new Exception("Wait condition not met.");
                        }
                    }
                    else
                    {
                        step.Action.Invoke();
                    }

                    stepButton.SetState(true);
                    Logger.Log($"Executed step: {step.Description}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Test] Step failed: {step.Description}", ex);
                }
            }, buttonColor);

            stepsFlow.Add(stepButton);
        }

        if (isAutoRunEnabled)
        {
            currentAutoRunStep = 0;
            Scheduler.AddDelayed(runNextStep, 200);
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
            testContentContainer.Margin = new MarginPadding
            {
                Top = header_height,
                Left = sidebar_width * 2
            };
        }
        else
        {
            testSidebar.Hide();
            stepSidebar.X = 0;
            testContentContainer.Margin = new MarginPadding
            {
                Top = header_height,
                Left = sidebar_width
            };
        }
    }

    private void runNextStep()
    {
        if (!isAutoRunEnabled || currentTest == null || currentAutoRunStep >= currentTest.Steps.Count)
            return;

        var step = currentTest.Steps[currentAutoRunStep];

        var button = stepsFlow.Children.Count > currentAutoRunStep ? stepsFlow.Children[currentAutoRunStep] as TestStepButton : null;

        var currentItem = stepsFlow.Children.Count > currentAutoRunStep ? stepsFlow.Children[currentAutoRunStep] : null;
        if (currentItem != null)
        {
            stepScrollContainer.ScrollIntoView(currentItem);
        }

        if (step.IsLabel)
        {
            currentAutoRunStep++;
            Scheduler.AddDelayed(runNextStep, 10);
            return;
        }

        if (!isWaitingForStep)
        {
            button.Flash();

            try
            {
                step.Action?.Invoke();
                Logger.Log($"[Test] Auto-executed step: {step.Description}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Test] Step failed: {step.Description}", ex);
                button?.SetState(false);
                isAutoRunEnabled = false;
                return;
            }

            if (step.WaitTime > 0 || step.WaitCondition != null)
            {
                isWaitingForStep = true;
                stepWaitStartTime = currentTest.Clock.CurrentTime;
            }
            else
            {
                button.SetState(true);
            }
        }

        if (isWaitingForStep)
        {
            double elapsed = currentTest.Clock.CurrentTime - stepWaitStartTime;

            if (step.WaitTime > 0 && elapsed < step.WaitTime)
            {
                Scheduler.AddDelayed(runNextStep, 10);
                return;
            }

            if (step.WaitCondition != null && !step.WaitCondition())
            {
                if (step.HasTimeout && elapsed > step.Timeout)
                {
                    Logger.Error($"[Test] Auto-run timed out on step: {step.Description}");
                    button?.SetState(false);
                    isAutoRunEnabled = false;
                    return;
                }
                Scheduler.AddDelayed(runNextStep, 10);
                return;
            }

            isWaitingForStep = false;
            button?.SetState(true);
        }

        currentAutoRunStep++;
        Scheduler.AddDelayed(runNextStep, 200);
    }

    private IEnumerable getTestCaseSourceData(Type type, string name, object instance)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        var prop = type.GetProperty(name, flags);
        if (prop != null) return (IEnumerable)prop.GetValue(prop.GetMethod.IsStatic ? null : instance);

        var method = type.GetMethod(name, flags);
        if (method != null) return (IEnumerable)method.Invoke(method.IsStatic ? null : instance, null);

        var field = type.GetField(name, flags);
        if (field != null) return (IEnumerable)field.GetValue(field.IsStatic ? null : instance);

        return null;
    }

    private class TestBrowserButton : ClickableContainer
    {
        private Box backgroundBox;
        private Color originalBackgroundColor;

        public TestBrowserButton(string text, Action action, Color backgroundColor)
        {
            RelativeSizeAxes = Axes.X;
            Height = 30;
            Width = 1;
            Action = action;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;
            Masking = true;
            Name = text;

            Add(backgroundBox = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = backgroundColor,
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

            originalBackgroundColor = backgroundColor;
        }

        public override bool OnHover(MouseEvent e)
        {
            backgroundBox.Color = originalBackgroundColor;
            backgroundBox.FadeToColour(originalBackgroundColor.Lighten(0.5f), 50, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            backgroundBox.FadeToColour(originalBackgroundColor, 50, Easing.OutQuint);
            return base.OnHoverLost(e);
        }

        public void Flash()
        {
            backgroundBox.Color = originalBackgroundColor;
            backgroundBox.FlashColour(Color.White, 500, Easing.OutQuint);
        }
    }

    private class TestStepButton : ClickableContainer
    {
        private Box backgroundBox;
        private Box statusBox;
        private Color originalBackgroundColor;

        public TestStepButton(string text, Action action, Color backgroundColor)
        {
            RelativeSizeAxes = Axes.X;
            Height = 30;
            Width = 1;
            Action = action;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;
            Masking = true;
            Name = text;

            Add(backgroundBox = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = backgroundColor,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft
            });

            Add(statusBox = new Box
            {
                RelativeSizeAxes = Axes.Y,
                Size = new Vector2(8, 1),
                Color = Color.Red,
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft
            });

            Add(new SpriteText
            {
                Text = text,
                Font = FontUsage.Default.With(size: 15),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Position = new Vector2(10, 0)
            });

            originalBackgroundColor = backgroundColor;
        }

        public void SetState(bool isSuccess)
        {
            statusBox.Color = isSuccess ? Color.LimeGreen : Color.Red;
            statusBox.FlashColour(isSuccess ? Color.LimeGreen.Lighten(0.5f) : Color.Red.Lighten(0.5f), 500, Easing.OutQuint);
        }

        public override bool OnHover(MouseEvent e)
        {
            backgroundBox.Color = originalBackgroundColor;
            backgroundBox.FadeToColour(originalBackgroundColor.Lighten(0.5f), 50, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            backgroundBox.FadeToColour(originalBackgroundColor, 50, Easing.OutQuint);
            return base.OnHoverLost(e);
        }

        public void Flash()
        {
            backgroundBox.Color = originalBackgroundColor;
            backgroundBox.FlashColour(Color.White, 500, Easing.OutQuint);
        }
    }

    private class HeaderButton : ClickableContainer
    {
        public HeaderButton(string text, Action action, Color bgColor)
        {
            RelativeSizeAxes = Axes.Y;
            Width = 120;
            Height = 1;
            Action = action;
            Masking = true;
            CornerRadius = 5;

            Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Color = bgColor
            });

            if (!string.IsNullOrEmpty(text))
            {
                Add(new SpriteText
                {
                    Text = text,
                    Font = FontUsage.Default.With(size: 15),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                });
            }
        }
    }
}
