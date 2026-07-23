// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Performance;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Input;
using Sakura.Framework.Maths;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Cursor;

// TODO: Add IRemoveFromDrawVisualiser to make inspect mode not catch the cursor drawable.
// The cursor container should also show in the draw visualiser too.
public partial class CursorContainer : Container, IRemoveFromDrawVisualiser
{
    public Drawable ActiveCursor { get; protected set; }

    [Resolved]
    private IWindow window { get; set; } = null!;

    /// <summary>
    /// Whether the cursor should automatically hide when the physical mouse cursor leaves the OS window bounds.
    /// Defaults to true. Set to false for simulated cursors (e.g. using in test scene that need to always show)
    /// </summary>
    public bool HideWhenOutsideWindow { get; set; } = true;

    private Vector2 lastScreenSpaceMousePosition;
    private bool isCursorVisible = true;

    public CursorContainer()
    {
        Depth = float.MaxValue;
        RelativeSizeAxes = Axes.Both;
        Size = new Vector2(1);
        Add(ActiveCursor = CreateCursor());
    }

    protected virtual Drawable CreateCursor() => new DefaultCursor();

    public override void LoadComplete()
    {
        base.LoadComplete();
        // TODO: This should be more centralized. Like add an interface for cursor drawable.
        if (ActiveCursor is DefaultCursor defaultCursor)
        {
            window.CursorState.ValueChanged += state => defaultCursor.ChangeCursor(state.NewValue);
        }
    }

    public override void Update()
    {
        base.Update();
        if (!IsLoaded) return;

        var manager = GetContainingInputManager();
        if (manager != null)
            lastScreenSpaceMousePosition = manager.CurrentState.MousePosition;

        bool shouldBeVisible = !HideWhenOutsideWindow || window.CursorInWindow;

        if (isCursorVisible != shouldBeVisible)
        {
            isCursorVisible = shouldBeVisible;

            ActiveCursor.ClearTransforms();

            if (shouldBeVisible)
                ActiveCursor.ScaleTo(1, 200, Easing.OutQuint).FadeIn(200, Easing.OutQuint);
            else
                ActiveCursor.ScaleTo(0f, 200, Easing.OutQuint).FadeOut(200, Easing.OutQuint);
        }

        if (ActiveCursor.IsHidden)
            return;

        Vector2 localPosition = ToLocalSpace(lastScreenSpaceMousePosition);

        ActiveCursor.Position = localPosition;
    }

    private partial class DefaultCursor : Container
    {
        private readonly IconSprite iconSprite;

        public DefaultCursor()
        {
            Size = new Vector2(25);

            Add(iconSprite = new IconSprite()
            {
                Origin = Anchor.TopLeft,
                Anchor = Anchor.TopLeft,
                Color = Color.DeepPink,
                Icon = IconUsage.ArrowSelectorTool,
                IconSize = 25f,
                Position = new Vector2(-6, -5) // Just adjust position to make left-top corner the "tip" of the cursor
            });
        }

        public override bool OnClick(MouseButtonEvent e)
        {
            iconSprite.FlashColor(Color.White, 300, Easing.OutQuint);
            return base.OnClick(e);
        }

        public void ChangeCursor(CursorState state)
        {
            switch (state)
            {
                case CursorState.Default:
                    iconSprite.Icon = IconUsage.ArrowSelectorTool;
                    break;
                case CursorState.Pointer:
                    iconSprite.Icon = IconUsage.PanToolAlt;
                    break;
                case CursorState.Text:
                    iconSprite.Icon = IconUsage.AlignSelfStretch;
                    break;
                case CursorState.Wait:
                    iconSprite.Icon = IconUsage.Hourglass;
                    break;
                case CursorState.Crosshair:
                    iconSprite.Icon = IconUsage.Add;
                    break;
                case CursorState.NotAllowed:
                    iconSprite.Icon = IconUsage.Block;
                    break;
                default:
                    iconSprite.Icon = IconUsage.ArrowSelectorTool;
                    break;
            }
        }
    }
}
