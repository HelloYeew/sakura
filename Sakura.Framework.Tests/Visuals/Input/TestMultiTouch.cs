// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Tests.Visuals.Input;

/// <summary>
/// A pad that takes every finger on it. Each finger draws its own line as it moves, under a numbered marker that goes
/// when it lifts. On a touchscreen, put several fingers down at once.
/// </summary>
public partial class TestMultiTouch : ManualInputManagerTestScene
{
    private TouchPad pad = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Add a touch pad", () =>
        {
            TestContent.Clear();
            TestContent.Add(pad = new TouchPad
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1)
            });
        });
    }

    [Test]
    public void TestEachFingerDrawsItsOwnLine()
    {
        AddStep("Put three fingers down", () =>
        {
            for (int i = 0; i < 3; i++)
                InputManager.TouchDown((TouchSource)i, start(i, 3));
        });
        AddAssert("A marker under each", () => pad.FingersDown == 3
                                               && markerAt(TouchSource.Touch1, start(0, 3))
                                               && markerAt(TouchSource.Touch2, start(1, 3))
                                               && markerAt(TouchSource.Touch3, start(2, 3)));

        AddStep("Move them, two up and one down", () =>
        {
            for (int step = 1; step <= 10; step++)
            {
                for (int i = 0; i < 3; i++)
                    InputManager.MoveTouchTo((TouchSource)i, moved(i, step));
            }
        });
        AddAssert("Each marker followed its finger", () => markerAt(TouchSource.Touch1, moved(0, 10))
                                                           && markerAt(TouchSource.Touch2, moved(1, 10))
                                                           && markerAt(TouchSource.Touch3, moved(2, 10)));
        AddAssert("Each finger drew a line", () => pad[TouchSource.Touch1]!.LinePoints > 2
                                                   && pad[TouchSource.Touch2]!.LinePoints > 2
                                                   && pad[TouchSource.Touch3]!.LinePoints > 2);

        AddStep("Lift the middle finger", () => InputManager.TouchUp(TouchSource.Touch2));
        AddAssert("Only its marker went", () => pad.FingersDown == 2
                                                && pad[TouchSource.Touch2] == null
                                                && markerAt(TouchSource.Touch1, moved(0, 10))
                                                && markerAt(TouchSource.Touch3, moved(2, 10)));

        AddStep("Lift the others", () =>
        {
            InputManager.TouchUp(TouchSource.Touch1);
            InputManager.TouchUp(TouchSource.Touch3);
        });
        AddAssert("No markers left", () => pad.FingersDown == 0);
    }

    [Test]
    public void TestTenFingers()
    {
        AddStep("Put ten fingers down", () =>
        {
            for (int i = 0; i < InputState.MAX_TOUCHES; i++)
                InputManager.TouchDown((TouchSource)i, start(i, InputState.MAX_TOUCHES));
        });
        AddAssert("Ten markers", () => pad.FingersDown == InputState.MAX_TOUCHES
                                       && markerAt(TouchSource.Touch10, start(InputState.MAX_TOUCHES - 1, InputState.MAX_TOUCHES)));

        AddStep("Lift them all", () =>
        {
            for (int i = 0; i < InputState.MAX_TOUCHES; i++)
                InputManager.TouchUp((TouchSource)i);
        });
        AddAssert("No markers left", () => pad.FingersDown == 0);
    }

    // Where finger i of count lands: spread across the pad, halfway down.
    private Vector2 start(int i, int count) => pad.ToScreenSpace(pad.DrawSize * new Vector2((i + 1f) / (count + 1), 0.5f));

    // Where finger i is after the given number of steps: the even ones go up, the odd ones down.
    private Vector2 moved(int i, int step) => start(i, 3) + new Vector2(0, (i % 2 == 0 ? -8 : 8) * step);

    private bool markerAt(TouchSource source, Vector2 screenSpacePosition)
        => pad[source] is FingerTrail trail && Precision.AlmostEquals(trail.MarkerScreenPosition, screenSpacePosition, 1f);

    private partial class TouchPad : Container
    {
        private static readonly Color[] colours =
        {
            Color.HotPink, Color.DeepSkyBlue, Color.LimeGreen, Color.Orange, Color.MediumPurple,
            Color.Gold, Color.Tomato, Color.Turquoise, Color.Violet, Color.YellowGreen
        };

        private readonly FingerTrail?[] fingers = new FingerTrail?[InputState.MAX_TOUCHES];

        public int FingersDown { get; private set; }

        /// <summary>
        /// The finger that's down for <paramref name="source"/>, if one is.
        /// </summary>
        public FingerTrail? this[TouchSource source] => fingers[(int)source];

        public TouchPad()
        {
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(1),
                    Color = Color.DarkSlateGray
                },
                new SpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Y = 20,
                    Text = "Put several fingers down",
                    Color = Color.LightGray,
                    Font = FontUsage.Default.With(size: 20)
                }
            };
        }

        public override bool OnTouchDown(TouchEvent e)
        {
            var trail = new FingerTrail(e.Source, colours[(int)e.Source], ToLocalSpace(e.ScreenSpacePosition));

            fingers[(int)e.Source] = trail;
            FingersDown++;
            Add(trail);
            return true;
        }

        public override bool OnTouchMove(TouchEvent e)
        {
            fingers[(int)e.Source]?.MoveTo(ToLocalSpace(e.ScreenSpacePosition));
            return true;
        }

        public override bool OnTouchUp(TouchEvent e)
        {
            var trail = fingers[(int)e.Source];

            if (trail == null)
                return true;

            fingers[(int)e.Source] = null;
            FingersDown--;
            trail.Lift();
            return true;
        }
    }

    /// <summary>
    /// A finger's marker and the line it draws, in the pad's space.
    /// </summary>
    private partial class FingerTrail : Container
    {
        // A point every few units, rather than one for every move.
        private const float min_point_distance = 4;

        private readonly Path line;
        private readonly Container marker;

        public FingerTrail(TouchSource source, Color colour, Vector2 position)
        {
            RelativeSizeAxes = Axes.Both;
            Size = new Vector2(1);

            Children = new Drawable[]
            {
                line = new Path
                {
                    Thickness = 6,
                    Color = colour
                },
                marker = new Container
                {
                    Origin = Anchor.Centre,
                    Position = position,
                    Size = new Vector2(70),
                    Children = new Drawable[]
                    {
                        new Circle
                        {
                            RelativeSizeAxes = Axes.Both,
                            Size = new Vector2(1),
                            Color = colour,
                            Alpha = 0.6f
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = $"{(int)source + 1}",
                            Color = Color.White,
                            Font = FontUsage.Default.With(size: 28)
                        }
                    }
                }
            };

            line.AddVertex(position);
        }

        public Vector2 MarkerScreenPosition => marker.ToScreenSpace(marker.DrawSize / 2);

        public int LinePoints => line.PathVertices.Count;

        public void MoveTo(Vector2 position)
        {
            marker.Position = position;

            if ((position - line.PathVertices[^1]).LengthSquared() >= min_point_distance * min_point_distance)
                line.AddVertex(position);
        }

        /// <summary>
        /// Takes the marker away and fades the line out.
        /// </summary>
        public void Lift()
        {
            marker.Hide();
            this.FadeOut(1500);
            Expire();
        }
    }
}
