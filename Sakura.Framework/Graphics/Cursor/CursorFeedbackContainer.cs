// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Allocation;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Input;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Graphics.Cursor;

/// <summary>
/// Drives the OS cursor from whatever is under the pointer, add one to an application and every
/// <see cref="IHasCursor"/> in the tree starts being honored.
/// </summary>
public partial class CursorFeedbackContainer : Container
{
    [Resolved]
    private IWindow window { get; set; } = null!;

    public CursorFeedbackContainer()
    {
        RelativeSizeAxes = Axes.Both;
    }

    public override void Update()
    {
        base.Update();

        // Reactive<T> drops a writing that does not change the value, so
        // there is no need to remember what was set last, and no SDL call unless the shape moved.
        window.CursorState.Value = Resolve(GetContainingInputManager());
    }

    /// <summary>
    /// Which cursor the given input state calls for. Separated from <see cref="Update"/> so that it
    /// can be exercised directly.
    /// </summary>
    public static CursorState Resolve(InputManager? inputManager)
    {
        if (inputManager == null)
            return CursorState.Default;

        // A drag holds capture after the pointer has left the drawable that started it, which is
        // the normal case when dragging an image past the cursor. Without this the hand would open
        // again the moment the content moved out from under the pointer, which is precisely when the
        // user most needs to be told the drag is still live.
        if (inputManager.DragCaptureTarget is IHasCursor dragged)
            return dragged.Cursor;

        // Front-to-back: buildPositional adds each drawable after recursing its children, so the
        // front-most receiver is at the head of the queue.
        var queue = inputManager.PositionalInputQueue;

        for (int i = 0; i < queue.Count; i++)
        {
            var drawable = queue[i];

            // Filtering on IsHovered rather than just walking the queue is what makes hover blocking
            // work here for free. The queue holds everything under the pointer, including drawables
            // behind a blocking overlay; the hovered set is only the prefix up to the first blocker,
            // so a control underneath an open menu cannot reach past it and set the cursor.
            if (!drawable.IsHovered)
                continue;

            if (drawable is IHasCursor provider)
                return provider.Cursor;
        }

        return CursorState.Default;
    }
}
