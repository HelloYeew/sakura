// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Graphics.Pooling;

/// <summary>
/// A drawable that is supported by <see cref="DrawablePool{T}"/>.
/// </summary>
public abstract partial class PoolableDrawable : Container
{
    private IDrawablePool? pool;

    protected PoolableDrawable()
    {
        // A pooled drawable is removed from its parent precisely in order to be reused, so removal must
        // never dispose it including when an ancestor is torn down, which removes it (returning it to
        // its pool) on the way down. The pool owns its lifetime and disposes it when the pool itself goes.
        DisposeOnRemoval = false;
    }

    /// <summary>
    /// Whether this pooled drawable is currently being used (is out of the pool).
    /// </summary>
    public bool IsInUse { get; private set; }

    /// <summary>
    /// Whether this drawable was constructed beyond the pool's maximum capacity.
    /// Excess drawables are discarded on return rather than returned to the pool.
    /// </summary>
    internal bool IsExcess { get; private set; }

    internal void SetPool(IDrawablePool? pool)
    {
        this.pool = pool;
    }

    internal void Assign()
    {
        if (IsInUse)
            throw new InvalidOperationException($"Cannot assign an already in-use {nameof(PoolableDrawable)}.");

        IsInUse = true;
    }

    internal void MarkAsExcess()
    {
        IsExcess = true;
    }

    /// <summary>
    /// Returns this drawable to its pool.
    /// </summary>
    public void Return()
    {
        if (!IsInUse)
            throw new InvalidOperationException($"Cannot return a {nameof(PoolableDrawable)} that is not in use.");

        IsInUse = false;
        pool?.Return(this);
    }

    protected override void OnParentChanged()
    {
        base.OnParentChanged();

        if (Parent == null && IsInUse)
        {
            Return();
        }
    }
}
