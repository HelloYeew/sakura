// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public class TestPath : TestScene
{
    private Container pathContainer = null!;
    private SpriteText performanceText = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Initialize free-hand path canvas", () =>
        {
            Clear();

            pathContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Depth = 10
            };

            performanceText = new SpriteText
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-20, 20),
                Color = Color.White,
                Text = "Lines: 0 | GL Vertices: 0",
                Depth = 0
            };

            var canvas = new DrawingCanvas(pathContainer, performanceText)
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Depth = 2
            };

            Add(canvas);
            Add(pathContainer);
            Add(performanceText);
        });

        AddStep("Clear Canvas", () =>
        {
            pathContainer.Clear();
            performanceText.Text = "Lines: 0 | GL Vertices: 0";
        });
    }

    [Test]
    public void TestDrawLines()
    {
        AddStep("Clear canvas and draw lines", () =>
        {
            pathContainer.Clear();
        });
    }

    /// <summary>
    /// Invisible box that captures mouse drags and feeds them to dynamic Paths.
    /// </summary>
    private class DrawingCanvas : Box
    {
        private readonly Container containerRef;
        private readonly SpriteText statsRef;

        private Path? currentPath;
        private int totalLines;
        private int totalVertices;

        public DrawingCanvas(Container container, SpriteText stats)
        {
            containerRef = container;
            statsRef = stats;
            Color = Color.Transparent;
        }

        /// <summary>
        /// Transforms screen space (mouse) into the local space of this canvas
        /// </summary>
        /// <param name="screenSpacePos"></param>
        /// <returns></returns>
        private Vector2 getLocalPosition(Vector2 screenSpacePos)
        {
            // TODO: We have a lot of class like this? Should we create a global helper for this?
            if (Matrix4x4.Invert(ModelMatrix, out var inverse))
            {
                var localNormalized = Vector4.Transform(new Vector4(screenSpacePos.X, screenSpacePos.Y, 0, 1), inverse);
                return new Vector2(localNormalized.X * DrawSize.X, localNormalized.Y * DrawSize.Y);
            }
            return screenSpacePos;
        }

        public override bool OnMouseDown(MouseButtonEvent e)
        {
            bool handled = base.OnMouseDown(e);

            if (e.Button == MouseButton.Left)
            {
                // Start a new line
                currentPath = new Path
                {
                    Thickness = 8f,
                    Color = Color.LimeGreen,
                    JointStyle = PathJointStyle.Miter,
                    MiterLimit = 3f
                };

                containerRef.Add(currentPath);
                currentPath.AddVertex(getLocalPosition(e.ScreenSpaceMousePosition));

                totalLines++;
                updateStats();
                handled = true;
            }
            return handled;
        }

        public override bool OnDragStart(MouseButtonEvent e) => true;

        public override bool OnDrag(MouseEvent e)
        {
            if (currentPath == null) return false;

            var localPos = getLocalPosition(e.ScreenSpaceMousePosition);

            // If we only have 1 point, just add the next one if it's far enough
            if (currentPath.PathVertices.Count < 2)
            {
                if (Vector2.Distance(currentPath.PathVertices[0], localPos) > 4f)
                {
                    currentPath.AddVertex(localPos);
                    totalVertices += 6;
                    updateStats();
                }
                return true;
            }

            var p1 = currentPath.PathVertices[^2];
            var p2 = currentPath.PathVertices[^1];

            // Calculate directions
            var dir1 = Vector2.Normalize(p2 - p1);
            var dir2 = Vector2.Normalize(localPos - p2);

            // Dot product gives 1 if aligned, -1 if opposite
            float dot = Vector2.Dot(dir1, dir2);
            float distance = Vector2.Distance(p2, localPos);

            // 0.995f gives a slight tolerance for tiny mouse jitters.
            // The closer to 1.0f, the stricter the straight-line requirement.
            if (dot > 0.995f)
            {
                // Just update the last vertex to the new position to create a smooth line without adding new vertices.
                currentPath.UpdateLastVertex(localPos);
            }
            else if (distance > 4f)
            {
                // The angle changed significantly
                currentPath.AddVertex(localPos);
                totalVertices += 6;
                updateStats();
            }

            return true;
        }

        public override bool OnMouseUp(MouseButtonEvent e)
        {
            if (e.Button == MouseButton.Left)
            {
                currentPath = null; // End the current line
                return true;
            }
            return base.OnMouseUp(e);
        }

        private void updateStats()
        {
            statsRef.Text = $"Lines: {totalLines} | GL Vertices: {totalVertices}";
        }
    }
}
