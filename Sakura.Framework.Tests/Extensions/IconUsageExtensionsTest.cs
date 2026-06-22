// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.IconUsageExtensions;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Tests.Extensions;

public class IconUsageExtensionsTest
{
    [Test]
    public void TestToGlyphMatchesCodepoint()
    {
        string glyph = IconUsage.PlayArrow.ToGlyph();

        Assert.That(glyph, Is.EqualTo(char.ConvertFromUtf32((int)IconUsage.PlayArrow)));
    }

    [Test]
    public void TestToGlyphRoundTrips()
    {
        string glyph = IconUsage.Alarm.ToGlyph();

        // the produced string should decode back to the same codepoint.
        Assert.That(char.ConvertToUtf32(glyph, 0), Is.EqualTo((int)IconUsage.Alarm));
    }

    [Test]
    public void TestToGlyphComposesWithText()
    {
        string composed = $"Play {IconUsage.PlayArrow.ToGlyph()}";

        Assert.That(composed, Does.StartWith("Play "));
        Assert.That(composed, Does.EndWith(IconUsage.PlayArrow.ToGlyph()));
    }
}
