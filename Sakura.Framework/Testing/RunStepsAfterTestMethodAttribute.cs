// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

namespace Sakura.Framework.Testing;

/// <summary>
/// Runs a <see cref="TestScene"/>'s steps under NUnit as soon as its test method returns, before any [TearDown] runs.
/// </summary>
/// <remarks>
/// NUnit runs a derived class's [TearDown] before its base class's, so steps run from a [TearDown] on
/// <see cref="TestScene"/> came after the test's own cleanup: a folder the cleanup deleted was gone before the steps
/// that needed it ran.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
internal sealed class RunStepsAfterTestMethodAttribute : Attribute, IWrapTestMethod
{
    /// <summary>
    /// Wraps the test method so its steps run straight after it.
    /// </summary>
    public TestCommand Wrap(TestCommand command) => new RunStepsCommand(command);

    /// <summary>
    /// Runs the test method, then the steps it and the [SetUp] methods queued.
    /// </summary>
    private sealed class RunStepsCommand : DelegatingTestCommand
    {
        /// <summary>
        /// Wraps the command that runs the test method.
        /// </summary>
        public RunStepsCommand(TestCommand innerCommand)
            : base(innerCommand)
        {
        }

        /// <summary>
        /// Runs the test method, then its steps unless the method itself already failed. A failing step throws, and
        /// NUnit records it as the test's failure.
        /// </summary>
        public override TestResult Execute(TestExecutionContext context)
        {
            var result = innerCommand.Execute(context);

            if (result.ResultState.Status != TestStatus.Failed && context.TestObject is TestScene scene)
                scene.RunQueuedSteps();

            return result;
        }
    }
}
