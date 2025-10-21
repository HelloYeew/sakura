// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Extensions.DrawableExtensions;

/// <summary>
/// Provides transformation-related extension methods for <see cref="Drawable"/> objects.
/// </summary>
public static class TransformExtensions
{
    /// <summary>
    /// Add a transform to the drawable with proper timing.
    /// </summary>
    private static T addTransform<T>(this Drawable drawable, T transform, double duration) where T : Transform
    {
        double startTime = drawable.Clock.CurrentTime + drawable.TimeUntilTransformsCanStart;
        transform.StartTime = startTime;
        transform.EndTime = startTime + duration;

        drawable.AddTransform(transform);

        // Reset the delay after a transform has been added.
        // This makes subsequent chains concurrent by default unless another Wait() or Then() is called.
        drawable.TimeUntilTransformsCanStart = 0;
        return transform;
    }

    /// <summary>
    /// Moves the drawable to a specific position over a duration.
    /// </summary>
    public static Drawable MoveTo(this Drawable drawable, Vector2 newPosition, double duration = 0, EasingType easing = EasingType.None)
    {
        drawable.addTransform(new MoveTransform
        {
            StartValue = drawable.Position,
            EndValue = newPosition,
            Easing = easing
        }, duration);
        return drawable;
    }

    /// <summary>
    /// Resizes the drawable to a specific size over a duration.
    /// </summary>
    public static Drawable ResizeTo(this Drawable drawable, Vector2 newSize, double duration = 0, EasingType easing = EasingType.None)
    {
        drawable.addTransform(new ResizeTransform
        {
            StartValue = drawable.Size,
            EndValue = newSize,
            Easing = easing
        }, duration);
        return drawable;
    }

    /// <summary>
    /// Adjusts the drawable's scale to a specific value over a duration.
    /// </summary>
    public static Drawable ScaleTo(this Drawable drawable, Vector2 newScale, double duration = 0, EasingType easing = EasingType.None)
    {
        drawable.addTransform(new ScaleTransform
        {
            StartValue = drawable.Scale,
            EndValue = newScale,
            Easing = easing
        }, duration);
        return drawable;
    }

    /// <summary>
    /// Adjusts the drawable's scale uniformly to a specific value over a duration.
    /// </summary>
    public static Drawable ScaleTo(this Drawable drawable, float newScale, double duration = 0, EasingType easing = EasingType.None)
        => drawable.ScaleTo(new Vector2(newScale), duration, easing);

    /// <summary>
    /// Rotates the drawable to a specific angle over a duration.
    /// </summary>
    public static Drawable RotateTo(this Drawable drawable, float newRotation, double duration = 0, EasingType easing = EasingType.None)
    {
        drawable.addTransform(new RotateTransform
        {
            StartValue = drawable.Rotation,
            EndValue = newRotation,
            Easing = easing
        }, duration);
        return drawable;
    }

    /// <summary>
    /// Fades the drawable to a specific alpha over a duration.
    /// </summary>
    public static Drawable FadeTo(this Drawable drawable, float newAlpha, double duration = 0, EasingType easing = EasingType.None)
    {
        drawable.addTransform(new AlphaTransform
        {
            StartValue = drawable.Alpha,
            EndValue = newAlpha,
            Easing = easing
        }, duration);
        return drawable;
    }

    /// <summary>
    /// Fades the drawable to full alpha over a duration.
    /// </summary>
    public static Drawable FadeIn(this Drawable drawable, double duration = 0, EasingType easing = EasingType.None) => drawable.FadeTo(1, duration, easing);

    /// <summary>
    /// Fades the drawable to zero alpha over a duration.
    /// </summary>
    public static Drawable FadeOut(this Drawable drawable, double duration = 0, EasingType easing = EasingType.None) => drawable.FadeTo(0, duration, easing);

    /// <summary>
    /// Fades the drawable from zero alpha to full alpha over a duration.
    /// </summary>
    public static Drawable FadeFromZero(this Drawable drawable, double duration = 0, EasingType easing = EasingType.None)
    {
        drawable.Alpha = 0;
        return drawable.FadeTo(1, duration, easing);
    }

    /// <summary>
    /// Fades the drawable to zero alpha over a duration.
    /// </summary>
    public static Drawable FadeToZero(this Drawable drawable, double duration = 0, EasingType easing = EasingType.None)
    {
        return drawable.FadeTo(0, duration, easing);
    }

    /// <summary>
    /// Instantly flashes the drawable to a specific colour, then fades back to its original colour over a duration.
    /// </summary>
    public static Drawable FlashColour(this Drawable drawable, Color flashColour, double duration, EasingType easing = EasingType.None)
    {
        drawable.addTransform(new FlashColorTransform
        {
            FlashColour = flashColour,
            Easing = easing
        }, duration);
        return drawable;
    }

    /// <summary>
    /// Schedules the next transformation to start after a specified delay from the end of the last transformation.
    /// </summary>
    public static Drawable Wait(this Drawable drawable, double duration)
    {
        double latestEnd = drawable.GetLatestTransformEndTime();
        if (latestEnd < drawable.Clock.CurrentTime) latestEnd = drawable.Clock.CurrentTime;

        drawable.TimeUntilTransformsCanStart = latestEnd - drawable.Clock.CurrentTime + duration;
        return drawable;
    }

    /// <summary>
    /// Schedules the next transformation to start immediately after the last transformation finishes.
    /// </summary>
    public static Drawable Then(this Drawable drawable) => drawable.Wait(0);

    /// <summary>
    /// Loop the latest added transforms.
    /// </summary>
    public static Drawable Loop(this Drawable drawable)
    {
        drawable.LoopLatestTransforms();
        return drawable;
    }

    /// <summary>
    /// Schedules an action to be executed once at the current time.
    /// </summary>
    public static Drawable Schedule(this Drawable drawable, Action action)
    {
        drawable.Scheduler.Add(action);
        return drawable;
    }

    /// <summary>
    /// Schedules an action to be executed after a delay.
    /// </summary>
    public static Drawable AddDelayed(this Drawable drawable, Action action, double delay)
    {
        drawable.Scheduler.AddDelayed(action, delay);
        return drawable;
    }
}
