// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections;
using System.Linq;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Sakura.Framework.Configurations;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Reactive;
using Sakura.Framework.Timing;
using Vector2 = Sakura.Framework.Maths.Vector2;

namespace Sakura.Framework.Testing;

public partial class TestBrowserApp : App
{
    private DrawSizePreservingFillContainer mainContainer;
    private Container testContentContainer;
    private Box testContentBackgroundBox;
    private Container testSidebar;
    private Container stepSidebar;
    private FlowContainer testListFlow;
    private FlowContainer stepsFlow;
    private TestScene currentTest;
    private SpriteText hotReloadText;
    private Container headerContainer;
    private ScrollableContainer stepScrollContainer;
    private BasicSliderBar<double> volumeSlider;
    private BasicSliderBar<double> clockRateSlider;
    private SpriteText clockRateLabel;
    private BasicCheckbox autoRunCheckbox;
    private BasicTextBox searchTextBox;

    private readonly Assembly testAssembly;

    /// <summary>
    /// A rate-adjustable clock that drives all test content.
    /// Wraps the app clock so the sidebars and header are unaffected by rate changes.
    /// </summary>
    private FramedClock testClock = null!;

    private const int sidebar_width = 150;

    private ReactiveBool autoRunEnabled = new ReactiveBool(false);
    private int currentAutoRunStep;
    private const int header_height = 40;

    private bool isWaitingForStep;
    private double stepWaitStartTime;

    /// <summary>
    /// Incremented every time a new test is loaded. Stale <see cref="runNextStep"/>
    /// callbacks captured an older generation and exit immediately, preventing
    /// double-speed execution when a test is switched mid-run.
    /// </summary>
    private int runGeneration;

    /// <summary>
    /// Initial rate for the test clock. Useful for headless CI runs where you want
    /// tests to execute faster than real-time (e.g. pass 2.0 for 2× speed).
    /// </summary>
    public double InitialClockRate { get; init; } = 1.0;

    public TestBrowserApp(Assembly testAssembly = null!)
    {
        this.testAssembly = testAssembly ?? Assembly.GetEntryAssembly();
    }

    public override void Load()
    {
        TestScene.IsVisualRunner = true;
        base.Load();

        Add(new SafeAreaContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = mainContainer = new DrawSizePreservingFillContainer
            {
                RelativeSizeAxes = Axes.Both
            }
        });

        // Build the rate-adjustable clock that drives test content only.
        // InitialClockRate can be set to >1 for headless/CI runs to speed up tests.
        testClock = new FramedClock(Clock) { Rate = InitialClockRate };

        mainContainer.Add(testContentBackgroundBox = new Box()
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Margin = new MarginPadding
            {
                Top = header_height,
                Left = sidebar_width * 2
            },
            Color = Color.Black
        });

        mainContainer.Add(testContentContainer = new Container
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
        });

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

        headerFlow.Add(autoRunCheckbox = new BasicCheckbox()
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft
        });

        autoRunCheckbox.Current.BindTo(autoRunEnabled);

        autoRunCheckbox.Current.ValueChanged += e =>
        {
            autoRunEnabled.Value = e.NewValue;

            if (e.NewValue)
            {
                currentAutoRunStep = 0;
                runNextStep(runGeneration);
            }
        };

        headerFlow.Add(new SpriteText()
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = "Auto Run",
            Margin = new MarginPadding { Right = 20 },
            Font = FontUsage.Default.With(size: 15)
        });

        headerFlow.Add(new SpriteText()
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = "Volume",
            Font = FontUsage.Default.With(size: 15)
        });

        headerFlow.Add(volumeSlider = new BasicSliderBar<double>()
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Size = new Vector2(150, 20),
            MinValue = 0,
            MaxValue = 1
        });

        var volumeReactive = Host.FrameworkConfigManager.Get<double>(FrameworkSetting.MasterVolume);
        volumeSlider.Current.Value = volumeReactive.Value;

        volumeReactive.BindValueChanged(e => volumeSlider.Current.Value = e.NewValue, true);

        headerFlow.Add(new SpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = "Rate",
            Font = FontUsage.Default.With(size: 15),
            Margin = new MarginPadding { Left = 10, Right = 10 }
        });

        headerFlow.Add(clockRateSlider = new BasicSliderBar<double>
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Size = new Vector2(120, 20),
            MinValue = 0.0,
            MaxValue = 4.0
        });

        clockRateSlider.Current.Value = InitialClockRate;

        headerFlow.Add(new ClickableContainer
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            AutoSizeAxes = Axes.X,
            RelativeSizeAxes = Axes.Y,
            Margin = new MarginPadding { Left = 4 },
            Action = () => clockRateSlider.Current.Value = 1.0,
            Child = clockRateLabel = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = "1.00×",
                Font = FontUsage.Default.With(size: 14),
                Color = Color.LightGreen,
                Width = 60
            }
        });

        headerFlow.Add(new HeaderButton("Background", () =>
        {
            testContentBackgroundBox.FadeToColor(ColorExtensions.GetRandomColor(), 250, Easing.OutQuint);
        }, Color.Transparent));

        clockRateSlider.Current.ValueChanged += e =>
        {
            testClock.Rate = e.NewValue;
            clockRateLabel.Text = $"{e.NewValue:F2}×";
        };

        headerContainer.Add(headerFlow);
        mainContainer.Add(headerContainer);

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

        testSidebar.Add(searchTextBox = new BasicTextBox
        {
            RelativeSizeAxes = Axes.X,
            Width = 0.95f,
            Height = 25,
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            PlaceholderText = "Search...",
            Margin = new MarginPadding { Top = 5 }
        });

        searchTextBox.Text.ValueChanged += e =>
        {
            testListFlow.Clear();
            loadTestClasses(e.NewValue);
        };

        var scrollWrapper = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Size = new Vector2(1),
            Padding = new MarginPadding
            {
                Top = 35
            }
        };

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
        scrollWrapper.Add(leftScroll);
        testSidebar.Add(scrollWrapper);
        mainContainer.Add(testSidebar);

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
        mainContainer.Add(stepSidebar);

        hotReloadText = new SpriteText
        {
            Text = "Hot Reloaded!",
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Color = Color.LightGreen,
            Depth = float.MaxValue,
            Alpha = 0
        };
        mainContainer.Add(hotReloadText);

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

    public override void Update()
    {
        base.Update();
        testClock.ProcessFrame();
    }

    private void loadTestClasses(string searchQuery = "")
    {
        var testGroups = testAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(TestScene)) && !t.IsAbstract)
            .Where(t => string.IsNullOrEmpty(searchQuery) || t.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
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
                string displayName = type.Name.StartsWith("Test") ? type.Name.Substring(4) : type.Name;
                var button = new TestBrowserButton(displayName, () => loadTest(type), Color.DarkGray);
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

        // cancel any in-flight runNextStep callbacks from the previous test.
        runGeneration++;
        isWaitingForStep = false;
        currentAutoRunStep = 0;

        currentTest = (TestScene)Activator.CreateInstance(testSceneType);
        testContentContainer.Add(currentTest);

        currentTest.Clock = new FramedClock(testClock, true);
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
            generateStepVisual((dynamic)step);
        }

        if (autoRunEnabled)
        {
            currentAutoRunStep = 0;
            scheduleNextStep(200, runGeneration);
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

    private void scheduleNextStep(double delayMs, int generation)
    {
        double rate = testClock.Rate > 0 ? testClock.Rate : 1.0;
        Scheduler?.AddDelayed(() => runNextStep(generation), delayMs / rate);
    }

    private void runNextStep(int generation)
    {
        // Stale callback — a new test was loaded after this was scheduled.
        if (generation != runGeneration) return;

        if (!autoRunEnabled || currentTest == null || currentAutoRunStep >= currentTest.Steps.Count)
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
            scheduleNextStep(10, generation);
            return;
        }

        if (step.GetType().IsGenericType && step.GetType().GetGenericTypeDefinition() == typeof(SliderStep<>))
        {
            currentAutoRunStep++;
            scheduleNextStep(10, generation);
            return;
        }

        if (!isWaitingForStep)
        {
            button?.Flash();

            try
            {
                if (step is RepeatStep repeatStep)
                {
                    repeatStep.Action?.Invoke();
                    repeatStep.CurrentIteration++;

                    button?.UpdateText($"{repeatStep.Description} ({repeatStep.CurrentIteration}/{repeatStep.RepeatCount})");

                    if (repeatStep.CurrentIteration < repeatStep.RepeatCount)
                    {
                        scheduleNextStep(10, generation);
                        return;
                    }

                    repeatStep.CurrentIteration = 0;
                    button?.SetState(true);
                }
                else if (step is ActionStep actionStep)
                {
                    actionStep.Action?.Invoke();
                    Logger.Log($"[Test] Auto-executed step: {step.Description}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[Test] Step failed: {step.Description}", ex);
                button?.SetState(false);
                autoRunEnabled.Value = false;
                return;
            }

            // Check if we need to start waiting
            if (step is WaitStep waitStep)
            {
                isWaitingForStep = true;
                stepWaitStartTime = currentTest.Clock.CurrentTime;
            }
            else
            {
                button?.SetState(true);
            }
        }

        if (isWaitingForStep && step is WaitStep currentWaitStep)
        {
            double elapsed = currentTest.Clock.CurrentTime - stepWaitStartTime;

            if (currentWaitStep.WaitTime > 0 && elapsed < currentWaitStep.WaitTime)
            {
                scheduleNextStep(10, generation);
                return;
            }

            if (currentWaitStep.WaitCondition != null && !currentWaitStep.WaitCondition())
            {
                if (currentWaitStep.HasTimeout && elapsed > currentWaitStep.Timeout)
                {
                    Logger.Error($"[Test] Auto-run timed out on step: {step.Description}");
                    button?.SetState(false);
                    autoRunEnabled.Value = false;
                    return;
                }
                scheduleNextStep(10, generation);
                return;
            }

            isWaitingForStep = false;
            button?.SetState(true);
        }

        currentAutoRunStep++;
        scheduleNextStep(200, generation);
    }

    private void generateStepVisual(ActionStep step)
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
            return;
        }

        Color buttonColor = step.IsAssert ? Color.DarkRed : Color.DarkBlue;

        switch (step.Context)
        {
            case StepContext.OneTimeSetUp:
            case StepContext.SetUp:
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
                step.Action?.Invoke();
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

    private void generateStepVisual(WaitStep step)
    {
        Color buttonColor = Color.DarkCyan;

        switch (step.Context)
        {
            case StepContext.OneTimeSetUp:
            case StepContext.SetUp:
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
                if (step.WaitCondition != null && !step.WaitCondition())
                {
                    throw new Exception("Wait condition not met.");
                }

                stepButton.SetState(true);
                Logger.Log($"Executed wait step: {step.Description}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Test] Step failed: {step.Description}", ex);
            }
        }, buttonColor);

        stepsFlow.Add(stepButton);
    }

    private void generateStepVisual<T>(SliderStep<T> step) where T : struct, INumber<T>, IMinMaxValue<T>
    {
        stepsFlow.Add(new TestSliderStepControl<T>(step));
    }

    private void generateStepVisual(RepeatStep step)
    {
        Color buttonColor = Color.DarkMagenta;

        TestStepButton stepButton = null!;
        stepButton = new TestStepButton($"{step.Description} (0/{step.RepeatCount})", () =>
        {
            try
            {
                stepButton.Flash();
                for (int i = 0; i < step.RepeatCount; i++)
                {
                    step.Action?.Invoke();
                }
                stepButton.UpdateText($"{step.Description} ({step.RepeatCount}/{step.RepeatCount})");
                stepButton.SetState(true);
                Logger.Log($"Executed repeat step: {step.Description} x{step.RepeatCount}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[Test] Step failed: {step.Description}", ex);
                stepButton.SetState(false);
            }
        }, buttonColor);

        stepsFlow.Add(stepButton);
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

    private partial class TestBrowserButton : ClickableContainer
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
            backgroundBox.FadeToColor(originalBackgroundColor.Lighten(0.5f), 50, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            backgroundBox.FadeToColor(originalBackgroundColor, 50, Easing.OutQuint);
            return base.OnHoverLost(e);
        }

        public void Flash()
        {
            backgroundBox.Color = originalBackgroundColor;
            backgroundBox.FlashColor(Color.White, 500, Easing.OutQuint);
        }
    }

    private partial class TestStepButton : ClickableContainer
    {
        private Box backgroundBox;
        private Box statusBox;
        private Color originalBackgroundColor;
        private SpriteText drawableText;

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

            Add(drawableText = new SpriteText
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
            statusBox.FlashColor(isSuccess ? Color.LimeGreen.Lighten(0.5f) : Color.Red.Lighten(0.5f), 500, Easing.OutQuint);
        }

        public override bool OnHover(MouseEvent e)
        {
            backgroundBox.Color = originalBackgroundColor;
            backgroundBox.FadeToColor(originalBackgroundColor.Lighten(0.5f), 50, Easing.OutQuint);
            return base.OnHover(e);
        }

        public override bool OnHoverLost(MouseEvent e)
        {
            backgroundBox.FadeToColor(originalBackgroundColor, 50, Easing.OutQuint);
            return base.OnHoverLost(e);
        }

        public void Flash()
        {
            backgroundBox.Color = originalBackgroundColor;
            backgroundBox.FlashColor(Color.White, 500, Easing.OutQuint);
        }

        public void UpdateText(string newText)
        {
            drawableText.Text = newText;
        }
    }

    private partial class TestSliderStepControl<T> : Container where T : struct, INumber<T>, IMinMaxValue<T>
    {
        public TestSliderStepControl(SliderStep<T> step)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            var currentValueText = new SpriteText
            {
                Text = step.StartValue.ToString(),
                Font = FontUsage.Default.With(size: 14),
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight
            };

            var slider = new BasicSliderBar<T>
            {
                RelativeSizeAxes = Axes.X,
                Width = 1,
                Height = 15,
                MinValue = step.MinValue,
                MaxValue = step.MaxValue,
                Margin = new MarginPadding { Top = 20 }
            };

            slider.Current.Value = step.StartValue;

            slider.Current.ValueChanged += e =>
            {
                currentValueText.Text = e.NewValue.ToString();
                step.ValueChanged?.Invoke(e.NewValue);
            };

            Children = new Drawable[]
            {
                new SpriteText
                {
                    Text = step.Description,
                    Font = FontUsage.Default.With(size: 14),
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft
                },
                currentValueText,
                slider
            };
        }
    }

    private partial class HeaderButton : ClickableContainer
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
