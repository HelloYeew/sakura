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

        Vector2 unitVector = Vector2.Divide(vec, vec); // Using Vector2 methods

        System.Numerics.Vector2 backToSysVec = vec; // Implicit conversion back to System.Numerics.Vector2
        Assert.AreEqual(sysVec, backToSysVec);
    }
}
