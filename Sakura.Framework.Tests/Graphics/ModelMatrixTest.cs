// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Numerics;
using NUnit.Framework;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// What a drawable's model matrix can leave out and still come out the same.
/// </summary>
[TestFixture]
public class ModelMatrixTest
{
    /// <summary>
    /// A drawable at rotation 0 skips the rotation step: the identity it would multiply by changes no value of any matrix
    /// the steps before it can make, scaled, sheared, flipped or moved. Compared as floats, every value but zero is the
    /// same bits; a -0 multiplied by the identity comes back +0, which is equal and draws the same.
    /// </summary>
    [Test]
    public void MultiplyingByARotationOfZeroChangesNoValue()
    {
        var random = new Random(1234);
        var rotation = Matrix3x2.CreateRotation(0f);

        for (int i = 0; i < 10_000; i++)
        {
            var m = new Matrix3x2(
                next(random), next(random) * (i % 2),
                next(random) * (i % 3 == 0 ? 1 : 0), next(random),
                next(random) * 1000, next(random) * 1000);

            var rotated = m * rotation;

            Assert.That(values(rotated), Is.EqualTo(values(m)), $"matrix {i}: {m}");
        }
    }

    /// <summary>
    /// A float from -100 to 100, negative half the time, as a scale or a shear can be.
    /// </summary>
    private static float next(Random random) => (float)(random.NextDouble() * 200 - 100);

    /// <summary>
    /// The matrix's six components.
    /// </summary>
    private static float[] values(Matrix3x2 m) => new[]
    {
        m.M11,
        m.M12,
        m.M21,
        m.M22,
        m.M31,
        m.M32
    };
}
