// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Platform.Dialogs;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class FileDialogFilterTest
{
    [Test]
    public void ToPattern_JoinsExtensionsWithSemicolon()
    {
        var filter = new FileDialogFilter("Images", "png", "jpg", "jpeg");
        Assert.That(filter.ToPattern(), Is.EqualTo("png;jpg;jpeg"));
    }

    [Test]
    public void ToPattern_StripsLeadingDots()
    {
        var filter = new FileDialogFilter("Images", ".png", ".jpg");
        Assert.That(filter.ToPattern(), Is.EqualTo("png;jpg"));
    }

    [Test]
    public void ToPattern_TrimsWhitespaceAndDropsEmptyEntries()
    {
        var filter = new FileDialogFilter("Docs", " txt ", "", "   ", "md");
        Assert.That(filter.ToPattern(), Is.EqualTo("txt;md"));
    }

    [Test]
    public void ToPattern_WildcardPreserved()
    {
        var filter = new FileDialogFilter("Everything", "*");
        Assert.That(filter.ToPattern(), Is.EqualTo("*"));
    }

    [Test]
    public void ToPattern_NoExtensions_ReturnsEmpty()
    {
        var filter = new FileDialogFilter("Empty");
        Assert.That(filter.ToPattern(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void AllFiles_ProducesWildcardPattern()
    {
        var filter = FileDialogFilter.AllFiles();
        Assert.That(filter.ToPattern(), Is.EqualTo("*"));
        Assert.That(filter.Name, Is.EqualTo("All files"));
    }

    [Test]
    public void Constructor_NullExtensions_DoesNotThrow()
    {
        var filter = new FileDialogFilter("Null", null!);
        Assert.That(filter.Extensions, Is.Empty);
        Assert.That(filter.ToPattern(), Is.EqualTo(string.Empty));
    }
}
