// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Collections.Generic;
using NUnit.Framework;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Graphics.UserInterface;
using Sakura.Framework.Input;
using Sakura.Framework.Logging;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing.Input;
using Sakura.Framework.Timing;

namespace Sakura.Framework.Tests.Input;

/// <summary>
/// What a text box asks of the window's text input, which on a phone is the virtual keyboard.
/// </summary>
[TestFixture]
public class TextBoxKeyboardTest
{
    private HeadlessTextureManager textureManager = null!;
    private RendererFontStore fontStore = null!;
    private RecordingWindow window = null!;
    private ManualClock manual = null!;
    private TestRoot root = null!;
    private ManualInputManager input = null!;

    [OneTimeSetUp]
    public void InitializeLogger() => Logger.Initialize();

    [OneTimeTearDown]
    public void ShutdownLogger() => Logger.Shutdown();

    [SetUp]
    public void SetUp()
    {
        textureManager = new HeadlessTextureManager();
        fontStore = new RendererFontStore(new HeadlessRenderer(textureManager));

        var fonts = new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources")
            .GetStorageForDirectory("Fonts");

        fontStore.AddFont(fonts, "Comfortaa-Regular.ttf");

        window = new RecordingWindow();
        manual = new ManualClock { CurrentTime = 1000 };

        root = new TestRoot(fontStore, window)
        {
            Size = new Vector2(800, 600),
            Clock = new FramedClock(manual)
        };

        root.Add(input = new ManualInputManager { UseParentInput = false });

        root.Load();
        root.CompleteLoad();
    }

    [TearDown]
    public void TearDown()
    {
        fontStore.Dispose();
        textureManager.Dispose();
    }

    private void settle(int frames = 3)
    {
        for (int i = 0; i < frames; i++)
        {
            manual.CurrentTime += 16;
            root.UpdateSubTree();
        }
    }

    private BasicTextBox addTextBox(TextInputType type = TextInputType.Text)
    {
        var box = new BasicTextBox { Position = new Vector2(100), Size = new Vector2(300, 40), InputType = type };
        input.Add(box);
        settle();
        return box;
    }

    [Test]
    public void TapOpensTheKeyboardForTheBoxsType()
    {
        var box = addTextBox(TextInputType.Password);

        input.Tap(box);

        Assert.Multiple(() =>
        {
            Assert.That(box.HasFocus, Is.True);
            Assert.That(window.Calls, Has.Member("start Password"));
            Assert.That(window.Calls, Has.No.Member("stop"));
        });
    }

    [Test]
    public void TapOnTheFocusedBoxOpensTheKeyboardAgain()
    {
        var box = addTextBox();

        input.Tap(box);
        int startsBefore = window.Starts;

        // As after the user put the keyboard away: the box still has focus, so focus can't bring it back.
        input.Tap(box);

        Assert.Multiple(() =>
        {
            Assert.That(box.HasFocus, Is.True);
            Assert.That(window.Starts, Is.GreaterThan(startsBefore));
            Assert.That(window.Calls, Has.No.Member("stop"));
        });
    }

    [Test]
    public void TapOutsideClosesTheKeyboard()
    {
        var box = addTextBox();

        input.Tap(box);
        input.Tap(new Vector2(700, 500));

        Assert.Multiple(() =>
        {
            Assert.That(box.HasFocus, Is.False);
            Assert.That(window.Calls[^1], Is.EqualTo("stop"));
        });
    }

    private partial class TestRoot : Container
    {
        private readonly IFontStore fontStore;
        private readonly IWindow window;

        public TestRoot(IFontStore fontStore, IWindow window)
        {
            this.fontStore = fontStore;
            this.window = window;
        }

        public override void Load()
        {
            base.Load();

            Cache(fontStore);
            Cache(window);
        }
    }

    /// <summary>
    /// A headless window that remembers what text input was asked of it.
    /// </summary>
    private class RecordingWindow : HeadlessWindow, IWindow
    {
        public readonly List<string> Calls = new List<string>();

        public int Starts { get; private set; }

        public new void StartTextInput(TextInputType type = TextInputType.Text)
        {
            Starts++;
            Calls.Add($"start {type}");
        }

        public new void StopTextInput() => Calls.Add("stop");
    }
}
