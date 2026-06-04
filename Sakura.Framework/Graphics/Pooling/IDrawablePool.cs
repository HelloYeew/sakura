// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;

namespace Sakura.Framework.Graphics.Pooling;

public interface IDrawablePool
{
    /// <summary>
    /// Get a drawable from this pool.
    /// </summary>
    /// <param name="setupAction">An optional action to be performed on this drawable immediately after retrieval.</param>
    /// <returns>The drawable.</returns>
    PoolableDrawable Get(Action<PoolableDrawable>? setupAction = null);

    /// <summary>
    /// Return a drawable after use.
    /// </summary>
    /// <param name="pooledDrawable">The drawable to return. Should have originally come from this pool.</param>
    void Return(PoolableDrawable pooledDrawable);
}
