// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using NUnit.Framework;
using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Rendering;
using Sakura.Framework.Graphics.Textures;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Drawables;

/// <summary>
/// A storm of pieces in one draw, each placed, turned and tinted on its own, set again each frame.
/// </summary>
public partial class TestParticleField : TestScene
{
    [Resolved]
    private ITextureManager textureManager { get; set; } = null!;

    private Storm storm = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Show a storm", () =>
        {
            Clear();

            Add(new Box { RelativeSizeAxes = Axes.Both, Color = ColorExtensions.FromHex("#151D30") });
            Add(storm = new Storm());
        });
    }

    [Test]
    public void TestAStorm()
    {
        AddSliderStep("Pieces", 0, 3000, 500, count => storm.PieceCount = count);
        AddSliderStep("Size", 2f, 80f, 16f, size => storm.PieceSize = size);
        AddUntilStep("Drawing them", () => storm.Count == storm.PieceCount);
    }

    [Test]
    public void TestATexture()
    {
        AddStep("Give it a texture", () => storm.Texture = textureManager.Get("small.png"));
        AddStep("Tinted rectangles again", () => storm.Texture = null);
    }

    [Test]
    public void TestAdditiveLight()
    {
        AddStep("Additive", () => storm.Blending = BlendingMode.Additive);
        AddStep("Alpha", () => storm.Blending = BlendingMode.Alpha);
    }

    /// <summary>
    /// Clearing leaves nothing drawn, and it picks up again.
    /// </summary>
    [Test]
    public void TestNothingToDraw()
    {
        AddStep("No pieces", () => storm.PieceCount = 0);
        AddUntilStep("No vertices", () => storm.VertexCount == 0);
        AddStep("Some again", () => storm.PieceCount = 200);
        AddUntilStep("Vertices", () => storm.VertexCount == 200 * 6);
    }

    /// <summary>
    /// Pieces swirling round the middle, placed again each frame.
    /// </summary>
    private partial class Storm : ParticleField
    {
        private static readonly Color[] tints =
        {
            ColorExtensions.FromHex("#FFB7D5"),
            ColorExtensions.FromHex("#FFE3EE"),
            ColorExtensions.FromHex("#C9A7F0"),
            ColorExtensions.FromHex("#FFD27F")
        };

        public int PieceCount { get; set; } = 500;

        public float PieceSize { get; set; } = 16;

        public override void Update()
        {
            Clear();

            double time = Clock.CurrentTime / 1000;
            Vector2 middle = DrawSize / 2;
            float reach = Math.Min(middle.X, middle.Y) * 0.95f;

            for (int i = 0; i < PieceCount; i++)
            {
                // Spread evenly through the disc by the golden angle, each turning at its own speed.
                float along = MathF.Sqrt((i + 0.5f) / Math.Max(1, PieceCount));
                float angle = i * 2.39996f + (float)(time * (0.2f + along * 0.5f));
                var position = middle + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (along * reach);

                Add(new Particle(position, new Vector2(PieceSize, PieceSize * 0.6f), angle + (float)time, tints[i % tints.Length], 0.4f + 0.6f * along));
            }

            base.Update();
        }
    }
}
