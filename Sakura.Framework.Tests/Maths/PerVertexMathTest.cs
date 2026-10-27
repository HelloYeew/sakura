// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using Sakura.Framework.Maths;
using SystemMatrix3x2 = System.Numerics.Matrix3x2;
using SystemVector2 = System.Numerics.Vector2;

namespace Sakura.Framework.Tests.Maths;

/// <summary>
/// Test for System.Numerics per-vertex math generate loop in  CoreCLR,
/// Add until https://github.com/dotnet/runtime/pull/134105 got release on .NET 12
/// See https://github.com/HelloYeew/sakura/pull/206 for more details
/// </summary>
[TestFixture]
public class PerVertexMathTest
{
    // System.Numerics' "estimate" intrinsics: a ReadyToRun build never compiles them, leaving them to a JIT, and iOS has
    // none, so each call there runs in the interpreter.
    private static readonly string[] estimates = { "MultiplyAddEstimate", "ReciprocalEstimate", "ReciprocalSqrtEstimate", "ConvertToIntegerNative" };

    private static IEnumerable<TestCaseData> perVertexMembers()
    {
        yield return new TestCaseData(typeof(Vector2).GetMethod(nameof(Vector2.Transform), new[] { typeof(Vector2), typeof(Matrix3x2) })).SetArgDisplayNames("Vector2.Transform");
        yield return new TestCaseData(typeof(Vector2).GetMethod(nameof(Vector2.TransformNormal), new[] { typeof(Vector2), typeof(Matrix3x2) })).SetArgDisplayNames("Vector2.TransformNormal");
        yield return new TestCaseData(typeof(Vector2).GetMethod(nameof(Vector2.Lerp), new[] { typeof(Vector2), typeof(Vector2), typeof(float) })).SetArgDisplayNames("Vector2.Lerp");

        // .NET 10's own product is plain multiplies and adds, so these two only catch a forward on .NET 11 or later,
        // which iOS runs.
        yield return new TestCaseData(typeof(Matrix3x2).GetMethod("op_Multiply", new[] { typeof(Matrix3x2), typeof(Matrix3x2) })).SetArgDisplayNames("Matrix3x2 * Matrix3x2");
        yield return new TestCaseData(typeof(Matrix3x2).GetMethod(nameof(Matrix3x2.Multiply), new[] { typeof(Matrix3x2), typeof(Matrix3x2) })).SetArgDisplayNames("Matrix3x2.Multiply");
    }

    [TestCaseSource(nameof(perVertexMembers))]
    public void ReachesNoEstimateIntrinsic(MethodInfo member)
    {
        string? path = pathToEstimate(member, 8, new Dictionary<MethodBase, int>());

        Assert.That(path, Is.Null, $"Runs in the interpreter on iOS: {path}");
    }

    /// <summary>
    /// What the members above are checked with finds the estimate System.Numerics reaches, so they can't pass for want
    /// of looking.
    /// </summary>
    [Test]
    public void FindsTheEstimateSystemNumericsReaches()
    {
        var transform = typeof(SystemVector2).GetMethod(nameof(SystemVector2.Transform), new[] { typeof(SystemVector2), typeof(SystemMatrix3x2) })!;

        Assert.That(pathToEstimate(transform, 8, new Dictionary<MethodBase, int>()), Is.Not.Null);
    }

    [Test]
    public void TransformMatchesSystemNumerics()
    {
        var random = new Random(1);

        for (int i = 0; i < 1000; i++)
        {
            var matrix = randomMatrix(random);
            var position = new Vector2(next(random), next(random));

            var transformed = Vector2.Transform(position, matrix);
            var expected = SystemVector2.Transform(position, matrix);
            var normal = Vector2.TransformNormal(position, matrix);
            var expectedNormal = SystemVector2.TransformNormal(position, matrix);

            Assert.Multiple(() =>
            {
                Assert.That(transformed.X, Is.EqualTo(expected.X).Within(roundingOf(position.X * matrix.M11, position.Y * matrix.M21, matrix.M31)));
                Assert.That(transformed.Y, Is.EqualTo(expected.Y).Within(roundingOf(position.X * matrix.M12, position.Y * matrix.M22, matrix.M32)));
                Assert.That(normal.X, Is.EqualTo(expectedNormal.X).Within(roundingOf(position.X * matrix.M11, position.Y * matrix.M21)));
                Assert.That(normal.Y, Is.EqualTo(expectedNormal.Y).Within(roundingOf(position.X * matrix.M12, position.Y * matrix.M22)));
            });
        }
    }

    [Test]
    public void MultiplyMatchesSystemNumericsAndAppliesTheLeftFirst()
    {
        var random = new Random(2);

        for (int i = 0; i < 1000; i++)
        {
            var first = randomMatrix(random);
            var second = randomMatrix(random);
            var product = first * second;
            SystemMatrix3x2 expected = first.Value * second.Value;

            Assert.Multiple(() =>
            {
                Assert.That(product.M11, Is.EqualTo(expected.M11).Within(roundingOf(first.M11 * second.M11, first.M12 * second.M21)));
                Assert.That(product.M12, Is.EqualTo(expected.M12).Within(roundingOf(first.M11 * second.M12, first.M12 * second.M22)));
                Assert.That(product.M21, Is.EqualTo(expected.M21).Within(roundingOf(first.M21 * second.M11, first.M22 * second.M21)));
                Assert.That(product.M22, Is.EqualTo(expected.M22).Within(roundingOf(first.M21 * second.M12, first.M22 * second.M22)));
                Assert.That(product.M31, Is.EqualTo(expected.M31).Within(roundingOf(first.M31 * second.M11, first.M32 * second.M21, second.M31)));
                Assert.That(product.M32, Is.EqualTo(expected.M32).Within(roundingOf(first.M31 * second.M12, first.M32 * second.M22, second.M32)));
                Assert.That(Matrix3x2.Multiply(first, second), Is.EqualTo(product));
            });
        }

        // Moved, then turned a quarter: (1, 0) goes to (2, 0), then to (0, 2).
        var moveThenTurn = Matrix3x2.CreateTranslation(1, 0) * Matrix3x2.CreateRotation(MathF.PI / 2);
        var point = Vector2.Transform(new Vector2(1, 0), moveThenTurn);

        Assert.That(point.X, Is.EqualTo(0).Within(1e-6f));
        Assert.That(point.Y, Is.EqualTo(2).Within(1e-6f));
    }

    [Test]
    public void IdentityAndTranslationAreExact()
    {
        var random = new Random(3);

        for (int i = 0; i < 1000; i++)
        {
            var position = new Vector2(next(random), next(random));
            var matrix = randomMatrix(random);
            var translation = Matrix3x2.CreateTranslation(next(random), next(random));

            Assert.Multiple(() =>
            {
                Assert.That(Vector2.Transform(position, Matrix3x2.Identity), Is.EqualTo(position));
                Assert.That(Vector2.Transform(position, translation), Is.EqualTo(new Vector2(position.X + translation.M31, position.Y + translation.M32)));
                Assert.That(matrix * Matrix3x2.Identity, Is.EqualTo(matrix));
                Assert.That(Matrix3x2.Identity * matrix, Is.EqualTo(matrix));
            });
        }
    }

    [Test]
    public void LerpEndsExactlyOnItsEnds()
    {
        var random = new Random(4);

        for (int i = 0; i < 1000; i++)
        {
            var from = new Vector2(next(random), next(random));
            var to = new Vector2(next(random), next(random));
            float amount = (float)random.NextDouble();
            var between = Vector2.Lerp(from, to, amount);
            var expected = SystemVector2.Lerp(from, to, amount);

            Assert.Multiple(() =>
            {
                // A transform's last frame lands exactly on its end value.
                Assert.That(Vector2.Lerp(from, to, 0), Is.EqualTo(from));
                Assert.That(Vector2.Lerp(from, to, 1), Is.EqualTo(to));
                Assert.That(between.X, Is.EqualTo(expected.X).Within(roundingOf(from.X * (1 - amount), to.X * amount)));
                Assert.That(between.Y, Is.EqualTo(expected.Y).Within(roundingOf(from.Y * (1 - amount), to.Y * amount)));
            });
        }
    }

    private static float next(Random random) => (float)(random.NextDouble() * 2000 - 1000);

    private static Matrix3x2 randomMatrix(Random random)
        => new Matrix3x2(next(random) / 100, next(random) / 100, next(random) / 100, next(random) / 100, next(random), next(random));

    // The gap between 1 and the next float: one rounding, relative to the value rounded.
    private const float machine_epsilon = 1.1920929e-7f;

    /// <summary>
    /// How far a sum of these terms may come out between fused and separate multiply-adds: a few roundings of them.
    /// </summary>
    private static float roundingOf(params float[] terms) => terms.Sum(MathF.Abs) * 4 * machine_epsilon + float.Epsilon;

    /// <summary>
    /// The calls from <paramref name="method"/> that end at an estimate intrinsic, as "a -> b -> c", or null for none.
    /// Follows only the math structs and the math in the base library, where these live. <paramref name="explored"/>
    /// holds the depth each method was looked into with, so one reached again with more to go is looked into again.
    /// </summary>
    private static string? pathToEstimate(MethodBase method, int depth, Dictionary<MethodBase, int> explored)
    {
        if (estimates.Contains(method.Name) && method.DeclaringType?.Assembly == typeof(object).Assembly)
            return describe(method);

        if (depth == 0 || (explored.TryGetValue(method, out int before) && before >= depth) || !followed(method))
            return null;

        explored[method] = depth;

        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();

        if (il == null)
            return null;

        var typeArguments = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        foreach (int token in callTokens(il))
        {
            MethodBase? callee;

            try
            {
                callee = method.Module.ResolveMethod(token, typeArguments, methodArguments);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (callee == null)
                continue;

            string? path = pathToEstimate(callee, depth - 1, explored);

            if (path != null)
                return $"{describe(method)} -> {path}";
        }

        return null;
    }

    private static bool followed(MethodBase method)
    {
        string space = method.DeclaringType?.Namespace ?? string.Empty;

        return method.DeclaringType?.Assembly == typeof(Vector2).Assembly
            ? space == typeof(Vector2).Namespace
            : space is "System.Numerics" or "System.Runtime.Intrinsics" || method.DeclaringType == typeof(float) || method.DeclaringType == typeof(MathF);
    }

    private static string describe(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";

    private static readonly OpCode[] one_byte_codes = new OpCode[0x100];
    private static readonly OpCode[] two_byte_codes = new OpCode[0x100];

    static PerVertexMathTest()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var code = (OpCode)field.GetValue(null)!;
            ushort value = (ushort)code.Value;

            if (code.Size == 1)
                one_byte_codes[value] = code;
            else
                two_byte_codes[value & 0xFF] = code;
        }
    }

    /// <summary>
    /// The method tokens of every call, virtual call and construction in a method body.
    /// </summary>
    private static IEnumerable<int> callTokens(byte[] il)
    {
        for (int i = 0; i < il.Length;)
        {
            var code = il[i] == 0xFE ? two_byte_codes[il[++i]] : one_byte_codes[il[i]];
            i++;

            if (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj)
                yield return BitConverter.ToInt32(il, i);

            i += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
        }
    }
}
