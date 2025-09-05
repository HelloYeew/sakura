// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Maths;
using SystemVector2 = System.Numerics.Vector2;

namespace Sakura.Framework.Tests.Maths;

public class Vector2Test
{
    [Test]
    public void TestImplicitConversion()
    {
        var sysVec = new SystemVector2(1.0f, 2.0f);
        Vector2 vec = sysVec; // Implicit conversion to Vector2
        Assert.That(vec.X, Is.EqualTo(1.0f));
        Assert.That(vec.Y, Is.EqualTo(2.0f));

        var sakuraVector = new Vector2(1, 2);
        float distance = Vector2.Distance(sakuraVector, new Vector2(4, 6));
        Assert.That(distance, Is.EqualTo(5.0f).Within(0.0001f));
    }
}
