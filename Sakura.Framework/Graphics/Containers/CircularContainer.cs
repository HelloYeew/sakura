// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A container that's round via automate <see cref="Container.CornerRadius"/> calculation.
/// </summary>
public class CircularContainer : Container
{
    public CircularContainer()
    {
        Masking = true;
    }

    public override void Update()
    {
        base.Update();
        CornerRadius = Math.Min(DrawSize.X, DrawSize.Y) / 2f;
    }
}
