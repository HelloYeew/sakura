// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Platform;
using Sakura.Framework.Platform.Dialogs;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class HeadlessWindowFileDialogTest
{
    private HeadlessWindow window = null!;
    private static readonly string[] paths = new[] { "/tmp/picked.txt" };
    private static readonly string[] paths_array = new[] { "sentinel" };

    [SetUp]
    public void SetUp() => window = new HeadlessWindow();

    [TearDown]
    public void TearDown() => window.Dispose();

    [Test]
    public void OpenFileDialog_InvokesCallbackWithCannedResult()
    {
        window.FileDialogResult = FileDialogResult.FromPaths(paths);

        FileDialogResult received = default;
        bool called = false;

        window.ShowOpenFileDialog(new FileDialogOptions(), r =>
        {
            called = true;
            received = r;
        });

        Assert.That(called, Is.True);
        Assert.That(received.Successful, Is.True);
        Assert.That(received.Path, Is.EqualTo(paths[0]));
    }

    [Test]
    public void SaveFileDialog_DefaultResultIsCancellation()
    {
        FileDialogResult received = FileDialogResult.FromPaths(paths_array);

        window.ShowSaveFileDialog(new FileDialogOptions(), r => received = r);

        Assert.That(received.Successful, Is.False);
        Assert.That(received.Error, Is.Null);
    }

    [Test]
    public void OpenFolderDialog_RecordsRequestedOptions()
    {
        var options = new FileDialogOptions
        {
            Title = "Pick a folder",
            DefaultLocation = "/home/user",
            AllowMultiple = true,
        };

        window.ShowOpenFolderDialog(options, _ => { });

        Assert.That(window.LastFileDialogOptions, Is.SameAs(options));
    }

    [Test]
    public void FailedResult_PropagatesError()
    {
        window.FileDialogResult = FileDialogResult.Failed("no backend");

        FileDialogResult received = default;
        window.ShowOpenFileDialog(new FileDialogOptions(), r => received = r);

        Assert.That(received.Successful, Is.False);
        Assert.That(received.Error, Is.EqualTo("no backend"));
    }
}
