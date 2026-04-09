// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Drawables;

public class TestBezierCurve : TestScene
{
    private BezierCurve curve;
    private Box p0Marker;
    private Box p1Marker;
    private Box p2Marker;
    private Box p3Marker;

    [SetUp]
    public void SetUp()
    {
        AddStep("Initialize curve and markers", () =>
        {
            Clear();

            curve = new BezierCurve
            {
                P0 = new Vector2(100, 400),
                P1 = new Vector2(200, 100),
                P2 = new Vector2(600, 100),
                P3 = new Vector2(700, 400),
                Thickness = 4f,
                Color = Color.White,
                Depth = 1
            };

            Add(curve);

            Add(p0Marker = new DraggableMarker(curve.P0, Color.Red)
            {
                OnPositionChanged = pos => curve.P0 = pos
            });
            Add(p1Marker = new DraggableMarker(curve.P1, Color.LimeGreen)
            {
                OnPositionChanged = pos => curve.P1 = pos
            });
            Add(p2Marker = new DraggableMarker(curve.P2, Color.LimeGreen)
            {
                OnPositionChanged = pos => curve.P2 = pos
            });
            Add(p3Marker = new DraggableMarker(curve.P3, Color.Blue)
            {
                OnPositionChanged = pos => curve.P3 = pos
            });
        });
    }

    [Test]
    public void TestControlPointManipulation()
    {
        AddStep("Move P1 down", () => updatePoint1(new Vector2(200, 300)));
        AddStep("Move P2 down", () => updatePoint2(new Vector2(600, 300)));

        AddStep("Create a loop", () =>
        {
            updatePoint1(new Vector2(600, 100));
            updatePoint2(new Vector2(200, 100));
        });

        AddStep("Create S-curve", () =>
        {
            updatePoint1(new Vector2(200, 700));
            updatePoint2(new Vector2(600, 100));
        });

        AddStep("Reset to arch", () =>
        {
            updatePoint1(new Vector2(200, 100));
            updatePoint2(new Vector2(600, 100));
        });
    }

    [Test]
    public void TestStyling()
    {
        AddStep("Increase thickness to 10", () => curve.Thickness = 10f);
        AddAssert("Thickness is 10", () => Precision.AlmostEquals(curve.Thickness, 10f));

        AddStep("Decrease thickness to 2", () => curve.Thickness = 2f);

        AddStep("Change color to Cyan", () => curve.Color = Color.Cyan);
        AddAssert("Color is Cyan", () => curve.Color == Color.Cyan);
    }

    private void updatePoint1(Vector2 newPos)
    {
        curve.P1 = newPos;
        p1Marker.Position = newPos;
    }

    private void updatePoint2(Vector2 newPos)
    {
        curve.P2 = newPos;
        p2Marker.Position = newPos;
    }

    private class DraggableMarker : Box
    {
        public Action<Vector2>? OnPositionChanged { get; set; }

        public DraggableMarker(Vector2 initialPosition, Color color)
        {
            Size = new Vector2(15, 15);
            Origin = Anchor.Centre;
            Position = initialPosition;
            Color = color;
            Depth = 0;
        }

        public override bool OnDragStart(MouseButtonEvent e) => true;

        public override bool OnDrag(MouseEvent e)
        {
            Position += e.Delta;
            OnPositionChanged?.Invoke(Position);
            return true;
        }
    }
}
