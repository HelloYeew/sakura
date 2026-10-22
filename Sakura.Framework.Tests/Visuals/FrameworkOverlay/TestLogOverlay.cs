// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Configurations;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Logging;
using Sakura.Framework.Reactive;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.FrameworkOverlay;

public partial class TestLogOverlay : TestScene
{
    private LogOverlay overlay = null!;
    private Reactive<bool> shown = null!;

    [Resolved]
    private FrameworkConfigManager frameworkConfigManager { get; set; } = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add overlay", () =>
        {
            shown = frameworkConfigManager.Get<bool>(FrameworkSetting.ShowLogOverlay);
            shown.Value = true;
            // At the top, so in the visual runner it doesn't sit on the app's own one at the bottom.
            Add(overlay = new LogOverlay
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft
            });
        });
    }

    [Test]
    public void TestEntriesShowWhileItIsOn()
    {
        AddStep("Log a warning", () => Logger.Warning("A warning to show"));
        AddUntilStep("It's shown", () => shows("A warning to show"));

        AddStep("Log an error with a stack trace", () => Logger.Error("An error to show", new InvalidOperationException("what went wrong")));
        AddUntilStep("Only its first line is shown", () => shows("An error to show") && !shows("what went wrong"));
    }

    [Test]
    public void TestWhatWasLoggedWhileOffShowsWhenTurnedOn()
    {
        AddStep("Turn it off", () => shown.Value = false);
        AddStep("Log a warning", () => Logger.Warning("Logged while off"));
        AddWaitStep("Give it time to arrive", 200);
        AddAssert("Not shown", () => !shows("Logged while off"));

        AddStep("Turn it on", () => shown.Value = true);
        AddUntilStep("Shown now", () => shows("Logged while off"));
    }

    [TearDown]
    public void TearDown() => AddStep("Turn it off", () => shown.Value = false);

    private bool shows(string text) => contains(overlay, text);

    private static bool contains(Drawable drawable, string text)
    {
        if (drawable is SpriteText spriteText && spriteText.Text.Contains(text, StringComparison.Ordinal))
            return true;

        if (drawable is Container container)
        {
            foreach (var child in container.InternalChildren)
            {
                if (contains(child, text))
                    return true;
            }
        }

        return false;
    }
}
