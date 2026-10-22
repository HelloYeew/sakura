// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;

namespace Sakura.Framework.Tests;

/// <summary>
/// Marks a test that depends on real time: audio that plays as fast as a device drains it, threads that must wake
/// on time, or two threads racing. A busy or slow machine, a shared CI runner most of all, can make it miss by
/// chance, so it runs again on a failed assertion, up to <see cref="DEFAULT_TRIES"/> times in all. One unlucky
/// run doesn't fail the build, and a test that's really broken still fails every try.
/// </summary>
/// <remarks>
/// Set <c>SAKURA_TESTS_FAIL_FLAKY</c> to <c>1</c> to run each one once, and see how often it fails.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FlakyTestAttribute : RetryAttribute
{
    public const int DEFAULT_TRIES = 3;

    private static readonly bool fail_flaky = Environment.GetEnvironmentVariable("SAKURA_TESTS_FAIL_FLAKY") == "1";

    public FlakyTestAttribute()
        : this(DEFAULT_TRIES)
    {
    }

    public FlakyTestAttribute(int tries)
        : base(fail_flaky ? 1 : tries)
    {
    }
}
