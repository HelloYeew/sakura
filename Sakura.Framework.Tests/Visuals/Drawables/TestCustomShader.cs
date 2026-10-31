// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System.Runtime.InteropServices;
using NUnit.Framework;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

/// <summary>
/// A shader of a game's own (<c>Resources/Shaders/test-ripple.frag</c>), taking its values from a uniform block. Headless draws
/// nothing with one, so this is one to look at in the test browser, on each backend in turn.
/// </summary>
public partial class TestCustomShader : TestScene
{
    private static readonly CustomShaderStore store = new CustomShaderStore(new EmbeddedResourceStorage(typeof(TestApp).Assembly, "Sakura.Framework.Tests.Resources.Shaders"));

    private Ripple ripple = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Show a ripple", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(ripple = new Ripple
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(480),
                Color = ColorExtensions.FromHex("#E55F9A")
            });
        });
    }

    [Test]
    public void TestItsBlock()
    {
        AddSliderStep("Rings", 1f, 12f, 5f, rings => ripple.Rings = rings);
        AddSliderStep("Fade", 0f, 1f, 1f, alpha => ripple.Alpha = alpha);
        AddStep("Another color", () => ripple.Color = ColorExtensions.FromHex("#9EC3CE"));
        AddStep("Added as light", () => ripple.Blending = BlendingMode.Additive);
        AddStep("As usual", () => ripple.Blending = BlendingMode.Alpha);
    }

    /// <summary>
    /// Several at once on one shader, each with its own values, and the framework drawing between them.
    /// </summary>
    [Test]
    public void TestSeveralTogether()
    {
        AddStep("Show three, with boxes between", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });

            for (int i = 0; i < 3; i++)
            {
                Add(new Ripple { X = 40 + i * 260, Y = 100, Size = new Vector2(240), Rings = 2 + i * 3, Color = i == 1 ? ColorExtensions.FromHex("#FFD27F") : ColorExtensions.FromHex("#C9A7F0") });
                Add(new Box { X = 240 + i * 260, Y = 200, Size = new Vector2(60), Color = ColorExtensions.FromHex("#7E9B3E") });
            }
        });
    }

    /// <summary>
    /// Inside a mask, as anything else is: clipped to its corners, and faded by its fade.
    /// </summary>
    [Test]
    public void TestInAMask()
    {
        Container mask = null!;

        AddStep("Put it in a mask", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(mask = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(320),
                Masking = true,
                CornerRadius = 60,
                Child = ripple = new Ripple
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(480),
                    Color = ColorExtensions.FromHex("#E55F9A")
                }
            });
        });
        AddSliderStep("Corner radius", 0f, 160f, 60f, radius => mask.CornerRadius = radius);
        AddSliderStep("Mask fade", 0f, 100f, 0f, fade => mask.MaskingFade = new MarginPadding(fade));
    }

    /// <summary>
    /// One that won't build is logged once and left out; what's behind it still draws.
    /// </summary>
    [Test]
    public void TestOneThatWontBuild() => AddStep("Show one", () => Add(new Broken { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(200) }));

    /// <summary>
    /// <c>RippleBlock</c> in <c>test-ripple.frag</c>, field for field.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct RippleBlock
    {
        [FieldOffset(0)]
        public Vector4 Color;

        [FieldOffset(16)]
        public float Time;

        [FieldOffset(20)]
        public float Rings;

        [FieldOffset(24)]
        public float Alpha;
    }

    private partial class Ripple : Drawable
    {
        public float Rings { get; set; } = 5;

        private RippleBlock block() => new RippleBlock
        {
            Color = new Vector4(Color.R / 255f, Color.G / 255f, Color.B / 255f, 1),
            Time = (float)(Clock.CurrentTime / 1000),
            Rings = Rings,
            Alpha = DrawAlpha
        };

        public override void Update()
        {
            Invalidate(InvalidationFlags.DrawInfo);
            base.Update();
        }

        protected override DrawNode CreateDrawNode() => new CustomShaderDrawNode<RippleBlock>(store, "test-ripple.frag", nameof(RippleBlock), block);
    }

    private partial class Broken : Drawable
    {
        protected override DrawNode CreateDrawNode() => new CustomShaderDrawNode(store, "test-broken.frag");
    }
}
