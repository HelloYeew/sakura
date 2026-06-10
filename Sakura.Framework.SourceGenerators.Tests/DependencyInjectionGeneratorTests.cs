// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Linq;
using NUnit.Framework;
using Sakura.Framework.SourceGenerators.Generators.Dependencies;

namespace Sakura.Framework.SourceGenerators.Tests;

/// <summary>
/// Tests for <see cref="DependencyInjectionGenerator"/>.
/// Each test compiles a minimal in-memory C# source that stubs the Sakura DI attributes
/// and interfaces, runs the generator, and asserts on the produced partial class output.
/// </summary>
[TestFixture]
public class DependencyInjectionGeneratorTests
{
    // Shared attribute/interface stubs injected into every test compilation.
    // These mirror the real types in Sakura.Framework.Allocation so the generator
    // can resolve them by fully qualified name.

    private const string stubs = """
        namespace Sakura.Framework.Allocation
        {
            public interface IDependencyInjectionCandidate { }
            public interface ISourceGeneratedDependencyActivator
            {
                void RegisterForDependencyActivation(IDependencyActivatorRegistry registry);
            }
            public interface IDependencyActivatorRegistry
            {
                bool IsRegistered(System.Type type);
                void Register(System.Type type, InjectDependenciesDelegate? inject, CacheDependenciesDelegate? cache);
            }
            public delegate void InjectDependenciesDelegate(object target, IReadOnlyDependencyContainer dependencies);
            public delegate IReadOnlyDependencyContainer CacheDependenciesDelegate(object target, IReadOnlyDependencyContainer? parent);
            public interface IReadOnlyDependencyContainer { T Get<T>() where T : class; void Inject<T>(T instance) where T : class; }
            public class DependencyContainer : IReadOnlyDependencyContainer
            {
                public DependencyContainer(IReadOnlyDependencyContainer? parent = null) { }
                public T Get<T>() where T : class => default!;
                public void Inject<T>(T instance) where T : class { }
                public void CacheAs<T>(object instance) where T : class { }
            }

            [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field)]
            public class ResolvedAttribute : System.Attribute { }

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

    // root drawable implements IDependencyInjectionCandidate directly, no DI attrs, should emit a 'virtual' method with null,null delegates.

    [Test]
    public void Root_class_emits_virtual_method_with_null_delegates()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_Drawable.DI.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain("public virtual void RegisterForDependencyActivation"));
            Assert.That(generated, Does.Contain(": global::Sakura.Framework.Allocation.ISourceGeneratedDependencyActivator"));
            Assert.That(generated, Does.Not.Contain("base.RegisterForDependencyActivation"));
            // Both delegates should be null — nothing to inject or cache
            Assert.That(generated, Does.Contain("null,"));
            Assert.That(generated, Does.Contain("null"));
        });
    }

    // [Resolved] property — should generate inject delegate that calls deps.Get<T>()

    [Test]
    public void Resolved_property_generates_inject_delegate()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class SpriteText : Drawable
                {
                    [Resolved]
                    private IFontStore fontStore { get; set; } = null!;
                }

                public interface IFontStore { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_SpriteText.DI.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain("public override void RegisterForDependencyActivation"));
            Assert.That(generated, Does.Contain("base.RegisterForDependencyActivation(registry)"));
            Assert.That(generated, Does.Contain("self.fontStore = deps.Get<global::WaguriApp.IFontStore>()"));
            // No [Cached] — cache delegate should be null
            Assert.That(generated, Does.Contain("null"));
        });
    }

    // [Resolved] field (not property)

    [Test]
    public void Resolved_field_generates_inject_delegate()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    [Resolved]
                    private IAudioManager audioManager = null!;
                }

                public interface IAudioManager { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_MyComponent.DI.g.cs");

        Assert.That(generated, Does.Contain("self.audioManager = deps.Get<global::WaguriApp.IAudioManager>()"));
    }

    // [BackgroundDependencyLoader] with parameters

    [Test]
    public void BackgroundDependencyLoader_with_params_generates_method_call()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class VideoSprite : Drawable
                {
                    [BackgroundDependencyLoader]
                    private void load(IRenderer renderer, ITextureManager textures) { }
                }

                public interface IRenderer { }
                public interface ITextureManager { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_VideoSprite.DI.g.cs");

        Assert.That(generated, Does.Contain("self.load(deps.Get<global::WaguriApp.IRenderer>(), deps.Get<global::WaguriApp.ITextureManager>())"));
    }

    // [BackgroundDependencyLoader] with no parameters

    [Test]
    public void BackgroundDependencyLoader_no_params_generates_parameterless_call()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    [BackgroundDependencyLoader]
                    private void load() { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_MyComponent.DI.g.cs");

        Assert.That(generated, Does.Contain("self.load()"));
    }

    // [Resolved] + [BackgroundDependencyLoader] together

    [Test]
    public void Resolved_and_BackgroundLoader_both_appear_in_inject_delegate()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class MyComponent : Drawable
                {
                    [Resolved]
                    private IService service { get; set; } = null!;

                    [BackgroundDependencyLoader]
                    private void load(IRenderer renderer) { }
                }

                public interface IService { }
                public interface IRenderer { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_MyComponent.DI.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain("self.service = deps.Get<global::WaguriApp.IService>()"));
            Assert.That(generated, Does.Contain("self.load(deps.Get<global::WaguriApp.IRenderer>())"));
        });
    }

    // Class-level [Cached] — cache this as own type

    [Test]
    public void Class_level_Cached_generates_cache_delegate_for_self()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                [Cached]
                public partial class AudioManager : Drawable { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_AudioManager.DI.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain("(target, parent) =>"));
            Assert.That(generated, Does.Contain("deps.CacheAs<global::WaguriApp.AudioManager>(self)"));
        });
    }

    // Class-level [Cached(typeof(IFoo))] — cache as interface

    [Test]
    public void Class_level_Cached_with_type_arg_caches_as_interface()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public interface IAudioManager { }

                [Cached(typeof(IAudioManager))]
                public partial class AudioManager : Drawable, IAudioManager { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_AudioManager.DI.g.cs");

        Assert.That(generated, Does.Contain("deps.CacheAs<global::WaguriApp.IAudioManager>(self)"));
    }

    // Member-level [Cached] on a property

    [Test]
    public void Member_level_Cached_property_generates_cache_delegate()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public interface IRenderer { }

                public partial class AppHost : Drawable
                {
                    [Cached]
                    public IRenderer Renderer { get; } = null!;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_AppHost.DI.g.cs");

        Assert.That(generated, Does.Contain("deps.CacheAs<global::WaguriApp.IRenderer>(self.Renderer)"));
    }

    // Multi-level inheritance: base has [Resolved], derived has [Resolved]
    // Both should get their own generated files. Derived should call base.

    [Test]
    public void Multi_level_inheritance_each_type_gets_own_generated_file()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class Container : Drawable
                {
                    [Resolved]
                    private IWindow window { get; set; } = null!;
                }

                public partial class FpsGraph : Container
                {
                    [Resolved]
                    private IRenderer renderer { get; set; } = null!;
                }

                public interface IWindow { }
                public interface IRenderer { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);

        // All three types should have files
        Assert.That(all.Keys, Has.Some.EndsWith("WaguriApp_Drawable.DI.g.cs"));
        Assert.That(all.Keys, Has.Some.EndsWith("WaguriApp_Container.DI.g.cs"));
        Assert.That(all.Keys, Has.Some.EndsWith("WaguriApp_FpsGraph.DI.g.cs"));

        string containerSource = all.First(kv => kv.Key.EndsWith("WaguriApp_Container.DI.g.cs")).Value;
        string fpsSource = all.First(kv => kv.Key.EndsWith("WaguriApp_FpsGraph.DI.g.cs")).Value;

        Assert.Multiple(() =>
        {
            // Container: override (base Drawable is candidate), injects window
            Assert.That(containerSource, Does.Contain("public override void RegisterForDependencyActivation"));
            Assert.That(containerSource, Does.Contain("self.window = deps.Get<global::WaguriApp.IWindow>()"));

            // FpsGraph: override, calls base, injects renderer
            Assert.That(fpsSource, Does.Contain("public override void RegisterForDependencyActivation"));
            Assert.That(fpsSource, Does.Contain("base.RegisterForDependencyActivation(registry)"));
            Assert.That(fpsSource, Does.Contain("self.renderer = deps.Get<global::WaguriApp.IRenderer>()"));
        });
    }

    // Non-partial class is silently skipped (no generated file), will fallback to reflection

    [Test]
    public void Non_partial_class_is_skipped_no_generated_file()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                // NOT partial — generator must skip it
                public class SpriteText : Drawable
                {
                    [Resolved]
                    private IFontStore fontStore { get; set; } = null!;
                }

                public interface IFontStore { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);

        Assert.That(all.Keys, Has.None.EndsWith("WaguriApp_SpriteText.DI.g.cs"));
    }

    // Guard: IsRegistered check appears in generated code

    [Test]
    public void Generated_code_contains_IsRegistered_guard()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
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

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_MyComponent.DI.g.cs");

        Assert.That(generated, Does.Contain("if (registry.IsRegistered(typeof(global::WaguriApp.MyComponent)))"));
    }

    // Generator produces no diagnostics on valid input

    [Test]
    public void Generator_produces_no_diagnostics_on_valid_input()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public interface IService { }

                [Cached(typeof(IService))]
                public partial class MyComponent : Drawable, IService
                {
                    [Resolved]
                    private IService service { get; set; } = null!;

                    [BackgroundDependencyLoader]
                    private void load(IService svc) { }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        GeneratorTestHelper.AssertNoDiagnostics(result);
    }

    // Nested class tests (EnclosingTypes support)
    // A DI candidate nested inside a non-DI partial class should emit a file wrapped
    // in an outer `partial class` block so the C# compiler can locate the type.

    [Test]
    public void Nested_class_generated_file_is_wrapped_in_enclosing_partial_class()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class CursorContainer : Drawable
                {
                    public partial class DefaultCursor : Drawable
                    {
                        [Resolved]
                        private IService service { get; set; } = null!;
                    }
                }

                public interface IService { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        // hint name: WaguriApp_CursorContainer_DefaultCursor.DI.g.cs
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_CursorContainer_DefaultCursor.DI.g.cs");

        Assert.Multiple(() =>
        {
            // Enclosing wrapper must be present
            Assert.That(generated, Does.Contain("partial class CursorContainer"));
            // Inner class declaration must be inside it
            Assert.That(generated, Does.Contain("partial class DefaultCursor"));
            // DI injection must still be emitted
            Assert.That(generated, Does.Contain("self.service = deps.Get<global::WaguriApp.IService>()"));
            // Namespace block should be outermost
            Assert.That(generated, Does.Contain("namespace WaguriApp"));
        });
    }

    // Verify correct brace nesting: namespace > outer class > inner class > generated method.

    [Test]
    public void Nested_class_generated_file_has_correct_indentation_and_braces()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class Outer : Drawable
                {
                    public partial class Inner : Drawable
                    {
                        [BackgroundDependencyLoader]
                        private void load(IService svc) { }
                    }
                }

                public interface IService { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_Outer_Inner.DI.g.cs");

        // Count opening vs closing braces — must be balanced.
        int opens  = generated.Count(c => c == '{');
        int closes = generated.Count(c => c == '}');
        Assert.That(opens, Is.EqualTo(closes), "Generated file has unbalanced braces");
    }

    // Double nesting: Grandparent > Parent > Child, all partial DI candidates.

    [Test]
    public void Double_nested_class_wraps_two_enclosing_partial_classes()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class GrandParent : Drawable
                {
                    public partial class Parent : Drawable
                    {
                        public partial class Child : Drawable
                        {
                            [Resolved]
                            private IService service { get; set; } = null!;
                        }
                    }
                }

                public interface IService { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        // hint name: WaguriApp_GrandParent_Parent_Child.DI.g.cs
        string generated = GeneratorTestHelper.GetGeneratedSource(result, "WaguriApp_GrandParent_Parent_Child.DI.g.cs");

        Assert.Multiple(() =>
        {
            Assert.That(generated, Does.Contain("partial class GrandParent"));
            Assert.That(generated, Does.Contain("partial class Parent"));
            Assert.That(generated, Does.Contain("partial class Child"));
            Assert.That(generated, Does.Contain("self.service = deps.Get<global::WaguriApp.IService>()"));

            int opens  = generated.Count(c => c == '{');
            int closes = generated.Count(c => c == '}');
            Assert.That(opens, Is.EqualTo(closes), "Generated file has unbalanced braces");
        });
    }

    // Generic enclosing type: the wrapper must include the type parameter list.

    [Test]
    public void Generic_enclosing_type_includes_type_params_in_wrapper()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                public partial class Container<T> : Drawable
                {
                    public partial class Inner : Drawable
                    {
                        [Resolved]
                        private IService service { get; set; } = null!;
                    }
                }

                public interface IService { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        // hint name uses '<' → '_': WaguriApp_Container_T__Inner.DI.g.cs
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);
        string? innerKey = all.Keys.FirstOrDefault(k => k.Contains("Inner") && k.EndsWith(".DI.g.cs"));

        Assert.That(innerKey, Is.Not.Null, "No generated file found for Container<T>.Inner");

        string generated = all[innerKey!];

        Assert.Multiple(() =>
        {
            // Enclosing wrapper must carry the type parameter
            Assert.That(generated, Does.Contain("partial class Container<T>"));
            Assert.That(generated, Does.Contain("partial class Inner"));
            Assert.That(generated, Does.Contain("self.service = deps.Get<global::WaguriApp.IService>()"));
        });
    }

    // A relay type (no DI attrs, but sits between two DI types) should get its own
    // generated file so that subclasses can safely emit `override`.

    [Test]
    public void Relay_nested_type_without_di_attrs_still_gets_generated_file()
    {
        const string source = """
            using Sakura.Framework.Allocation;
            namespace WaguriApp
            {
                public abstract partial class Drawable : IDependencyInjectionCandidate { }

                // No DI attributes — relay only.
                public partial class CursorContainer : Drawable
                {
                    public partial class RelayCursor : Drawable { }

                    public partial class CursorZone : RelayCursor
                    {
                        [Resolved]
                        private IService service { get; set; } = null!;
                    }
                }

                public interface IService { }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<DependencyInjectionGenerator>(stubs, source);
        var all = GeneratorTestHelper.GetAllGeneratedSources(result);

        // RelayCursor must have a file even though it has no DI attrs
        Assert.That(all.Keys, Has.Some.EndsWith("WaguriApp_CursorContainer_RelayCursor.DI.g.cs"),
            "Relay nested type should still produce a generated file");

        string relaySrc = all.First(kv => kv.Key.EndsWith("WaguriApp_CursorContainer_RelayCursor.DI.g.cs")).Value;
        // Its method must be override (base Drawable is a candidate) and call base
        Assert.That(relaySrc, Does.Contain("public override void RegisterForDependencyActivation"));

        // CursorZone must override and inject
        string zoneSrc = all.First(kv => kv.Key.EndsWith("WaguriApp_CursorContainer_CursorZone.DI.g.cs")).Value;
        Assert.Multiple(() =>
        {
            Assert.That(zoneSrc, Does.Contain("public override void RegisterForDependencyActivation"));
            Assert.That(zoneSrc, Does.Contain("self.service = deps.Get<global::WaguriApp.IService>()"));
        });
    }
}
