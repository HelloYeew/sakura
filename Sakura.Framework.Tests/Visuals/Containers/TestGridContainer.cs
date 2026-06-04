// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

public class TestGridContainer : TestScene
{
    private GridContainer grid = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Clear and create Grid", () =>
        {
            Clear();
            Add(grid = new GridContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.8f),
                Masking = true,
                BorderThickness = 2,
                BorderColor = Color.White
            });
        });
    }

    [Test]
    public void TestDistributedSizing()
    {
        AddStep("Set distributed content (2x2)", () =>
        {
            grid.RowDimensions = new[]
            {
                new Dimension(GridSizeMode.Distributed),
                new Dimension(GridSizeMode.Distributed)
            };
            grid.ColumnDimensions = new[]
            {
                new Dimension(GridSizeMode.Distributed),
                new Dimension(GridSizeMode.Distributed)
            };

            grid.Content = new Drawable?[][]
            {
                new Drawable?[]
                {
                    createCell("0, 0", Color.Red),
                    createCell("0, 1", Color.Blue)
                },
                new Drawable?[]
                {
                    createCell("1, 0", Color.Green),
                    createCell("1, 1", Color.Yellow)
                }
            };
        });
    }

    [Test]
    public void TestMixedSizing()
    {
        AddStep("Set mixed content (3x3)", () =>
        {
            grid.RowDimensions = new[]
            {
                new Dimension(GridSizeMode.Absolute, 100),
                new Dimension(GridSizeMode.Relative, 0.4f),
                new Dimension(GridSizeMode.Distributed)
            };
            grid.ColumnDimensions = new[]
            {
                new Dimension(GridSizeMode.Absolute, 150),
                new Dimension(GridSizeMode.Relative, 0.3f),
                new Dimension(GridSizeMode.Distributed)
            };

            grid.Content = new Drawable?[][]
            {
                new Drawable?[]
                {
                    createCell("Abs 150 Abs 100", Color.Red),
                    createCell("Rel 0.3 Abs 100", Color.Blue),
                    createCell("Dist Abs 100", Color.Green)
                },
                new Drawable?[]
                {
                    createCell("Abs 150 Rel 0.4", Color.Orange),
                    createCell("Rel 0.3 Rel 0.4", Color.Purple),
                    createCell("Dist Rel 0.4", Color.Cyan)
                },
                new Drawable?[]
                {
                    createCell("Abs 150 Dist", Color.Pink),
                    createCell("Rel 0.3 Dist", Color.Brown),
                    createCell("Dist Dist", Color.Gray)
                }
            };
        });
    }

    [Test]
    public void TestAutoSize()
    {
        AddStep("Set AutoSize row/col", () =>
        {
            grid.RowDimensions = new[]
            {
                new Dimension(GridSizeMode.AutoSize),
                new Dimension(GridSizeMode.Distributed)
            };
            grid.ColumnDimensions = new[]
            {
                new Dimension(GridSizeMode.AutoSize),
                new Dimension(GridSizeMode.Distributed)
            };

            grid.Content = new Drawable?[][]
            {
                new Drawable?[]
                {
                    new Container
                    {
                        Size = new Vector2(250, 150), // explicit size to trigger AutoSize
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Color = Color.Teal
                            },
                            new SpriteText
                            {
                                Text = "Auto Size (250x150)",
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre
                            }
                        }
                    },
                    createCell("Dist Width", Color.Olive)
                },
                new Drawable?[]
                {
                    createCell("Dist Height", Color.Maroon),
                    createCell("Dist Both", Color.Navy)
                }
            };
        });
    }

    [Test]
    public void TestDynamicContentUpdate()
    {
        TestDistributedSizing();

        AddStep("Update single cell [0][0]", () =>
        {
            if (grid.Content != null)
            {
                // this will trigger the ObservableArray event and invalidate layout automatically
                grid.Content[0][0] = createCell("updated cell!", Color.Magenta);
            }
        });

        AddStep("Nullify cell [1][1]", () =>
        {
            if (grid.Content != null)
            {
                grid.Content[1][1] = null;
            }
        });

        AddStep("Reassign completely new layout", () =>
        {
            grid.RowDimensions = new[]
            {
                new Dimension(GridSizeMode.Distributed)
            };
            grid.ColumnDimensions = new[]
            {
                new Dimension(GridSizeMode.Distributed),
                new Dimension(GridSizeMode.Distributed)
            };

            grid.Content = new Drawable?[][]
            {
                new Drawable?[]
                {
                    createCell("New Layout 0,0", Color.DarkCyan),
                    createCell("New Layout 0,1", Color.DarkGoldenrod)
                }
            };
        });
    }

    /// <summary>
    /// Helper method to visually distinguish cells.
    /// </summary>
    private static Drawable createCell(string text, Color color)
    {
        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Color = color
                },
                new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = text,
                    Font = FontUsage.Default.With(size: 20, weight: "Bold"),
                }
            }
        };
    }
}
