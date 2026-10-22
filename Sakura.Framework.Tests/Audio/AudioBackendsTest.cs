// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Audio;
using Sakura.Framework.Audio.Headless;

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
            Is.EqualTo(AudioBackends.BassAvailable
                ? new[] { AudioBackend.Automatic, AudioBackend.BASS, AudioBackend.SDL }
                : new[] { AudioBackend.Automatic, AudioBackend.SDL }),
            "Automatic first, matching RendererTypes.GetSuitableRenderers, and BASS only where it loads.");
    }

    [Test]
    public void BassIsOfferedWhereItShips()
    {
        Assert.That(AudioBackends.BassAvailable, Is.EqualTo(!OperatingSystem.IsAndroid()), "every desktop package carries libbass; Android's doesn't");
    }

    [Test]
    public void AutomaticPlaysThroughSdl()
    {
        var sdl = new HeadlessAudioManager();

        Assert.That(AudioBackends.Create(AudioBackend.Automatic, () => sdl, failIfCalled, bassAvailable: true), Is.SameAs(sdl));
    }

    [Test]
    public void BassWhereItIsAskedForAndLoads()
    {
        var bass = new HeadlessAudioManager();

        Assert.That(AudioBackends.Create(AudioBackend.BASS, failIfCalled, () => bass, bassAvailable: true), Is.SameAs(bass));
    }

    /// <summary>
    /// A framework.ini carried over from a desktop, or edited by hand, on a phone: SDL rather than a crash.
    /// </summary>
    [Test]
    public void BassAskedForWhereItIsMissingPlaysThroughSdl()
    {
        var sdl = new HeadlessAudioManager();

        Assert.That(AudioBackends.Create(AudioBackend.BASS, () => sdl, failIfCalled, bassAvailable: false), Is.SameAs(sdl));
    }

    [Test]
    public void SdlThatWontStartFallsBackToBass()
    {
        var bass = new HeadlessAudioManager();

        Assert.That(AudioBackends.Create(AudioBackend.SDL, sdlFails, () => bass, bassAvailable: true), Is.SameAs(bass));
    }

    /// <summary>
    /// Neither will start: the app runs silent rather than not at all.
    /// </summary>
    [Test]
    public void NothingThatStartsMeansSilence()
    {
        Assert.That(AudioBackends.Create(AudioBackend.Automatic, sdlFails, failIfCalled, bassAvailable: false), Is.InstanceOf<HeadlessAudioManager>());
    }

    private static IAudioManager sdlFails() => throw new InvalidOperationException("Failed to initialise SDL audio");

    private static IAudioManager failIfCalled() => throw new AssertionException("This backend shouldn't have been started.");

    /// <summary>
    /// A new backend should be a deliberate decision about whether users see it, not an omission.
    /// </summary>
    [Test]
    public void EveryBackendIsEitherOfferedOrDeliberatelyHidden()
    {
        AudioBackend[] hidden = AudioBackends.BassAvailable ? [AudioBackend.SDLManaged] : [AudioBackend.SDLManaged, AudioBackend.BASS];

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
