// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Linq;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Lists;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Containers;

public enum GridSizeMode
{
    Distributed,
    Relative,
    Absolute,
    AutoSize
}

public class Dimension
{
    public readonly GridSizeMode Mode;
    public readonly float Size;
    public readonly float MinSize;
    public readonly float MaxSize;

    public Dimension(GridSizeMode mode = GridSizeMode.Distributed, float size = 0, float minSize = 0, float maxSize = float.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minSize, maxSize);

        Mode = mode;
        Size = size;
        MinSize = minSize;
        MaxSize = maxSize;
    }

    internal float Range => MaxSize - MinSize;
}

/// <summary>
/// A wrapper for the content of a <see cref="GridContainer"/> that provides notifications when elements are changed.
/// </summary>
public class GridContainerContent : ObservableArray<ObservableArray<Drawable?>>
{
    public event Action? ContentChanged;

    private GridContainerContent(Drawable?[][] drawables)
        : base(new ObservableArray<Drawable?>[drawables.Length])
    {
        for (int i = 0; i < drawables.Length; i++)
        {
            if (drawables[i] != null)
            {
                var observableArray = new ObservableArray<Drawable?>(drawables[i]);

                // Bubble up inner array modifications to the top
                observableArray.ArrayElementChanged += triggerChange;
                this[i] = observableArray;
            }
        }

        // Bubble up outer array modifications (e.g., replacing an entire row)
        ArrayElementChanged += triggerChange;
    }

    private void triggerChange() => ContentChanged?.Invoke();

    public static implicit operator GridContainerContent(Drawable?[][] drawables)
    {
        if (drawables == null)
            return null!;

        return new GridContainerContent(drawables);
    }
}

/// <summary>
/// A container which allows laying out <see cref="Drawable"/>s in a grid.
/// </summary>
public partial class GridContainer : Container
{
    private GridContainerContent? content;
    private bool contentReloadRequired = true;
    private bool cellLayoutRequired = true;
    private Vector2 lastDrawSize;
    private CellContainer[,] cells = new CellContainer[0, 0];

    private int cellRows => cells.GetLength(0);
    private int cellColumns => cells.GetLength(1);

    public GridContainerContent? Content
    {
        get => content;
        set
        {
            if (content == value)
                return;

            if (content != null)
                content.ContentChanged -= onContentChange;

            content = value;

            onContentChange();

            if (content != null)
                content.ContentChanged += onContentChange;
        }
    }

    private Dimension[] rowDimensions = Array.Empty<Dimension>();

    public Dimension[] RowDimensions
    {
        get => rowDimensions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (rowDimensions == value) return;

            rowDimensions = value;
            InvalidateCellLayout();
        }
    }

    private Dimension[] columnDimensions = Array.Empty<Dimension>();

    public Dimension[] ColumnDimensions
    {
        get => columnDimensions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (columnDimensions == value) return;

            columnDimensions = value;
            InvalidateCellLayout();
        }
    }

    private void onContentChange()
    {
        contentReloadRequired = true;
        Invalidate(InvalidationFlags.DrawInfo);
    }

    internal void InvalidateCellLayout()
    {
        cellLayoutRequired = true;
        Invalidate(InvalidationFlags.DrawInfo);
    }

    public override void Update()
    {
        if (contentReloadRequired)
            layoutContent();

        if (DrawSize != lastDrawSize)
        {
            cellLayoutRequired = true;
            lastDrawSize = DrawSize;
        }

        if (cellLayoutRequired)
            layoutCells();

        base.Update();
    }

    private void layoutContent()
    {
        contentReloadRequired = false;
        cellLayoutRequired = true;

        int requiredRows = Content?.Count ?? 0;
        int requiredColumns = requiredRows == 0 ? 0 : Content?.Max(c => c?.Count ?? 0) ?? 0;

        foreach (var cell in cells)
            cell?.Clear();

        ClearInternal();

        cells = new CellContainer[requiredRows, requiredColumns];

        for (int r = 0; r < cellRows; r++)
        {
            for (int c = 0; c < cellColumns; c++)
            {
                if (Content == null) continue;

                cells[r, c] = new CellContainer { GridParent = this };

                if (Content[r] == null || c >= Content[r]!.Count || Content[r]![c] == null)
                {
                    AddInternal(cells[r, c]);
                    continue;
                }

                var child = Content[r]![c]!;
                cells[r, c].Add(child);
                cells[r, c].Depth = child.Depth;

                AddInternal(cells[r, c]);
            }
        }
    }

    private void layoutCells()
    {
        cellLayoutRequired = false;

        float availableWidth = Math.Max(0, DrawSize.X - Padding.Total.X);
        float availableHeight = Math.Max(0, DrawSize.Y - Padding.Total.Y);

        float[] widths = distribute(columnDimensions, availableWidth, getCellSizesAlongAxis(Axes.X, availableWidth));
        float[] heights = distribute(rowDimensions, availableHeight, getCellSizesAlongAxis(Axes.Y, availableHeight));

        for (int col = 0; col < cellColumns; col++)
        {
            for (int row = 0; row < cellRows; row++)
            {
                var cell = cells[row, col];
                if (cell == null) continue;

                cell.Size = new Vector2(widths[col], heights[row]);

                float xPos = 0;
                for (int i = 0; i < col; i++) xPos += widths[i];

                float yPos = 0;
                for (int i = 0; i < row; i++) yPos += heights[i];

                cell.Position = new Vector2(xPos, yPos);
            }
        }
    }

    private float[] getCellSizesAlongAxis(Axes axis, float spanLength)
    {
        var spanDimensions = axis == Axes.X ? columnDimensions : rowDimensions;
        int spanCount = axis == Axes.X ? cellColumns : cellRows;

        float[] sizes = new float[spanCount];

        for (int i = 0; i < spanCount; i++)
        {
            var dimension = i < spanDimensions.Length ? spanDimensions[i] : new Dimension();

            switch (dimension.Mode)
            {
                case GridSizeMode.Distributed:
                    break;

                case GridSizeMode.Relative:
                    sizes[i] = dimension.Size * spanLength;
                    break;

                case GridSizeMode.Absolute:
                    sizes[i] = dimension.Size;
                    break;

                case GridSizeMode.AutoSize:
                    float size = 0;

                    if (axis == Axes.X)
                    {
                        for (int r = 0; r < cellRows; r++)
                        {
                            var cell = Content?[r]?[i];
                            if (cell == null || !cell.IsAlive || (cell.RelativeSizeAxes & Axes.X) != 0)
                                continue;

                            size = Math.Max(size, getCellWidth(cell));
                        }
                    }
                    else
                    {
                        for (int c = 0; c < cellColumns; c++)
                        {
                            var cell = Content?[i]?[c];
                            if (cell == null || !cell.IsAlive || (cell.RelativeSizeAxes & Axes.Y) != 0)
                                continue;

                            size = Math.Max(size, getCellHeight(cell));
                        }
                    }

                    sizes[i] = size;
                    break;
            }

            sizes[i] = Math.Clamp(sizes[i], dimension.MinSize, dimension.MaxSize);
        }

        return sizes;
    }

    private static float getCellWidth(Drawable cell) => (cell.Size.X * cell.Scale.X) + cell.Margin.Total.X;
    private static float getCellHeight(Drawable cell) => (cell.Size.Y * cell.Scale.Y) + cell.Margin.Total.Y;

    private float[] distribute(Dimension[] dimensions, float spanLength, float[] cellSizes)
    {
        int[] distributedIndices = Enumerable.Range(0, cellSizes.Length)
            .Where(i => i >= dimensions.Length || dimensions[i].Mode == GridSizeMode.Distributed)
            .ToArray();

        var distributedDimensions = distributedIndices
            .Select(i => new { Index = i, Dimension = i >= dimensions.Length ? new Dimension() : dimensions[i] })
            .ToList();

        int distributionCount = distributedIndices.Length;

        if (distributionCount == 0)
            return cellSizes;

        float requiredSize = cellSizes.Sum();
        float distributionSize = Math.Max(0, spanLength - requiredSize) / distributionCount;

        foreach (var entry in distributedDimensions.OrderBy(d => d.Dimension.Range))
        {
            cellSizes[entry.Index] = Math.Min(entry.Dimension.MaxSize, entry.Dimension.MinSize + distributionSize);

            if (--distributionCount > 0)
                distributionSize += Math.Max(0, distributionSize - entry.Dimension.Range) / distributionCount;
        }

        return cellSizes;
    }

    private partial class CellContainer : Container
    {
        public GridContainer? GridParent { get; set; }

        public override void Invalidate(InvalidationFlags flags = InvalidationFlags.All, bool propagateToParent = true)
        {
            base.Invalidate(flags, propagateToParent);

            if ((flags & InvalidationFlags.DrawInfo) != 0)
            {
                GridParent?.InvalidateCellLayout();
            }
        }
    }
}
