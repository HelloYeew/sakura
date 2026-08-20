// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Text;

namespace Sakura.Framework.Tests.Text;

/// <summary>
/// Tests for <see cref="FontScripts.FromCodepoint"/> the classification the fallback chain is
/// selected by, and which run segmentation is broken on.
/// </summary>
[TestFixture]
public class FontScriptTest
{
    [TestCase('A', FontScript.Latin)]
    [TestCase('z', FontScript.Latin)]
    [TestCase('А', FontScript.Cyrillic)] // U+0410
    [TestCase('α', FontScript.Greek)]
    [TestCase('あ', FontScript.Japanese)]
    [TestCase('カ', FontScript.Japanese)]
    [TestCase('한', FontScript.Korean)]
    [TestCase('漢', FontScript.Han)]
    [TestCase('ก', FontScript.Thai)]
    [TestCase('ا', FontScript.Arabic)]
    [TestCase('א', FontScript.Hebrew)]
    [TestCase('अ', FontScript.Devanagari)]
    public void ScriptIsClassifiedFromTheCodepoint(char codepoint, FontScript expected)
        => Assert.That(FontScripts.FromCodepoint(codepoint), Is.EqualTo(expected));

    /// <summary>
    /// Characters Unicode attributes to no script of their own continue the run they appear in, so the
    /// comma in a Japanese sentence is drawn by the Japanese font rather than sending the run back to
    /// the primary font (and splitting it in two while doing so)
    /// </summary>
    [TestCase('、', FontScript.Japanese)]
    [TestCase(' ', FontScript.Japanese)]
    [TestCase('1', FontScript.Japanese)]
    [TestCase('.', FontScript.Japanese)]
    public void ScriptLessCharactersContinueTheRun(char codepoint, FontScript expected)
        => Assert.That(FontScripts.FromCodepoint(codepoint, FontScript.Japanese), Is.EqualTo(expected));

    /// <summary>
    /// Emoji are pictographs regardless of the surrounding text. Unicode calls most of them
    /// <c>Common</c>, so classifying them by script property alone would hand them to whichever font is
    /// drawing the surrounding words.
    /// </summary>
    [TestCase(0x1F600u)] // grinning face
    [TestCase(0x1F1EFu)] // regional indicator J
    [TestCase(0x2764u)] // heavy black heart
    [TestCase(0x2B50u)] // white medium star
    public void EmojiAreClassifiedAsEmojiInsideTextOfAnotherScript(uint codepoint)
        => Assert.That(FontScripts.FromCodepoint(codepoint, FontScript.Japanese), Is.EqualTo(FontScript.Emoji));

    /// <summary>
    /// Characters that live near the emoji blocks but read as text belong to the text font.
    /// </summary>
    [TestCase('©')]
    [TestCase('®')]
    [TestCase('™')]
    public void TextSymbolsAreNotEmoji(char codepoint)
        => Assert.That(FontScripts.FromCodepoint(codepoint, FontScript.Latin), Is.Not.EqualTo(FontScript.Emoji));

    /// <summary>
    /// A space after an emoji belongs with the text that follows, not with the emoji font.
    /// </summary>
    [Test]
    public void AnEmojiRunIsNotContinued()
        => Assert.That(FontScripts.FromCodepoint(' ', FontScript.Emoji), Is.EqualTo(FontScript.Any));

    /// <summary>
    /// The one thing a codepoint cannot settle: which language's forms an ideograph should be drawn in.
    /// </summary>
    [Test]
    public void ALanguageHintResolvesIdeographs()
        => Assert.That(FontScripts.FromCodepoint('漢', FontScript.Any, FontScript.Japanese), Is.EqualTo(FontScript.Japanese));

    /// <summary>
    /// Everything else is settled by the codepoint, so a hint must not be able to relabel it — a
    /// mislabeled string would otherwise render in the wrong language's font entirely.
    /// </summary>
    [Test]
    public void ALanguageHintDoesNotOverrideAnUnambiguousScript()
        => Assert.That(FontScripts.FromCodepoint('ก', FontScript.Any, FontScript.Japanese), Is.EqualTo(FontScript.Thai));

    /// <summary>
    /// Kana and kanji alternate constantly in Japanese text, and one font normally serves both, so they
    /// must not break the run into one per character class. A complex script must, since a run is shaped
    /// with properties guessed from its contents and Thai marks shaped as Latin sit in the wrong place.
    /// </summary>
    [Test]
    public void ShapingRunsAreSharedWithinAScriptGroupOnly()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(FontScripts.ShareShapingRun(FontScript.Japanese, FontScript.Han), Is.True);
            Assert.That(FontScripts.ShareShapingRun(FontScript.Latin, FontScript.Cyrillic), Is.True);
            Assert.That(FontScripts.ShareShapingRun(FontScript.Latin, FontScript.Any), Is.True);
            Assert.That(FontScripts.ShareShapingRun(FontScript.Latin, FontScript.Thai), Is.False);
            Assert.That(FontScripts.ShareShapingRun(FontScript.Thai, FontScript.Arabic), Is.False);
            Assert.That(FontScripts.ShareShapingRun(FontScript.Latin, FontScript.Emoji), Is.False);
        }
    }
}
