// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using System.Threading;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Statistic;

namespace Sakura.Framework.Graphics.Pooling;

public class DrawablePool<T> : Container, IDrawablePool, IDisposable where T : PoolableDrawable, new()
{
    private GlobalStatistic<DrawablePoolUsageStatistic>? statistic;

    private readonly int initialSize;
    private readonly int? maximumSize;
    private readonly Stack<T> pool = new Stack<T>();

    private static int poolInstanceID;

    private int currentPoolSize;
    private int countInUse;
    private int countExcessConstructed;

    public DrawablePool(int initialSize, int? maximumSize = null)
    {
        DisposeOnRemoval = true;

        if (initialSize > maximumSize)
            throw new ArgumentOutOfRangeException(nameof(initialSize), "Initial size must be less than or equal to maximum size.");

        this.maximumSize = maximumSize;
        this.initialSize = initialSize;

        int id = Interlocked.Increment(ref poolInstanceID);
        statistic = GlobalStatistics.Get<DrawablePoolUsageStatistic>(nameof(DrawablePool<T>), $"{typeof(T).Name}`{id}");
        statistic.Value = new DrawablePoolUsageStatistic();
    }

    public override void Load()
    {
        base.Load();

        for (int i = 0; i < initialSize; i++)
        {
            var d = create();
            AddInternal(d);
            RemoveInternal(d);
            pool.Push(d);
        }

        CurrentPoolSize = initialSize;
    }

    public void Return(PoolableDrawable pooledDrawable)
    {
        if (pooledDrawable is not T typedDrawable)
            throw new ArgumentException("Invalid type", nameof(pooledDrawable));

        if (pooledDrawable.IsInUse)
        {
            // Called directly on the pool rather than via drawable.Return().
            // Only legal if the drawable has already been removed from its parent.
            if (pooledDrawable.Parent != null)
                throw new InvalidOperationException("Drawable was attempted to be returned to pool while still in a hierarchy. Remove it from its container first.");

            pooledDrawable.Return();
            return;
        }

        // IsInUse is already false here (set by PoolableDrawable.Return() before calling us).
        // Do NOT check Parent here — OnParentChanged may fire before the framework nulls Parent,
        // so Parent can legitimately be non-null at this point.

        if (typedDrawable.IsExcess || CountAvailable >= maximumSize)
        {
            pooledDrawable.SetPool(null);
            if (pooledDrawable.DisposeOnRemoval && pooledDrawable is IDisposable disposable)
                disposable.Dispose();
        }
        else
        {
            pool.Push(typedDrawable);
        }

        CountInUse--;
    }

    PoolableDrawable IDrawablePool.Get(Action<PoolableDrawable>? setupAction) => Get(setupAction);

    public T Get(Action<T>? setupAction = null)
    {
        if (LoadState <= LoadState.Loading)
            throw new InvalidOperationException($"A {nameof(DrawablePool<T>)} must be in a loaded state before retrieving pooled drawables.");

        if (!pool.TryPop(out var drawable))
        {
            drawable = create();

            if (maximumSize == null || currentPoolSize < maximumSize)
            {
                CurrentPoolSize++;
            }
            else
            {
                // This drawable was constructed beyond the pool's max capacity.
                // Mark it so it is discarded on return rather than kept in the pool.
                drawable.MarkAsExcess();
                CountExcessConstructed++;
            }

            if (IsLoaded)
            {
                AddInternal(drawable);
                RemoveInternal(drawable);
            }
        }

        CountInUse++;
        drawable.Assign();

        drawable.LifetimeStart = double.MinValue;
        drawable.LifetimeEnd = double.MaxValue;

        setupAction?.Invoke(drawable);

        return drawable;
    }

    protected virtual T CreateNewDrawable() => new T();

    private T create()
    {
        var drawable = CreateNewDrawable();
        drawable.SetPool(this);
        return drawable;
    }

    public void Dispose()
    {
        foreach (var p in pool)
        {
            if (p is IDisposable disposable)
                disposable.Dispose();
        }

        pool.Clear();
        CountInUse = 0;
        CountExcessConstructed = 0;
        CurrentPoolSize = 0;

        if (statistic != null)
        {
            GlobalStatistics.Remove(statistic);
            statistic = null;
        }
    }

    public int CurrentPoolSize
    {
        get => currentPoolSize;
        private set
        {
            currentPoolSize = value;
            if (statistic != null) statistic.Value.CurrentPoolSize = value;
        }
    }

    public int CountInUse
    {
        get => countInUse;
        private set
        {
            countInUse = value;
            if (statistic != null) statistic.Value.CountInUse = value;
        }
    }

    public int CountExcessConstructed
    {
        get => countExcessConstructed;
        private set
        {
            countExcessConstructed = value;
            if (statistic != null) statistic.Value.CountExcessConstructed = value;
        }
    }

    public int CountAvailable => pool.Count;

    public class DrawablePoolUsageStatistic
    {
        public int CurrentPoolSize;
        public int CountInUse;
        public int CountExcessConstructed;

        public override string ToString() => $"{CountInUse}/{CurrentPoolSize} ({CountExcessConstructed} excess)";
    }
}
