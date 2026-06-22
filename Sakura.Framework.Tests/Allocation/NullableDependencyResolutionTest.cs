// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Allocation;

namespace Sakura.Framework.Tests.Allocation;

/// <summary>
/// Verifies that <c>[Resolved(canBeNull: true)]</c> and <c>[CanBeNull]</c> loader parameters
/// resolve missing dependencies to <c>null</c> instead of throwing
/// </summary>
[TestFixture]
public partial class NullableDependencyResolutionTest
{
    private static IReadOnlyDependencyContainer emptyContainer() => new DependencyContainer();

    private static IReadOnlyDependencyContainer containerWith(IService service)
    {
        var c = new DependencyContainer();
        c.Cache(service);
        return c;
    }

    [Test]
    public void Optional_resolved_member_is_null_when_missing()
    {
        var target = new GeneratedConsumer();
        DependencyActivator.Inject(target, emptyContainer());

        Assert.That(target.OptionalService, Is.Null);
    }

    [Test]
    public void Optional_resolved_member_is_populated_when_present()
    {
        var service = new ServiceImpl();
        var target = new GeneratedConsumer();
        DependencyActivator.Inject(target, containerWith(service));

        Assert.That(target.OptionalService, Is.SameAs(service));
    }

    [Test]
    public void Required_resolved_member_throws_when_missing()
    {
        var target = new RequiredConsumer();
        Assert.Throws<InvalidOperationException>(() => DependencyActivator.Inject(target, emptyContainer()));
    }

    [Test]
    public void Optional_loader_parameter_is_null_when_missing()
    {
        var target = new GeneratedLoaderConsumer();
        DependencyActivator.Inject(target, emptyContainer());

        Assert.That(target.LoaderSawNull, Is.True);
    }

    [Test]
    public void Reflection_fallback_optional_member_is_null_when_missing()
    {
        var target = new ReflectionConsumer();
        DependencyActivator.Inject(target, emptyContainer());

        Assert.That(target.OptionalService, Is.Null);
    }

    [Test]
    public void Reflection_fallback_optional_member_is_populated_when_present()
    {
        var service = new ServiceImpl();
        var target = new ReflectionConsumer();
        DependencyActivator.Inject(target, containerWith(service));

        Assert.That(target.OptionalService, Is.SameAs(service));
    }

    public interface IService { }

    private class ServiceImpl : IService { }

    public partial class GeneratedConsumer : IDependencyInjectionCandidate
    {
        [Resolved(canBeNull: true)]
        public IService? OptionalService { get; set; }
    }

    public partial class RequiredConsumer : IDependencyInjectionCandidate
    {
        [Resolved]
        public IService Service { get; set; } = null!;
    }

    public partial class GeneratedLoaderConsumer : IDependencyInjectionCandidate
    {
        public bool LoaderSawNull { get; private set; }

        [BackgroundDependencyLoader]
        private void load([CanBeNull] IService service)
        {
            LoaderSawNull = service == null;
        }
    }

    // forces the reflection fallback in ReflectionDependencyActivator.
    public class ReflectionConsumer : IDependencyInjectionCandidate
    {
        [Resolved(canBeNull: true)]
        public IService? OptionalService { get; set; }
    }
}
