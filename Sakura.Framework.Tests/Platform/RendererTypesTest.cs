// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Tests.Platform;

[TestFixture]
public class RendererTypesTest
{
    [Test]
    public void SuitableRenderersAreAutomaticThenThePlatformsOwn()
    {
        var suitable = RendererTypes.GetSuitableRenderers();

        Assert.Multiple(() =>
        {
            Assert.That(suitable[0], Is.EqualTo(RendererType.Automatic));
            Assert.That(suitable.Skip(1), Is.EqualTo(RendererTypes.GetPlatformRenderers()));
            Assert.That(suitable, Is.Unique, "a settings dropdown lists each once");
        });
    }

    [Test]
    public void LinuxTriesVulkanBeforeOpenGL()
    {
        // RuntimeInfo tells Android, which has a list of its own, apart from Linux.
        if (RuntimeInfo.OS != RuntimeInfo.Platform.Linux)
            Assert.Ignore("Linux only.");

        Assert.That(RendererTypes.GetPlatformRenderers(), Is.EqualTo(new[] { RendererType.Vulkan, RendererType.OpenGL }));
    }
}
