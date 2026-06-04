// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Platform;
using Vector2 = Sakura.Framework.Maths.Vector2;

namespace Sakura.Framework.Testing;

[TestFixture]
public abstract class TestScene : Container
{
    /// <summary>
    /// Tells the TestScene to bypass spinning up a headless host because the visual is running it.
    /// </summary>
    public static bool IsVisualRunner { get; set; }

    public IReadOnlyList<TestStep> Steps => steps;
    private readonly List<TestStep> steps = new List<TestStep>();
    public StepContext CurrentStepContext { get; set; } = StepContext.Test;

    public TestScene()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
    }

    public void AddStep(string description, Action stepAction)
    {
        steps.Add(new ActionStep
        {
            Description = description,
            Action = stepAction,
            IsAssert = false,
            Context = CurrentStepContext
        });
    }

    public void AddAssert(string description, Func<bool> assert)
    {
        steps.Add(new ActionStep
        {
            Description = description,
            Action = () => Assert.That(assert(), description),
            IsAssert = true,
            Context = CurrentStepContext
        });
    }

    public void AddWaitStep(string description, double milliseconds)
    {
        steps.Add(new WaitStep
        {
            Description = description,
            WaitTime = milliseconds,
            Context = CurrentStepContext
        });
    }

    public void AddUntilStep(string description, Func<bool> condition, double timeout = 10000)
    {
        steps.Add(new WaitStep
        {
            Description = description,
            WaitCondition = condition,
            HasTimeout = true,
            Timeout = timeout,
            Context = CurrentStepContext
        });
    }

    public void AddLabel(string description)
    {
        steps.Add(new ActionStep
        {
            Description = description,
            IsLabel = true,
            Context = CurrentStepContext
        });
    }

    public void AddSliderStep<T>(string description, T min, T max, T start, Action<T> valueChanged) where T : struct, INumber<T>
    {
        steps.Add(new SliderStep<T>
        {
            Description = description,
            MinValue = min,
            MaxValue = max,
            StartValue = start,
            ValueChanged = valueChanged,
            Context = CurrentStepContext
        });
    }

    public void AddRepeatStep(string description, Action stepAction, int repeatCount)
    {
        steps.Add(new RepeatStep
        {
            Description = description,
            Action = stepAction,
            RepeatCount = repeatCount,
            Context = CurrentStepContext
        });
    }

    [SetUp]
    public virtual void SetupNUnit()
    {
        if (!IsVisualRunner)
        {
            string methodName = TestContext.CurrentContext.Test.MethodName;
            if (!string.IsNullOrEmpty(methodName))
            {
                var method = GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var attribute = method?.GetCustomAttribute<VisualTestOnlyAttribute>()
                                ?? GetType().GetCustomAttribute<VisualTestOnlyAttribute>();

                if (attribute != null)
                {
                    Assert.Ignore(attribute.Reason);
                }
            }

            steps.Clear();
            Clear();
        }
        else
        {
            AddStep("Clear test scene", Clear);
        }
    }

    [TearDown]
    public virtual void TeardownNUnit()
    {
        if (IsVisualRunner || steps.Count == 0)
            return;

        using var host = new HeadlessAppHost($"HeadlessTest-{TestContext.CurrentContext.Test.Name}");
        var runnerApp = new HeadlessTestRunnerApp(this, host);

        try
        {
            host.Run(runnerApp);
        }
        finally
        {
            runnerApp.Remove(this);
        }

        if (runnerApp.TestException != null)
        {
            ExceptionDispatchInfo.Capture(runnerApp.TestException).Throw();
        }
    }

    /// <summary>
    /// Finds and executes all methods marked with NUnit's [SetUp] attribute.
    /// </summary>
    public void RunSetUpMethods()
    {
        var typeHierarchy = new List<Type>();
        var currentType = GetType();

        while (currentType != null && currentType != typeof(Container))
        {
            typeHierarchy.Insert(0, currentType);
            currentType = currentType.BaseType;
        }

        foreach (var type in typeHierarchy)
        {
            var setUpMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<SetUpAttribute>() != null);

            foreach (var method in setUpMethods)
            {
                method.Invoke(this, null);
            }
        }
    }

    /// <summary>
    /// Finds and executes all methods marked with NUnit's [TearDown] attribute.
    /// </summary>
    public void RunTearDownMethods()
    {
        var tearDownMethods = GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<TearDownAttribute>() != null);

        foreach (var method in tearDownMethods)
        {
            method.Invoke(this, null);
        }
    }

    /// <summary>
    /// Finds and executes all methods marked with NUnit's [OneTimeSetUp] attribute.
    /// </summary>
    public void RunOneTimeSetUpMethods()
    {
        var methods = GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<OneTimeSetUpAttribute>() != null);

        foreach (var method in methods)
        {
            method.Invoke(method.IsStatic ? null : this, null);
        }
    }

    /// <summary>
    /// Finds and executes all methods marked with NUnit's [OneTimeTearDown] attribute.
    /// </summary>
    public void RunOneTimeTearDownMethods()
    {
        var methods = GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<OneTimeTearDownAttribute>() != null);

        foreach (var method in methods)
        {
            method.Invoke(method.IsStatic ? null : this, null);
        }
    }

    /// <summary>
    /// A localized <see cref="App"/> instance responsible for running the test steps within the headless update loop.
    /// </summary>
    private class HeadlessTestRunnerApp : App
    {
        private readonly TestScene testScene;
        private readonly AppHost host;
        private int currentStepIndex;

        private bool isExecutingStep;
        private double currentStepStartTime;

        public Exception? TestException { get; private set; }

        protected override Assembly ResourceAssembly => testScene.GetType().Assembly;

        protected override string ResourceRootNamespace => $"{testScene.GetType().Assembly.GetName().Name}.Resources";

        public HeadlessTestRunnerApp(TestScene testScene, AppHost host)
        {
            this.testScene = testScene;
            this.host = host;
        }

        public override void Load()
        {
            base.Load();
            Logger.Debug($"Starting test scene: {testScene.GetType().Name}");
            Logger.Debug($"Resource assembly: {ResourceAssembly.FullName}");
            Logger.Debug($"Resource root namespace: {ResourceRootNamespace}");
            Add(testScene);
        }

        public override void Update()
        {
            base.Update();

            if (TestException != null) return;

            if (!testScene.IsLoaded) return;

            if (currentStepIndex >= testScene.Steps.Count)
            {
                host.Exit();
                return;
            }

            var step = testScene.Steps[currentStepIndex];

            if (!isExecutingStep)
            {
                isExecutingStep = true;
                currentStepStartTime = Clock.CurrentTime;

                Logger.Verbose($"Executing test step {currentStepIndex + 1}/{testScene.Steps.Count}: {step.Description}");

                try
                {
                    if (step is RepeatStep repeatStep)
                    {
                        repeatStep.Action?.Invoke();
                        repeatStep.CurrentIteration++;

                        if (repeatStep.CurrentIteration < repeatStep.RepeatCount)
                        {
                            isExecutingStep = false;
                            return;
                        }

                        repeatStep.CurrentIteration = 0;
                    }
                    else if (step is ActionStep actionStep)
                    {
                        actionStep.Action?.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    TestException = ex;
                    host.Exit();
                    return;
                }
            }

            double elapsed = Clock.CurrentTime - currentStepStartTime;

            if (step is WaitStep waitStep)
            {
                if (waitStep.WaitTime > 0)
                {
                    if (elapsed < waitStep.WaitTime)
                        return;
                }
                else if (waitStep.WaitCondition != null)
                {
                    try
                    {
                        if (!waitStep.WaitCondition())
                        {
                            if (waitStep.HasTimeout && elapsed > waitStep.Timeout)
                            {
                                throw new TimeoutException($"Test step '{step.Description}' timed out after {waitStep.Timeout}ms.");
                            }
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        TestException = ex;
                        host.Exit();
                        return;
                    }
                }
            }

            isExecutingStep = false;
            currentStepIndex++;
        }
    }
}
