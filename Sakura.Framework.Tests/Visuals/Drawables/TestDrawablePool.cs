// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Pooling;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public partial class TestDrawablePool : TestScene
{
    private DrawablePool<TestDrawable>? pool;
    private SpriteText? count;

    private readonly HashSet<TestDrawable> consumed = new HashSet<TestDrawable>();

    private const double time_per_action = 200;

    [Test]
    public void TestPoolInitialDrawableLoadedAheadOfTime()
    {
        const int pool_size = 3;
        resetWithNewPool(() => new TestPool(time_per_action, pool_size));

        AddRepeatStep("Check drawable is in ready state",
            () => Assert.That(pool.Get().IsLoaded, Is.True), 3);
    }

    [Test]
    public void TestPoolUsageWithinLimits()
    {
        const int pool_size = 10;
        resetWithNewPool(() => new TestPool(0, pool_size));

        AddRepeatStep("Get new pooled drawables", () => consumeDrawable(), 50);

        AddUntilStep("All returned to pool", () => pool.CountAvailable == pool.CurrentPoolSize);

        AddAssert("Consumed drawables report returned to pool", () => consumed.All(d => !d.IsInUse));

        AddAssert("No drawables leaked", () => pool.CountInUse == 0);
    }

    [Test]
    public void TestPoolUsageExceedsLimits()
    {
        const int pool_size = 10;

        resetWithNewPool(() => new TestPool(time_per_action * 50, pool_size));

        AddRepeatStep("Get new pooled drawables", () => consumeDrawable(), 50);

        AddUntilStep("All returned to pool", () => pool.CountAvailable == pool.CurrentPoolSize, 15000);

        AddAssert("Pool grew in size", () => pool.CountAvailable > pool_size);
        AddAssert("Consumed drawables report returned to pool", () => consumed.All(d => !d.IsInUse));
    }

    [TestCase(10)]
    [TestCase(20)]
    public void TestPoolInitialSize(int initialPoolSize)
    {
        resetWithNewPool(() => new TestPool(time_per_action * 20, initialPoolSize));

        AddUntilStep("Available count is correct", () => pool.CountAvailable == initialPoolSize);
    }

    [Test]
    public void TestReturnWithoutAdding()
    {
        resetWithNewPool(() => new TestPool(time_per_action, 1));

        TestDrawable drawable = null!;

        AddStep("Consume without adding", () => drawable = pool.Get());

        AddStep("Manually return", () => drawable.Return());

        AddUntilStep("Was returned", () => pool.CountAvailable == 1);

        AddAssert("Manually return twice throws", () =>
        {
            try
            {
                drawable.Return();
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        });
    }

    [Test]
    public void TestPoolReturnWhenAboveCapacity()
    {
        resetWithNewPool(() => new TestPool(time_per_action * 20, 1, 1));

        TestDrawable first = null!, second = null!;

        AddStep("Consume item", () => first = consumeDrawable());

        AddAssert("Pool is empty", () => pool.CountAvailable == 0);

        AddStep("Consume and return another item", () =>
        {
            second = pool.Get();
            second.Return();
        });

        AddAssert("First item still in use", () => first.IsInUse);

        AddUntilStep("Second is returned", () => !second.IsInUse && pool.CountAvailable == 1);

        AddStep("Expire first", () => first.Expire());

        AddUntilStep("Wait until first dead", () => !first.IsAlive);

        AddAssert("Excess constructed count is one", () => pool.CountExcessConstructed == 1);
    }

    [Test]
    public void TestAllDrawablesComeReady()
    {
        const int pool_size = 10;
        List<Drawable> retrieved = new List<Drawable>();

        resetWithNewPool(() => new TestPool(time_per_action * 20, 10, pool_size));

        AddStep("Clear retrieved list", () => retrieved.Clear());

        AddRepeatStep("Get many pooled drawables", () => retrieved.Add(pool.Get()), pool_size * 2);

        AddAssert("All drawables in ready state", () => retrieved.All(d => d.IsLoaded));
    }

    [Test]
    public void TestPoolUsageExceedsInitialNoMaximum()
    {
        const int requested_drawables = 20;
        const int initial_size = 10;

        resetWithNewPool(() => new TestPool(time_per_action * 20, initial_size));

        AddRepeatStep("Get many pooled drawables", () => consumeDrawable(), requested_drawables);

        AddAssert("Pool saturated", () => pool.CountAvailable == 0);

        AddUntilStep("Pool size returned to correct maximum", () => pool.CountAvailable == requested_drawables);

        AddUntilStep("Excess constructed zero", () => pool.CountExcessConstructed == 0);

        AddUntilStep("Count in pool is correct", () => consumed.Count(d => !d.IsInUse) == requested_drawables);
        AddAssert("All drawables in pool", () => consumed.All(d => !d.IsInUse));
    }

    [TestCase(10)]
    [TestCase(20)]
    public void TestPoolUsageExceedsMaximum(int maxPoolSize)
    {
        resetWithNewPool(() => new TestPool(time_per_action * maxPoolSize * 2, 10, maxPoolSize));

        AddRepeatStep("Get many pooled drawables", () => consumeDrawable(), maxPoolSize * 2);

        AddAssert("Pool saturated", () => pool.CountAvailable == 0);

        AddUntilStep("Pool size returned to correct maximum", () => pool.CountAvailable == maxPoolSize);
        AddUntilStep("Excess constructed count correct", () => pool.CountExcessConstructed == maxPoolSize);

        AddUntilStep("All consumed finished", () => consumed.All(d => !d.IsInUse));

        AddAssert("Excess drawables were used", () => consumed.Any(d => d.Parent == null && !d.IsInUse));
    }

    [Test]
    public void TestGetFromNotLoadedPool()
    {
        AddStep("Assert throws on not loaded pool", () =>
        {
            Assert.Throws<InvalidOperationException>(() => new TestPool(100, 1).Get());
        });
    }

    public override void Update()
    {
        base.Update();

        if (count != null && pool != null)
        {
            count.Text = $"available: {pool.CountAvailable} poolSize: {pool.CurrentPoolSize} inUse: {pool.CountInUse} consumed: {consumed.Count} excessConstructed: {pool.CountExcessConstructed}";
        }
    }

    private static int displayCount;
    private Random rng = new Random();

    private TestDrawable consumeDrawable(bool addToHierarchy = true)
    {
        var drawable = pool.Get(d =>
        {
            d.Position = new Vector2((float)rng.NextDouble() * DrawSize.X, (float)rng.NextDouble() * DrawSize.Y);
            d.DisplayString = (++displayCount).ToString();
        });

        consumed.Add(drawable);

        if (addToHierarchy)
            Add(drawable);

        return drawable;
    }

    private void resetWithNewPool(Func<DrawablePool<TestDrawable>> createPool)
    {
        AddStep("reset stats", () => consumed.Clear());

        AddStep("create pool", () =>
        {
            pool?.Dispose();
            Clear();
            pool = createPool();

            Add(pool);
            Add(count = new SpriteText
            {
                Text = "-",
                Font = FontUsage.Default.With(size: 20),
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Margin = new MarginPadding { Top = 10, Left = 10 },
                Depth = -1
            });
        });
    }

    private partial class TestPool : DrawablePool<TestDrawable>
    {
        private readonly double fadeTime;

        public TestPool(double fadeTime, int initialSize, int? maximumSize = null)
            : base(initialSize, maximumSize)
        {
            this.fadeTime = fadeTime;
        }

        protected override TestDrawable CreateNewDrawable()
        {
            return new TestDrawable(fadeTime);
        }
    }

    private partial class TestDrawable : PoolableDrawable
    {
        private readonly double fadeTime;
        private readonly SpriteText text;

        private bool isPrepared;

        public string DisplayString
        {
            set => text.Text = value;
        }

        public TestDrawable() : this(1000) { }

        public TestDrawable(double fadeTime)
        {
            this.fadeTime = fadeTime;

            RelativePositionAxes = Axes.Both;
            Size = new Vector2(50);
            Origin = Anchor.Centre;

            text = new SpriteText
            {
                Text = "-",
                Font = FontUsage.Default.With(size: 25),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Color = Color.White
            };

            var box = new Box
            {
                Color = Color.LimeGreen,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1)
            };

            Add(box);
            Add(text);
        }

        protected override void OnParentChanged()
        {
            base.OnParentChanged();

            if (Parent == null)
            {
                isPrepared = false;
                ClearTransforms();
            }
        }

        public override void Update()
        {
            base.Update();

            if (IsInUse && !isPrepared)
            {
                isPrepared = true;

                Alpha = 0;
                Rotation = 0;

                this.FadeIn(fadeTime).RotateTo(80, fadeTime);
                this.AddDelayed(Expire, fadeTime);
            }
            else if (!IsInUse)
            {
                isPrepared = false;
            }
        }
    }
}
