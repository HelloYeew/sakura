// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Rendering;

namespace Sakura.Framework.Graphics.Drawables;

/// <summary>
/// A circle drawable draw using shader-based rendering.
/// </summary>
public class Circle : Drawable
{
    public override void Draw(IRenderer renderer)
    {
        if (DrawAlpha <= 0)
            return;

        renderer.DrawCircle(this);
    }
}
