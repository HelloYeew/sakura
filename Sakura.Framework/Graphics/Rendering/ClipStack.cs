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
    /// A clip that lets nothing through: a mask of no size, off where nothing's drawn. (Without corners: a radius rounds
    /// even a shape of no size into a circle of that radius.)
    /// </summary>
    public static ClipState Nothing => new ClipState(new Vector4(-1000000, -1000000, 0.0001f, 0.0001f), 0, 0);

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
    /// Makes the given mask, inside the current clip, the new current clip. A vertex has room for one
    /// shape, so one of the two masks stays a shape and the other goes along as straight edges, in the
    /// fade (<see cref="cutFrom"/>): a mask cut by the one around it keeps its own box, slant and corners,
    /// rather than growing corners where it's cut. Whichever is kept, what's lost is only ever the corners
    /// or slant of the other, and the choice is the one that doesn't lose them where they'd show (see
    /// <see cref="keepsParentsShape"/>). Only where two shapes overlap at a corner does the outer one's
    /// corner go uncut.
    /// </summary>
    /// <param name="fade">How far in from each of the mask's edges its content fades in, in screen
    /// space. See <see cref="Fade"/>.</param>
    public void Push(Vector2 maskCenter, Vector2 maskHalfSize, float shearX, float cornerRadius, MarginPadding fade = default)
    {
        stack.Push(Current);

        // True AABB of this new mask, taking horizontal shear into account.
        float skew = Math.Abs(shearX * maskHalfSize.Y);
        float left = maskCenter.X - maskHalfSize.X - skew;
        float right = maskCenter.X + maskHalfSize.X + skew;
        float top = maskCenter.Y - maskHalfSize.Y;
        float bottom = maskCenter.Y + maskHalfSize.Y;

        var parent = Current;
        var (edges, length) = Fade(parent, left, top, right, bottom, fade);
        var ownData = new Vector4(maskCenter.X, maskCenter.Y, maskHalfSize.X, maskHalfSize.Y);

        // (Z > 0 is a mask to be inside.)
        if (parent.ClipData.Z <= 0)
        {
            Current = new ClipState(ownData, shearX, cornerRadius, edges, length);
            return;
        }

        float parentSkew = Math.Abs(parent.ShearX * parent.ClipData.W);
        float pLeft = parent.ClipData.X - parent.ClipData.Z - parentSkew;
        float pRight = parent.ClipData.X + parent.ClipData.Z + parentSkew;
        float pTop = parent.ClipData.Y - parent.ClipData.W;
        float pBottom = parent.ClipData.Y + parent.ClipData.W;

        if (left >= pRight || right <= pLeft || top >= pBottom || bottom <= pTop)
        {
            Current = ClipState.Nothing;
            return;
        }

        if (keepsParentsShape(parent, parentSkew, left, top, right, bottom, pLeft, pTop, pRight, pBottom, cornerRadius, skew, shearX))
        {
            if (left > pLeft)
                (edges.X, length.X) = cutFrom(edges.X, length.X, left);

            if (top > pTop)
                (edges.Y, length.Y) = cutFrom(edges.Y, length.Y, top);

            if (right < pRight)
                (edges.Z, length.Z) = cutBackFrom(edges.Z, length.Z, right);

            if (bottom < pBottom)
                (edges.W, length.W) = cutBackFrom(edges.W, length.W, bottom);

            Current = new ClipState(parent.ClipData, parent.ShearX, parent.Radius, edges, length);
            return;
        }

        if (left < pLeft)
            (edges.X, length.X) = cutFrom(edges.X, length.X, pLeft);

        if (top < pTop)
            (edges.Y, length.Y) = cutFrom(edges.Y, length.Y, pTop);

        if (right > pRight)
            (edges.Z, length.Z) = cutBackFrom(edges.Z, length.Z, pRight);

        if (bottom > pBottom)
            (edges.W, length.W) = cutBackFrom(edges.W, length.W, pBottom);

        Current = new ClipState(ownData, shearX, cornerRadius, edges, length);
    }

    /// <summary>
    /// Whether the mask around stays the shape, with this one's box as edges, rather than the other way about. That's when
    /// straight edges are all of this one that shows (none of its rounded corners do), and the shape around has corners or
    /// slanted sides that it reaches.
    /// </summary>
    private static bool keepsParentsShape(in ClipState parent, float parentSkew, float left, float top, float right, float bottom,
                                          float pLeft, float pTop, float pRight, float pBottom, float cornerRadius, float skew, float shearX)
    {
        float inLeft = pLeft - left, inTop = pTop - top, inRight = right - pRight, inBottom = bottom - pBottom;

        // A slant shows all the way along its sides: only well outside the box around does it not.
        if (shearX != 0)
            return Math.Min(Math.Min(inLeft, inTop), Math.Min(inRight, inBottom)) >= cornerRadius + 2 * skew;

        if (!(cornerIsHidden(inLeft, inTop, cornerRadius, parent.Radius)
              && cornerIsHidden(inRight, inTop, cornerRadius, parent.Radius)
              && cornerIsHidden(inLeft, inBottom, cornerRadius, parent.Radius)
              && cornerIsHidden(inRight, inBottom, cornerRadius, parent.Radius)))
            return false;

        // A box between the corners or sides of the shape around it isn't touched by them. A slanted side runs the whole
        // height, so only being between them across will do for one.
        float acrossReach = parent.Radius + 2 * parentSkew;
        bool clearAcross = left >= pLeft + acrossReach && right <= pRight - acrossReach;
        bool clearDown = top >= pTop + parent.Radius && bottom <= pBottom - parent.Radius;

        return !(clearAcross || (clearDown && parent.ShearX == 0));
    }

    /// <summary>
    /// Whether a mask's rounding at a corner can't be seen inside the mask around it: it has none, it's past that mask's side
    /// by its radius, or it's round that mask's corner, whose own rounding is what shows.
    /// </summary>
    /// <param name="inAcross">How far in from the mask's side that of the mask around it is, at this corner.</param>
    /// <param name="inDown">The same for its top or bottom.</param>
    /// <param name="radius">The mask's corner radius.</param>
    /// <param name="parentRadius">That of the mask around it.</param>
    private static bool cornerIsHidden(float inAcross, float inDown, float radius, float parentRadius) =>
        radius <= 0 || inAcross >= radius || inDown >= radius || (inAcross >= 0 && inDown >= 0 && radius <= parentRadius + Math.Min(inAcross, inDown));

    /// <summary>
    /// Adds a straight cut at <paramref name="at"/> to the fade on a left or top side. The fade is a line from where it's
    /// nothing to where it's whole; over a pixel centered on the cut, as its smoothstep runs, it's exactly the edge the
    /// shader gives a mask (<c>applyClipping</c>). With a fade there already, the line goes from the further in of the two
    /// zeroes to the further in of the two wholes, so it's never more than either.
    /// </summary>
    private static (float Edge, float Length) cutFrom(float edge, float length, float at)
    {
        float zero = at - 0.5f;
        float whole = at + 0.5f;

        if (length > 0)
        {
            zero = Math.Max(zero, edge);
            whole = Math.Max(whole, edge + length);
        }

        return (zero, whole - zero);
    }

    /// <summary>
    /// <see cref="cutFrom"/> for a right or bottom side, where in is back along the axis.
    /// </summary>
    private static (float Edge, float Length) cutBackFrom(float edge, float length, float at)
    {
        (float negated, float shared) = cutFrom(-edge, length, -at);
        return (-negated, shared);
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
