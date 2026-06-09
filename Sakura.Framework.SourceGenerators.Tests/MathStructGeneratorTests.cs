// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.SourceGenerators.Generators;

namespace Sakura.Framework.SourceGenerators.Tests;

/// <summary>
/// Tests for <see cref="MathStructGenerator"/>.
/// Each test compiles a minimal in-memory struct that stubs the MathStruct pattern
/// (a partial struct with a <c>Value</c> field wrapping a System.Numerics type),
/// runs the generator, and asserts on the produced output. Mostly test that "it just work"
/// </summary>
[TestFixture]
public class MathStructGeneratorTests
{
    private const string attribute_stub = """
        namespace Sakura.Framework.Maths
        {
            [System.AttributeUsage(System.AttributeTargets.Struct)]
            public class MathStructAttribute : System.Attribute { }
        }
        """;

    private const string vector2_source = """
        using Sakura.Framework.Maths;
        using SystemVector2 = System.Numerics.Vector2;

        namespace Sakura.Framework.Maths
        {
            [MathStruct]
            public partial struct Vector2
            {
                public SystemVector2 Value;

                public float X { get => Value.X; set => Value.X = value; }
                public float Y { get => Value.Y; set => Value.Y = value; }
            }
        }
        """;

    // Generator runs without errors on a valid MathStruct

    [Test]
    public void Generator_runs_without_diagnostics_on_valid_struct()
    {
        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, vector2_source);
        GeneratorTestHelper.AssertNoDiagnostics(result);
    }

    // Generator produces at least one output file for a [MathStruct] type

    [Test]
    public void Generator_produces_output_file_for_MathStruct()
    {
        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, vector2_source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);

        Assert.That(all, Is.Not.Empty, "Expected at least one generated file for [MathStruct] Vector2.");
    }

    // Generated output declares a partial struct in the correct namespace

    [Test]
    public void Generated_file_declares_partial_struct_in_correct_namespace()
    {
        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, vector2_source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);

        string? vector2File = all.Values.FirstOrDefault(s => s.Contains("partial struct Vector2"));
        Assert.That(vector2File, Is.Not.Null, "No generated file contains 'partial struct Vector2'.");
        Assert.That(vector2File, Does.Contain("namespace Sakura.Framework.Maths"));
    }

    // Constructor forwarding — generated file should contain a constructor
    // that takes the underlying System.Numerics params and assigns this.Value

    [Test]
    public void Generated_file_contains_constructor_forwarding_to_Value()
    {
        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, vector2_source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);
        string vector2File = all.Values.First(s => s.Contains("partial struct Vector2"));

        Assert.Multiple(() =>
        {
            Assert.That(vector2File, Does.Contain("public Vector2("));
            Assert.That(vector2File, Does.Contain("this.Value = new System.Numerics.Vector2("));
        });
    }

    // Arithmetic operator overloads are generated (+, -, *)

    [Test]
    public void Generated_file_contains_arithmetic_operator_overloads()
    {
        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, vector2_source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);
        string vector2File = all.Values.First(s => s.Contains("partial struct Vector2"));

        Assert.Multiple(() =>
        {
            Assert.That(vector2File, Does.Contain("operator +"));
            Assert.That(vector2File, Does.Contain("operator -"));
            Assert.That(vector2File, Does.Contain("operator *"));
        });
    }

    // Equality operators (== and !=) are generated

    [Test]
    public void Generated_file_contains_equality_operators()
    {
        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, vector2_source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);
        string vector2File = all.Values.First(s => s.Contains("partial struct Vector2"));

        Assert.Multiple(() =>
        {
            Assert.That(vector2File, Does.Contain("operator =="));
            Assert.That(vector2File, Does.Contain("operator !="));
        });
    }

    // IEquatable<T> is implemented when the underlying type supports it
    // (System.Numerics.Vector2 implements IEquatable<Vector2>)

    [Test]
    public void Generated_file_implements_IEquatable_when_underlying_type_supports_it()
    {
        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, vector2_source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);
        string vector2File = all.Values.First(s => s.Contains("partial struct Vector2"));

        Assert.Multiple(() =>
        {
            Assert.That(vector2File, Does.Contain("System.IEquatable<Vector2>"));
            Assert.That(vector2File, Does.Contain("public bool Equals(Vector2"));
            Assert.That(vector2File, Does.Contain("public override int GetHashCode()"));
        });
    }

    // Static method forwarding — Normalize, Dot, etc. from System.Numerics.Vector2

    [Test]
    public void Generated_file_contains_static_method_forwards()
    {
        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, vector2_source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);
        string vector2File = all.Values.First(s => s.Contains("partial struct Vector2"));

        // System.Numerics.Vector2 has Normalize, Dot, Distance etc.
        Assert.That(vector2File, Does.Contain("public static").And.Contain("Normalize").Or.Contain("Dot").Or.Contain("Distance"),
            "Expected at least one static method forward (Normalize, Dot, or Distance).");
    }

    // AggressiveInlining attribute is applied to generated members

    [Test]
    public void Generated_members_are_decorated_with_AggressiveInlining()
    {
        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, vector2_source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);
        string vector2File = all.Values.First(s => s.Contains("partial struct Vector2"));

        Assert.That(vector2File, Does.Contain("MethodImplOptions.AggressiveInlining"));
    }

    // Struct without [MathStruct] produces no output

    [Test]
    public void Struct_without_MathStruct_attribute_produces_no_output()
    {
        const string source = """
            namespace WaguriApp
            {
                public partial struct PlainStruct
                {
                    public float Value;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);

        Assert.That(all, Is.Empty, "Generator should produce no output for structs without [MathStruct].");
    }

    // Multiple [MathStruct] types in one compilation each get their own file

    [Test]
    public void Multiple_MathStruct_types_each_get_own_generated_file()
    {
        const string multi_source = """
            using Sakura.Framework.Maths;
            using SystemVector2 = System.Numerics.Vector2;
            using SystemVector3 = System.Numerics.Vector3;

            namespace Sakura.Framework.Maths
            {
                [MathStruct]
                public partial struct Vector2 { public SystemVector2 Value; }

                [MathStruct]
                public partial struct Vector3 { public SystemVector3 Value; }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, multi_source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);

        Assert.That(all.Count, Is.GreaterThanOrEqualTo(2), "Expected separate generated files for Vector2 and Vector3.");

        bool hasVector2 = all.Values.Any(s => s.Contains("partial struct Vector2"));
        bool hasVector3 = all.Values.Any(s => s.Contains("partial struct Vector3"));

        Assert.Multiple(() =>
        {
            Assert.That(hasVector2, Is.True, "No generated file for Vector2.");
            Assert.That(hasVector3, Is.True, "No generated file for Vector3.");
        });
    }

    // Generated code contains auto-generated header comment

    [Test]
    public void Generated_file_contains_auto_generated_header()
    {
        var result = GeneratorTestHelper.RunGenerator<MathStructGenerator>(attribute_stub, vector2_source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);
        string vector2File = all.Values.First(s => s.Contains("partial struct Vector2"));

        Assert.That(vector2File, Does.Contain("<auto-generated>"));
    }
}
