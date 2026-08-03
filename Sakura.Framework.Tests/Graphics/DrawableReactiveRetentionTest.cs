// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Reactive;

namespace Sakura.Framework.Tests.Graphics;

/// <summary>
/// Measures what <see cref="Reactive{T}"/> retains, and verifies the unbind-on-dispose that answers it.
/// </summary>
[TestFixture]
public class DrawableReactiveRetentionTest
{
    [SetUp]
    public void SetUp()
    {
        // Retention is measured against explicit disposal, not against a queue drain.
        DrawableDisposalQueue.Enabled = false;
        DrawableDisposalQueue.Flush();
    }

    /// <summary>
    /// The measurement itself, asserted so it cannot quietly change: subscribing to a long-lived reactive
    /// directly keeps the drawable alive even after it has been disposed.
    /// </summary>
    [Test]
    public void SubscribingDirectlyRetainsTheDrawableForever()
    {
        var setting = new Reactive<bool>(false);

        var reference = subscribeAndAbandon(setting);
        collect();

        Assert.That(reference.IsAlive, Is.True,
            "a drawable that subscribed to a long-lived reactive without tracking the subscription stays reachable from it");

        // Reachable through the setting, and only through it.
        setting.UnbindAll();
        GC.KeepAlive(setting);
    }

    [Test]
    public void TheTrackedHelperReleasesTheDrawableOnDispose()
    {
        var setting = new Reactive<bool>(false);

        var reference = bindTrackedAndAbandon(setting);
        collect();

        Assert.That(reference.IsAlive, Is.False, "disposal should have unsubscribed the handler");
        GC.KeepAlive(setting);
    }

    /// <summary>
    /// The reverse direction, which is how a settings screen actually wires a control up:
    /// <c>control.Current.BindTo(setting)</c> leaves the setting holding a handler that reaches into
    /// <c>Current</c>, whose own subscribers reach back into the control. The binding is made by the
    /// caller, so only the control unbinding the reactive it owns can sever it.
    /// </summary>
    [Test]
    public void DeclaringAnOwnedReactiveReleasesADrawableBoundByItsCaller()
    {
        var setting = new Reactive<bool>(false);

        var reference = bindOwnedReactiveAndAbandon(setting);
        collect();

        Assert.That(reference.IsAlive, Is.False);
        GC.KeepAlive(setting);
    }

    /// <summary>
    /// Unbinding must never reach past what this drawable itself bound. The sources are shared — a config
    /// setting is one instance handed to every caller, with the config manager's own persistence handler
    /// on that same event — so severing another subscriber would break unrelated code.
    /// </summary>
    [Test]
    public void UnbindingOnDisposeLeavesOtherSubscribersAlone()
    {
        var setting = new Reactive<bool>(false);
        int otherSubscriberCalls = 0;

        setting.ValueChanged += _ => otherSubscriberCalls++;

        var drawable = new BindingDrawable();
        drawable.TrackBindValueChanged(setting);
        drawable.Dispose();

        setting.Value = true;

        Assert.Multiple(() =>
        {
            Assert.That(drawable.Notifications, Is.Zero, "the disposed drawable must no longer be notified");
            Assert.That(otherSubscriberCalls, Is.EqualTo(1), "an unrelated subscriber must survive");
        });
    }

    /// <summary>
    /// The cascade has to unbind the whole subtree, not just the removed node — a nested drawable bound to
    /// a setting is the exact case <c>OnParentChanged</c> could never see.
    /// </summary>
    [Test]
    public void TheCascadeUnbindsNestedDrawables()
    {
        var setting = new Reactive<bool>(false);
        var root = new Container();
        var middle = new Container();
        var nested = new BindingDrawable();

        nested.TrackBindValueChanged(setting);
        middle.Add(nested);
        root.Add(middle);

        root.Remove(middle);

        setting.Value = true;

        Assert.That(nested.Notifications, Is.Zero);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference subscribeAndAbandon(Reactive<bool> setting)
    {
        var drawable = new BindingDrawable();

        // What a caller writes when it does not know about the tracked helper.
        setting.BindValueChanged(_ => drawable.Notify());
        drawable.Dispose();

        return new WeakReference(drawable);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference bindTrackedAndAbandon(Reactive<bool> setting)
    {
        var drawable = new BindingDrawable();

        drawable.TrackBindValueChanged(setting);
        drawable.Dispose();

        return new WeakReference(drawable);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference bindOwnedReactiveAndAbandon(Reactive<bool> setting)
    {
        var drawable = new BindingDrawable();

        // The caller's half of the wiring, exactly as a settings screen writes it.
        drawable.Current.BindTo(setting);
        drawable.Dispose();

        return new WeakReference(drawable);
    }

    private static void collect()
    {
        for (int i = 0; i < 2; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
        }
    }

    private partial class BindingDrawable : Container
    {
        /// <summary>
        /// A reactive this drawable exposes for others to bind to, in the shape of a control's
        /// <c>Current</c>.
        /// </summary>
        public readonly Reactive<bool> Current = new Reactive<bool>(false);

        public int Notifications { get; private set; }

        public BindingDrawable()
        {
            Current.ValueChanged += _ => Notify();
            OwnReactive(Current);
        }

        public void Notify() => Notifications++;

        public void TrackBindValueChanged(IReactive<bool> reactive) => BindValueChanged(reactive, _ => Notify());
    }
}
