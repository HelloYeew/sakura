// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Platform.Dialogs;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class FileDialogResultTest
{
    [Test]
    public void Cancelled_IsNotSuccessfulAndHasNoPaths()
    {
        var result = FileDialogResult.Cancelled;
        Assert.That(result.Successful, Is.False);
        Assert.That(result.Paths, Is.Empty);
        Assert.That(result.Path, Is.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void FromPaths_WithPaths_IsSuccessful()
    {
        var result = FileDialogResult.FromPaths(new[] { "/a/b.txt", "/a/c.txt" });
        Assert.That(result.Successful, Is.True);
        Assert.That(result.Paths, Is.EqualTo(new[] { "/a/b.txt", "/a/c.txt" }));
        Assert.That(result.Path, Is.EqualTo("/a/b.txt"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void FromPaths_Empty_IsTreatedAsCancellation()
    {
        var result = FileDialogResult.FromPaths(System.Array.Empty<string>());
        Assert.That(result.Successful, Is.False);
        Assert.That(result.Path, Is.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void Failed_CarriesErrorAndIsNotSuccessful()
    {
        var result = FileDialogResult.Failed("something broke");
        Assert.That(result.Successful, Is.False);
        Assert.That(result.Paths, Is.Empty);
        Assert.That(result.Error, Is.EqualTo("something broke"));
    }

    [Test]
    public void Error_DistinguishesFailureFromCancellation()
    {
        Assert.That(FileDialogResult.Cancelled.Error, Is.Null);
        Assert.That(FileDialogResult.Failed("x").Error, Is.Not.Null);
    }
}
