// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Numerics;

namespace Sakura.Framework.Testing;

public abstract class TestStep
{
    public string Description { get; set; } = string.Empty;
    public StepContext Context { get; set; } = StepContext.Test;
    public bool IsLabel { get; set; }
}

public class ActionStep : TestStep
{
    public Action? Action { get; set; }
    public bool IsAssert { get; set; }
}

public class WaitStep : TestStep
{
    public double WaitTime { get; set; }
    public Func<bool>? WaitCondition { get; set; }
    public bool HasTimeout { get; set; }
    public double Timeout { get; set; } = 10000;
}

public class SliderStep<T> : TestStep where T : struct, INumber<T>, IMinMaxValue<T>
{
    public T MinValue { get; set; }
    public T MaxValue { get; set; }
    public T StartValue { get; set; }
    public Action<T>? ValueChanged { get; set; }
}

public class RepeatStep : ActionStep
{
    public int RepeatCount { get; set; }
    public int CurrentIteration { get; set; }
}

public enum StepContext
{
    OneTimeSetUp,
    SetUp,
    Test,
    TearDown,
    OneTimeTearDown
}
