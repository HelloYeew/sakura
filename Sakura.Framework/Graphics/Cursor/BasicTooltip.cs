// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Text;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Cursor;

/// <summary>
/// A simple dark-box tooltip with white text. Used as the default by <see cref="TooltipContainer"/>.
/// Fades in/out and glides to its target position.
/// </summary>
public partial class BasicTooltip : VisibilityContainer, ITooltip
{
    private readonly SpriteText label;

    public BasicTooltip()
    {
        AutoSizeAxes = Axes.Both;
        Depth = float.MinValue;

        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(1),
                Color = Color.FromArgb(220, 30, 30, 30)
            },
            label = new SpriteText
            {
                Font = FontUsage.Default.With(size: 14),
                Color = Color.White,
                Margin = new MarginPadding(6)
            }
        };
    }

    public void SetContent(string content) => label.Text = content;

    public void Move(Vector2 position) =>
        this.MoveTo(position, Alpha > 0 ? 100 : 0, Easing.OutQuint);

    protected override bool StartHidden => true;

    protected override void PopIn() => this.FadeIn(150, Easing.OutQuint);

    protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);
}
