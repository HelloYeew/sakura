// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Maths;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// One clip region as the fragment shader's <c>applyClipping</c> reads it from each vertex: the
/// rounded parallelogram's center and un-sheared half size, its horizontal shear and corner radius.
/// </summary>
internal readonly struct ClipState
{
    public readonly Vector4 ClipData;
    public readonly float ShearX;
    public readonly float Radius;

    public ClipState(Vector4 clipData, float shearX, float radius)
    {
        ClipData = clipData;
        ShearX = shearX;
        Radius = radius;
    }

    // (0,0,-1,-1) means "no active clip" to the fragment shader's applyClipping.
    public static ClipState None => new ClipState(new Vector4(0, 0, -1, -1), 0, 0);
}

/// <summary>
/// The mask stack of a backend that clips in the fragment shader. Masking needs no stencil: the
/// active clip travels with every vertex and the shader evaluates its signed-distance field per
/// fragment, so a push only has to work out the clip region on the CPU.
/// </summary>
internal sealed class ClipStack
{
    private readonly Stack<ClipState> stack = new Stack<ClipState>();

    /// <summary>
    /// The clip stamped onto vertices drawn now. Settable so a framebuffer bind can save it, start the
    /// buffer's content from <see cref="ClipState.None"/> (the outer clip applies to the composited quad
    /// instead), and put it back on unbind.
    /// </summary>
    public ClipState Current { get; set; } = ClipState.None;

    public void Reset()
    {
        stack.Clear();
        Current = ClipState.None;
    }

    /// <summary>
    /// Makes the intersection of the given mask with the current clip the new current clip. The
    /// intersection is taken between axis-aligned bounding boxes, accounting for horizontal shear, and
    /// the new mask's own shear and corner radius are kept.
    /// </summary>
    public void Push(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius)
    {
        stack.Push(Current);

        // True AABB of this new mask, taking horizontal shear into account.
        float skewOffset = Math.Abs(shearX * maskHalfSize.Y);
        float left = maskCenter.X - maskHalfSize.X - skewOffset;
        float right = maskCenter.X + maskHalfSize.X + skewOffset;
        float top = maskCenter.Y - maskHalfSize.Y;
        float bottom = maskCenter.Y + maskHalfSize.Y;

        var parent = Current;

        // If already inside a parent mask (Z > 0), intersect their bounding boxes.
        if (parent.ClipData.Z > 0)
        {
            float parentSkew = Math.Abs(parent.ShearX * parent.ClipData.W);
            float pLeft = parent.ClipData.X - parent.ClipData.Z - parentSkew;
            float pRight = parent.ClipData.X + parent.ClipData.Z + parentSkew;
            float pTop = parent.ClipData.Y - parent.ClipData.W;
            float pBottom = parent.ClipData.Y + parent.ClipData.W;

            left = Math.Max(left, pLeft);
            right = Math.Min(right, pRight);
            top = Math.Max(top, pTop);
            bottom = Math.Min(bottom, pBottom);
        }

        var newCenter = new Vector2((left + right) / 2f, (top + bottom) / 2f);
        var newHalfSize = new Vector2((right - left) / 2f, (bottom - top) / 2f);

        // Collapsed intersection (child entirely outside parent) → shrink to ~zero so the shader
        // discards every fragment.
        if (left >= right || top >= bottom)
            newHalfSize = new Vector2(0.0001f, 0.0001f);
        else
            // Remove the skew offset again so the shader receives the true un-sheared half-size.
            newHalfSize.X = Math.Max(0.0001f, newHalfSize.X - skewOffset);

        Current = new ClipState(new Vector4(newCenter.X, newCenter.Y, newHalfSize.X, newHalfSize.Y), shearX, cornerRadius);
    }

    /// <summary>
    /// Restores the clip that was current before the matching <see cref="Push"/>. An unmatched pop
    /// leaves the current clip as it is.
    /// </summary>
    public void Pop()
    {
        if (stack.Count > 0)
            Current = stack.Pop();
    }

    /// <summary>
    /// Stamps the current clip onto <paramref name="vertices"/>.
    /// </summary>
    public void ApplyTo(Span<SakuraVertex> vertices)
    {
        var clip = Current;

        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i].ClipData = clip.ClipData;
            vertices[i].ClipShearX = clip.ShearX;
            vertices[i].ClipRadius = clip.Radius;
        }
    }
}
