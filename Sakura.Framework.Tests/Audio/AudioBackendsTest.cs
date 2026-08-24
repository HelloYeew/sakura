// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Audio;

namespace Sakura.Framework.Tests.Audio;

/// <summary>
/// What a settings UI is allowed to offer.
/// </summary>
[TestFixture]
public class AudioBackendsTest
{
    [Test]
    public void SuitableBackendsExcludeTheManagedMixer()
    {
        Assert.That(AudioBackends.GetSuitableBackends(), Does.Not.Contain(AudioBackend.SDLManaged),
            "SDLManaged is not a backend choice, it is SDL with its native mix engine switched off. It costs roughly "
            + "twenty times the output latency and exists to be diffed against, so it belongs in framework.ini rather "
            + "than in a dropdown.");
    }

    [Test]
    public void SuitableBackendsOfferEveryBackendAUserWouldWant()
    {
        Assert.That(AudioBackends.GetSuitableBackends(),
            Is.EqualTo(new[] { AudioBackend.Automatic, AudioBackend.BASS, AudioBackend.SDL }),
            "Automatic first, matching RendererTypes.GetSuitableRenderers.");
    }

    /// <summary>
    /// A new backend should be a deliberate decision about whether users see it, not an omission.
    /// </summary>
    [Test]
    public void EveryBackendIsEitherOfferedOrDeliberatelyHidden()
    {
        AudioBackend[] hidden = [AudioBackend.SDLManaged];

        var accounted = AudioBackends.GetSuitableBackends().Concat(hidden).ToArray();

        Assert.That(accounted, Is.EquivalentTo(Enum.GetValues<AudioBackend>()),
            "A backend was added to the AudioBackend enum without deciding whether GetSuitableBackends should offer "
            + "it. Add it to the list, or to this test's `hidden` array with a reason.");
    }

    /// <summary>
    /// Hiding it from the dropdown must not make it unreachable.
    /// </summary>
    [Test]
    public void TheHiddenBackendIsStillAValidSetting()
    {
        Assert.That(Enum.IsDefined(AudioBackend.SDLManaged), Is.True,
            "SDLManaged has to stay a parseable framework.ini value: it is what a bug report is asked to try, and what "
            + "the parity tests select.");
    }
}
