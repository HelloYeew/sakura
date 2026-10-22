// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering;

/// <summary>
/// One clip region as the fragment shader's <c>applyClipping</c> reads it from each vertex: the
/// rounded parallelogram's center and un-sheared half size, its horizontal shear and corner radius, and
/// the soft fade (<c>clipFade</c>) at its edges.
/// </summary>
internal readonly struct ClipState
{
    public readonly Vector4 ClipData;
    public readonly float ShearX;
    public readonly float Radius;

    /// <summary>
    /// The edges the fade is measured from (Left, Top, Right, Bottom), in screen space.
    /// </summary>
    public readonly Vector4 FadeEdges;

    /// <summary>
    /// How far in from each of <see cref="FadeEdges"/> the content fades in (Left, Top, Right, Bottom).
    /// 0 on a side is no fade there.
    /// </summary>
    public readonly Vector4 FadeLength;

    public ClipState(Vector4 clipData, float shearX, float radius)
        : this(clipData, shearX, radius, default, default)
    {
    }

    public ClipState(Vector4 clipData, float shearX, float radius, Vector4 fadeEdges, Vector4 fadeLength)
    {
        ClipData = clipData;
        ShearX = shearX;
        Radius = radius;
        FadeEdges = fadeEdges;
        FadeLength = fadeLength;
    }

    // (0,0,-1,-1) means "no active clip" to the fragment shader's applyClipping.
    public static ClipState None => new ClipState(new Vector4(0, 0, -1, -1), 0, 0);

    /// <summary>
    /// Writes this clip onto <paramref name="vertex"/>.
    /// </summary>
    public void ApplyTo(ref SakuraVertex vertex)
    {
        vertex.ClipData = ClipData;
        vertex.ClipShearX = ShearX;
        vertex.ClipRadius = Radius;
        vertex.ClipFadeEdges = FadeEdges;
        vertex.ClipFadeLength = FadeLength;
    }
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
    /// <param name="fade">How far in from each of the mask's edges its content fades in, in screen
    /// space. See <see cref="Fade"/>.</param>
    public void Push(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, MarginPadding fade = default)
    {
        stack.Push(Current);

        // True AABB of this new mask, taking horizontal shear into account.
        float skewOffset = Math.Abs(shearX * maskHalfSize.Y);
        float left = maskCenter.X - maskHalfSize.X - skewOffset;
        float right = maskCenter.X + maskHalfSize.X + skewOffset;
        float top = maskCenter.Y - maskHalfSize.Y;
        float bottom = maskCenter.Y + maskHalfSize.Y;

        var parent = Current;
        var (fadeEdges, fadeLength) = Fade(parent, left, top, right, bottom, fade);

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

        Current = new ClipState(new Vector4(newCenter.X, newCenter.Y, newHalfSize.X, newHalfSize.Y), shearX, cornerRadius, fadeEdges, fadeLength);
    }

    /// <summary>
    /// The fade for the content of a mask with the bounding box (<paramref name="left"/>,
    /// <paramref name="top"/>, <paramref name="right"/>, <paramref name="bottom"/>) and its own
    /// <paramref name="fade"/>, pushed inside <paramref name="parent"/>: the mask's own on each side it
    /// fades, measured from its bounding box (a sheared mask's slanted sides aren't followed), and the
    /// enclosing mask's on the rest, so content inside a nested mask (a rounded cover in a faded list)
    /// still fades.
    /// </summary>
    internal static (Vector4 Edges, Vector4 Length) Fade(ClipState parent, float left, float top, float right, float bottom, MarginPadding fade)
    {
        var edges = parent.FadeEdges;
        var length = parent.FadeLength;

        if (fade.Left > 0)
        {
            edges.X = left;
            length.X = fade.Left;
        }

        if (fade.Top > 0)
        {
            edges.Y = top;
            length.Y = fade.Top;
        }

        if (fade.Right > 0)
        {
            edges.Z = right;
            length.Z = fade.Right;
        }

        if (fade.Bottom > 0)
        {
            edges.W = bottom;
            length.W = fade.Bottom;
        }

        return (edges, length);
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
            clip.ApplyTo(ref vertices[i]);
    }
}
