// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Graphics.Containers;

/// <summary>
/// A <see cref="Container"/> which adds a basic visibility state and animations.
/// </summary>
public abstract class VisibilityContainer : Container
{
    private Visibility state = Visibility.Hidden;

    public Visibility State
    {
        get => state;
        set
        {
            if (state == value) return;
            state = value;
            UpdateState(state);
        }
    }

    /// <summary>
    /// Whether this container should start hidden when first loaded.
    /// </summary>
    protected virtual bool StartHidden => true;

    public override void LoadComplete()
    {
        base.LoadComplete();

        if (StartHidden)
        {
            State = Visibility.Hidden;
            PopOut();
            FinishTransforms(true);
        }
        else
        {
            State = Visibility.Visible;
            PopIn();
        }
    }

    public override void Show() => State = Visibility.Visible;
    public override void Hide() => State = Visibility.Hidden;
    public void ToggleVisibility() => State = State == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;

    /// <summary>
    /// Implement any transition to be played when <see cref="State"/> becomes <see cref="Visibility.Visible"/>
    /// </summary>
    protected abstract void PopIn();

    /// <summary>
    /// Implement any transition to be played when <see cref="State"/> becomes <see cref="Visibility.Hidden"/>
    /// </summary>
    protected abstract void PopOut();

    protected virtual void UpdateState(Visibility newState)
    {
        if (newState == Visibility.Visible)
            PopIn();
        else
            PopOut();
    }
}

public enum Visibility
{
    Hidden,
    Visible
}
