// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using NUnit.Framework;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Maths;
using Sakura.Framework.Testing;

namespace Sakura.Framework.Tests.Visuals.Containers;

public partial class TestTextFlowContainer : TestScene
{
    private TextFlowContainer textFlow = null!;
    private Container frame = null!;

    [SetUp]
    public void SetUp()
    {
        AddStep("Create text flow", () =>
        {
            Clear();

            Add(frame = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(480, 600),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Color = Color.DarkSlateGray
                    },
                    textFlow = new TextFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        Width = 1,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding(10),
                    }
                }
            });

            textFlow.AddText("This is a TextFlowContainer. Long running text automatically wraps into multiple lines at the container's width, word by word.");
        });
    }

    [Test]
    public void TestMixedStyles()
    {
        AddStep("Add colored text", () => textFlow.AddText(" Different calls can use different styles,", st => st.Color = Color.Orange));
        AddStep("Add big text", () => textFlow.AddText(" including different sizes,", st =>
        {
            st.Font = st.Font.With(size: 36);
            st.Color = Color.SkyBlue;
        }));
        AddStep("Add small text", () => textFlow.AddText(" or smaller fine print, all flowing together in one block.", st =>
        {
            st.Font = st.Font.With(size: 16);
            st.Color = Color.LightGray;
        }));
    }

    [Test]
    public void TestParagraphsAndLines()
    {
        AddStep("Add paragraph", () => textFlow.AddParagraph("A new paragraph starts after a vertical gap controlled by ParagraphSpacing.", st => st.Color = Color.PaleGreen));
        AddStep("Add line break text", () => textFlow.AddText("Embedded\nnewlines\nbreak lines without paragraph spacing.", st => st.Color = Color.Khaki));
        AddStep("New line", () => textFlow.NewLine());
        AddStep("New paragraph", () => textFlow.NewParagraph());
        AddStep("Add trailing text", () => textFlow.AddText("Trailing text after manual breaks."));
        AddAssert("Has children", () => textFlow.Children.Count > 0);
    }

    [Test]
    public void TestReflowOnResize()
    {
        AddSliderStep("Frame width", 150f, 900f, 480f, w =>
        {
            if (frame != null)
                frame.Size = new Vector2(w, frame.Size.Y);
        });

        AddStep("Replace with single text", () => textFlow.Text = "Setting the Text property replaces all content with a single uniformly-styled run of words.");
    }
}
