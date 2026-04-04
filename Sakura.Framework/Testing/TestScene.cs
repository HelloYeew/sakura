// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Testing;

[TestFixture]
public abstract class TestScene : Container
{
    /// <summary>
    /// Tells the TestScene to bypass spinning up a headless host because the visual is running it.
    /// </summary>
    public static bool IsVisualRunner { get; set; }

    public IReadOnlyList<TestStep> Steps => steps;
    private readonly List<TestStep> steps = new();

    public TestScene()
    {
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
    }

    public void AddStep(string description, Action stepAction)
    {
        steps.Add(new TestStep
        {
            Description = description,
            Action = stepAction,
            IsAssert = false
        });
    }

    public void AddAssert(string description, Func<bool> assert)
    {
        steps.Add(new TestStep
        {
            Description = description,
            Action = () => Assert.That(assert(), description),
            IsAssert = true
        });
    }

    public void AddWaitStep(string description, double milliseconds)
    {
        steps.Add(new TestStep
        {
            Description = description,
            WaitTime = milliseconds
        });
    }

    public void AddUntilStep(string description, Func<bool> condition, double timeout = 10000)
    {
        steps.Add(new TestStep
        {
            Description = description,
            WaitCondition = condition,
            HasTimeout = true,
            Timeout = timeout
        });
    }

    public void AddLabel(string description)
    {
        steps.Add(new TestStep
        {
            Description = description,
            IsLabel = true
        });
    }

    [SetUp]
    public virtual void SetupNUnit()
    {
        if (!IsVisualRunner)
        {
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
        host.Run(runnerApp);

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
                    step.Action?.Invoke();
                }
                catch (Exception ex)
                {
                    TestException = ex;
                    host.Exit();
                    return;
                }
            }

            double elapsed = Clock.CurrentTime - currentStepStartTime;

            if (step.WaitTime > 0)
            {
                if (elapsed < step.WaitTime)
                    return;
            }
            else if (step.WaitCondition != null)
            {
                try
                {
                    if (!step.WaitCondition())
                    {
                        if (step.HasTimeout && elapsed > step.Timeout)
                        {
                            throw new TimeoutException($"Test step '{step.Description}' timed out after {step.Timeout}ms.");
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

            isExecutingStep = false;
            currentStepIndex++;
        }
    }
}
