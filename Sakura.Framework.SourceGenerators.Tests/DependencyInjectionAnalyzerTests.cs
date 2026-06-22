// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Sakura.Framework.CodeFixes.Analyzers;
using Sakura.Framework.SourceGenerators.Analyzers;

namespace Sakura.Framework.SourceGenerators.Tests;

/// <summary>
/// Tests for <see cref="DependencyInjectionAnalyzer"/>.
/// Each test compiles a minimal in-memory C# source that stubs the Sakura DI attributes
/// and interfaces, runs the analyzer, and asserts on the reported diagnostics.
/// Code-fix tests additionally assert on the text produced by <see cref="AddPartialModifierCodeFix"/>.
/// </summary>
[TestFixture]
public class DependencyInjectionAnalyzerTests
{
    private const string stubs = """
        namespace Sakura.Framework.Allocation
        {
            public interface IDependencyInjectionCandidate { }
            public interface IReadOnlyDependencyContainer { T Get<T>() where T : class; T? TryGet<T>() where T : class; }
            public class DependencyContainer : IReadOnlyDependencyContainer
            {
                public DependencyContainer(IReadOnlyDependencyContainer? parent = null) { }
                public T Get<T>() where T : class => default!;
                public T? TryGet<T>() where T : class => default;
                public void CacheAs<T>(object instance) where T : class { }
            }

            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
            public class ResolvedAttribute : System.Attribute
            {
                public bool CanBeNull { get; init; }
                public ResolvedAttribute() { }
                public ResolvedAttribute(bool canBeNull) { CanBeNull = canBeNull; }
            }

            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class CanBeNullAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public class BackgroundDependencyLoaderAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Property | System.AttributeTargets.Field, AllowMultiple = true)]
            public class CachedAttribute : System.Attribute
            {
                public System.Type? CacheAs { get; }
                public CachedAttribute() { }
                public CachedAttribute(System.Type cacheAs) { CacheAs = cacheAs; }
            }
        }
        """;

    // -------------------------------------------------------------------------
    // SFDI0001 — missing partial on DI class
    // -------------------------------------------------------------------------

    [Test]
    public void SFDI0001_reported_when_DI_class_is_not_partial()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                // NOT partial — should trigger SFDI0001
                public class SpriteText : Drawable
                {
                    [Resolved]
                    private IFont font { get; set; } = null!;
                }

                public interface IFont { }
            }
            """;

        AnalyzerTestHelper.AssertSingleDiagnostic<DependencyInjectionAnalyzer>(
            "SFDI0001", stubs, source);
    }

    [Test]
    public void SFDI0001_not_reported_when_DI_class_is_partial()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class SpriteText : Drawable
                {
                    [Resolved]
                    private IFont font { get; set; } = null!;
                }

                public interface IFont { }
            }
            """;

        AnalyzerTestHelper.AssertNoDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
    }

    [Test]
    public void SFDI0001_not_reported_when_class_has_no_DI_attributes()
    {
        // A non-partial class with no DI attributes should produce no warning,
        // even if it happens to be in the IDependencyInjectionCandidate hierarchy.
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                // No DI attrs — no warning expected
                public class PlainContainer : Drawable { }
            }
            """;

        AnalyzerTestHelper.AssertNoDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
    }

    [Test]
    public async Task SFDI0001_code_fix_adds_partial_modifier()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public class SpriteText : Drawable
                {
                    [Resolved]
                    private IFont font { get; set; } = null!;
                }

                public interface IFont { }
            }
            """;

        string fixedSourceCode = await AnalyzerTestHelper.ApplyCodeFixAsync<
            DependencyInjectionAnalyzer,
            AddPartialModifierCodeFix>(
            "SFDI0001", stubs, source);

        Assert.That(fixedSourceCode, Does.Contain("partial class SpriteText"));
    }

    [Test]
    public void SFDI0002_reported_when_enclosing_class_is_not_partial()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                // Enclosing class is NOT partial
                public class Outer
                {
                    public partial class Inner : Drawable
                    {
                        [Resolved]
                        private IFont font { get; set; } = null!;
                    }
                }

                public interface IFont { }
            }
            """;

        AnalyzerTestHelper.AssertDiagnostic<DependencyInjectionAnalyzer>("SFDI0002", stubs, source);
    }

    [Test]
    public void SFDI0002_not_reported_when_all_enclosing_classes_are_partial()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class Outer
                {
                    public partial class Inner : Drawable
                    {
                        [Resolved]
                        private IFont font { get; set; } = null!;
                    }
                }

                public interface IFont { }
            }
            """;

        AnalyzerTestHelper.AssertNoDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
    }

    [Test]
    public async Task SFDI0002_code_fix_adds_partial_to_enclosing_class()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public class Outer
                {
                    public partial class Inner : Drawable
                    {
                        [Resolved]
                        private IFont font { get; set; } = null!;
                    }
                }

                public interface IFont { }
            }
            """;

        string fixedSourceCode = await AnalyzerTestHelper.ApplyCodeFixAsync<
            DependencyInjectionAnalyzer,
            AddPartialModifierCodeFix>(
            "SFDI0002", stubs, source);

        Assert.That(fixedSourceCode, Does.Contain("partial class Outer"));
    }

    // -------------------------------------------------------------------------
    // SFDI0003 — [Resolved] property has no setter
    // -------------------------------------------------------------------------

    [Test]
    public void SFDI0003_reported_when_resolved_property_has_no_setter()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class SpriteText : Drawable
                {
                    // get-only property — cannot be injected
                    [Resolved]
                    private IFont Font { get; } = null!;
                }

                public interface IFont { }
            }
            """;

        AnalyzerTestHelper.AssertSingleDiagnostic<DependencyInjectionAnalyzer>(
            "SFDI0003", stubs, source);
    }

    [Test]
    public void SFDI0003_not_reported_when_resolved_property_has_private_setter()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class SpriteText : Drawable
                {
                    [Resolved]
                    private IFont Font { get; set; } = null!;
                }

                public interface IFont { }
            }
            """;

        AnalyzerTestHelper.AssertNoDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
    }

    [Test]
    public void SFDI0003_not_reported_for_resolved_field()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class SpriteText : Drawable
                {
                    [Resolved]
                    private IFont font = null!;
                }

                public interface IFont { }
            }
            """;

        var diagnostics = AnalyzerTestHelper.GetDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
        Assert.That(diagnostics, Has.None.Matches<Microsoft.CodeAnalysis.Diagnostic>(d => d.Id == "SFDI0003"));
    }

    [Test]
    public void SFDI0004_reported_when_multiple_background_loaders_exist()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    [BackgroundDependencyLoader]
                    private void load() { }

                    [BackgroundDependencyLoader]
                    private void loadExtra() { }
                }
            }
            """;

        AnalyzerTestHelper.AssertDiagnostic<DependencyInjectionAnalyzer>("SFDI0004", stubs, source);
    }

    [Test]
    public void SFDI0004_not_reported_with_single_background_loader()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    [BackgroundDependencyLoader]
                    private void load() { }
                }
            }
            """;

        AnalyzerTestHelper.AssertNoDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
    }

    [Test]
    public void SFDI0005_reported_when_resolved_on_static_property()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    [Resolved]
                    private static IService service { get; set; } = null!;
                }

                public interface IService { }
            }
            """;

        AnalyzerTestHelper.AssertDiagnostic<DependencyInjectionAnalyzer>("SFDI0005", stubs, source);
    }

    [Test]
    public void SFDI0005_reported_when_resolved_on_static_field()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    [Resolved]
                    private static IService service = null!;
                }

                public interface IService { }
            }
            """;

        AnalyzerTestHelper.AssertDiagnostic<DependencyInjectionAnalyzer>("SFDI0005", stubs, source);
    }

    [Test]
    public void SFDI0005_not_reported_for_instance_resolved_member()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    [Resolved]
                    private IService service { get; set; } = null!;
                }

                public interface IService { }
            }
            """;

        var diagnostics = AnalyzerTestHelper.GetDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
        Assert.That(diagnostics, Has.None.Matches<Microsoft.CodeAnalysis.Diagnostic>(d => d.Id == "SFDI0005"));
    }

    [Test]
    public void SFDI0006_reported_when_cached_property_has_no_getter()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    private IService _service = null!;

                    // set-only property — cannot be read to cache
                    [Cached]
                    public IService Service { set => _service = value; }
                }

                public interface IService { }
            }
            """;

        AnalyzerTestHelper.AssertSingleDiagnostic<DependencyInjectionAnalyzer>(
            "SFDI0006", stubs, source);
    }

    [Test]
    public void SFDI0006_not_reported_when_cached_property_has_getter()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    [Cached]
                    public IService Service { get; } = null!;
                }

                public interface IService { }
            }
            """;

        var diagnostics = AnalyzerTestHelper.GetDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
        Assert.That(diagnostics, Has.None.Matches<Microsoft.CodeAnalysis.Diagnostic>(d => d.Id == "SFDI0006"));
    }

    [Test]
    public void SFDI0006_not_reported_for_cached_field()
    {
        // Fields can always be read — SFDI0006 should not fire for them.
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    [Cached]
                    private readonly IService service = null!;
                }

                public interface IService { }
            }
            """;

        var diagnostics = AnalyzerTestHelper.GetDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
        Assert.That(diagnostics, Has.None.Matches<Microsoft.CodeAnalysis.Diagnostic>(d => d.Id == "SFDI0006"));
    }

    [Test]
    public void No_diagnostics_on_fully_correct_DI_usage()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public interface IRenderer { }
                public interface IAudioManager { }

                [Cached(typeof(IAudioManager))]
                public partial class AudioManager : Drawable, IAudioManager { }

                public partial class GameScene : Drawable
                {
                    [Resolved]
                    private IRenderer renderer { get; set; } = null!;

                    [Cached]
                    private readonly AudioManager audioManager = null!;

                    [BackgroundDependencyLoader]
                    private void load(IRenderer r) { }
                }
            }
            """;

        AnalyzerTestHelper.AssertNoDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
    }

    [Test]
    public void SFDI0007_reported_when_canBeNull_member_is_non_nullable()
    {
        const string source = """
            #nullable enable
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class SpriteText : Drawable
                {
                    // Non-nullable type but marked optional — should trigger SFDI0007.
                    [Resolved(canBeNull: true)]
                    private IFont font { get; set; } = null!;
                }

                public interface IFont { }
            }
            """;

        AnalyzerTestHelper.AssertSingleDiagnostic<DependencyInjectionAnalyzer>("SFDI0007", stubs, source);
    }

    [Test]
    public void SFDI0007_not_reported_when_canBeNull_member_is_nullable()
    {
        const string source = """
            #nullable enable
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class SpriteText : Drawable
                {
                    [Resolved(canBeNull: true)]
                    private IFont? font { get; set; }
                }

                public interface IFont { }
            }
            """;

        var diagnostics = AnalyzerTestHelper.GetDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
        Assert.That(diagnostics.Any(d => d.Id == "SFDI0007"), Is.False);
    }

    [Test]
    public void SFDI0007_not_reported_for_required_resolved_member()
    {
        const string source = """
            #nullable enable
            using Sakura.Framework.Allocation;
            namespace WaguriGame
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class SpriteText : Drawable
                {
                    [Resolved]
                    private IFont font { get; set; } = null!;
                }

                public interface IFont { }
            }
            """;

        var diagnostics = AnalyzerTestHelper.GetDiagnostics<DependencyInjectionAnalyzer>(stubs, source);
        Assert.That(diagnostics.Any(d => d.Id == "SFDI0007"), Is.False);
    }
}
