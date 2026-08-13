// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;
using Sakura.Framework.Statistic;
using SakuraVertex = Sakura.Framework.Graphics.Rendering.Vertex.Vertex;

namespace Sakura.Framework.Graphics.Rendering.Batches;

/// <summary>
/// Backend-agnostic CPU-side accumulation of a draw batch: the vertex array, the optional index
/// array, and the per-vertex texture-slot / clip stamping every backend needs. Knows nothing about
/// any graphics API — the owning renderer supplies a flush callback that uploads and draws whatever
/// has accumulated, and this type calls it when the batch runs out of room.
/// </summary>
/// <remarks>
/// Two shapes, chosen at construction:
/// <list type="bullet">
/// <item><description><b>Indexed</b> (OpenGL, Direct3D11): a quad contributes 4 vertices + 6
/// indices, a 33% vertex-bandwidth saving over raw triangle pairs. Triangle lists contribute
/// sequential indices and set <see cref="HasNonQuad"/>, which tells the backend it cannot use a
/// static quad index buffer for this flush.</description></item>
/// <item><description><b>Non-indexed</b> (Metal): a quad is expanded to 6 vertices on the way in,
/// and no index array exists at all, because the native bridge draws triangle lists.</description></item>
/// </list>
/// </remarks>
public sealed class VertexBatch
{
    private static readonly GlobalStatistic<int> stat_buffer_full_flushes = GlobalStatistics.Get<int>("Renderer", "Buffer Full Flushes");

    /// <summary>
    /// Clip rect meaning "no active clip" to the fragment shader's <c>applyClipping</c>.
    /// </summary>
    private static readonly Vector4 no_clip = new Vector4(0, 0, -1, -1);

    private readonly SakuraVertex[] vertices;
    private readonly uint[] indices;

    /// <summary>
    /// Uploads and draws whatever has accumulated, then <see cref="Reset"/>s this batch. Invoked when
    /// an add would overflow the arrays; the owning renderer also calls its own flush directly for
    /// state changes (blend mode, render target, texture-slot exhaustion, …).
    /// </summary>
    private readonly Action flush;

    private int vertexCount;
    private int indexCount;

    /// <summary>
    /// Whether this batch is indexed. Non-indexed batches expand quads to triangles on the way in and
    /// leave <see cref="IndexCount"/> at zero.
    /// </summary>
    public bool Indexed { get; }

    /// <summary>
    /// Capacity of the vertex array. A single add never exceeds it (quads are at most 6 vertices,
    /// triangle lists are added a triangle at a time).
    /// </summary>
    public int MaxVertices { get; }

    /// <summary>
    /// Capacity of the index array; zero when <see cref="Indexed"/> is false.
    /// </summary>
    public int MaxIndices { get; }

    public int VertexCount => vertexCount;
    public int IndexCount => indexCount;

    public bool IsEmpty => vertexCount == 0;

    /// <summary>
    /// Whether anything in this batch broke quad alignment (i.e. came in through
    /// <see cref="AddRange"/>). An indexed backend must upload <see cref="Indices"/> for such a flush
    /// instead of relying on a static quad index buffer.
    /// </summary>
    public bool HasNonQuad { get; private set; }

    public ReadOnlySpan<SakuraVertex> Vertices => vertices.AsSpan(0, vertexCount);

    public ReadOnlySpan<uint> Indices => Indexed ? indices.AsSpan(0, indexCount) : ReadOnlySpan<uint>.Empty;

    /// <param name="maxVertices">Vertex capacity before an add forces a flush.</param>
    /// <param name="flush">Uploads + draws the accumulated batch and calls <see cref="Reset"/>.</param>
    /// <param name="indexed">See the shapes described on the type.</param>
    public VertexBatch(int maxVertices, Action flush, bool indexed = true)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxVertices, 6);
        ArgumentNullException.ThrowIfNull(flush);

        MaxVertices = maxVertices;
        Indexed = indexed;
        this.flush = flush;

        // Quads use 6 indices per 4 vertices (1.5×); triangle lists use 1 index per vertex.
        MaxIndices = indexed ? maxVertices * 3 / 2 : 0;

        vertices = new SakuraVertex[maxVertices];
        indices = indexed ? new uint[MaxIndices] : Array.Empty<uint>();
    }

    /// <summary>
    /// Flushes if the requested space would not fit, so the caller can add unconditionally afterwards.
    /// </summary>
    private void ensureCapacity(int vertexSpace, int indexSpace)
    {
        if (vertexCount + vertexSpace > MaxVertices || indexCount + indexSpace > MaxIndices)
        {
            stat_buffer_full_flushes.Value++;
            flush();
        }
    }

    /// <summary>
    /// Adds a quad of exactly 4 vertices ordered top-left, top-right, bottom-right, bottom-left.
    /// </summary>
    public void AddQuad(ReadOnlySpan<SakuraVertex> quad, float textureIndex = 0f, Vector4? clipData = null, float clipShearX = 0f, float clipRadius = 0f)
    {
        Vector4 actualClipData = clipData ?? no_clip;

        if (!Indexed)
        {
            // No index buffer to fan the four corners out with, so write the two triangles directly,
            // in the same winding the indexed path uses (TL, TR, BR / BR, BL, TL).
            ensureCapacity(6, 0);

            add(quad[0], textureIndex, actualClipData, clipShearX, clipRadius);
            add(quad[1], textureIndex, actualClipData, clipShearX, clipRadius);
            add(quad[2], textureIndex, actualClipData, clipShearX, clipRadius);
            add(quad[2], textureIndex, actualClipData, clipShearX, clipRadius);
            add(quad[3], textureIndex, actualClipData, clipShearX, clipRadius);
            add(quad[0], textureIndex, actualClipData, clipShearX, clipRadius);
            return;
        }

        ensureCapacity(4, 6);

        uint baseIndex = (uint)vertexCount;

        for (int i = 0; i < 4; i++)
            add(quad[i], textureIndex, actualClipData, clipShearX, clipRadius);

        indices[indexCount++] = baseIndex;
        indices[indexCount++] = baseIndex + 1;
        indices[indexCount++] = baseIndex + 2;
        indices[indexCount++] = baseIndex + 2;
        indices[indexCount++] = baseIndex + 3;
        indices[indexCount++] = baseIndex;
    }

    /// <summary>
    /// Adds an arbitrary triangle list (sequentially indexed when <see cref="Indexed"/>).
    /// </summary>
    public void AddRange(ReadOnlySpan<SakuraVertex> newVertices, float textureIndex = 0f, Vector4? clipData = null, float clipShearX = 0f, float clipRadius = 0f)
    {
        // Triangle-list vertices break the quad alignment, so this flush must use the dynamic index
        // buffer rather than a static quad pattern.
        HasNonQuad = true;

        Vector4 actualClipData = clipData ?? no_clip;

        // Added a triangle at a time so a capacity flush can never split one across two draws (which
        // would leave a draw with a vertex count that isn't a multiple of three, silently dropping the
        // remainder).
        for (int i = 0; i < newVertices.Length; i += 3)
        {
            int group = Math.Min(3, newVertices.Length - i);
            ensureCapacity(group, Indexed ? group : 0);

            for (int j = 0; j < group; j++)
            {
                if (Indexed)
                    indices[indexCount++] = (uint)vertexCount;

                add(newVertices[i + j], textureIndex, actualClipData, clipShearX, clipRadius);
            }
        }
    }

    private void add(in SakuraVertex vertex, float textureIndex, Vector4 clipData, float clipShearX, float clipRadius)
    {
        ref var v = ref vertices[vertexCount++];

        v = vertex;
        v.TexIndex = textureIndex;
        v.ClipData = clipData;
        v.ClipShearX = clipShearX;
        v.ClipRadius = clipRadius;
    }

    /// <summary>
    /// Drops everything accumulated so far. Called by the owning renderer's flush once the batch has
    /// been handed to the GPU.
    /// </summary>
    public void Reset()
    {
        vertexCount = 0;
        indexCount = 0;
        HasNonQuad = false;
    }
}
