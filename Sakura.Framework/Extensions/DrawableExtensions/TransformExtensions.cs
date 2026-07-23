// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Collections.Generic;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
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
        // An "immediate" transform starts now (no pending Wait()/Then()/sequence offset). Only these
        // may retarget an in-flight transform; delayed/chained ones are always appended so sequences
        // keep their ordering. Capture before TimeUntilTransformsCanStart is reset below.
        bool immediate = drawable.TimeUntilTransformsCanStart == 0;

        double startTime = drawable.Clock.CurrentTime + drawable.TimeUntilTransformsCanStart;
        transform.StartTime = startTime;
        transform.EndTime = startTime + duration;

        // Reset the delay after a transform has been added.
        // This makes subsequent chains concurrent by default unless another Wait() or Then() is called.
        drawable.TimeUntilTransformsCanStart = 0;

        // Redirect an already-running transform on the same property in place rather than adding a new
        // one, so per-frame retargets (e.g. a slider fill following a drag) keep advancing instead of
        // restarting their timeline every frame and freezing. Only for real (non-instant) animations,
        // instant sets fall through and snap as before.
        if (immediate && duration > 0 && !drawable.SuppressRetargetOnAdd && drawable.TryRetargetInFlight(transform))
            return transform;

        drawable.AddTransform(transform);
        return transform;
    }

    /// <summary>
    /// Moves the drawable to a specific position over a duration.
    /// </summary>
    public static Drawable MoveTo(this Drawable drawable, Vector2 newPosition, double duration = 0, EasingFunction easing = default)
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
    /// Moves the drawable to a specific X position over a duration.
    /// </summary>
    public static Drawable MoveToX(this Drawable drawable, float newX, double duration = 0, EasingFunction easing = default)
    {
        drawable.addTransform(new MoveTransform
        {
            StartValue = drawable.Position,
            EndValue = new Vector2(newX, drawable.Position.Y),
            Easing = easing
        }, duration);
        return drawable;
    }

    /// <summary>
    /// Moves the drawable to a specific Y position over a duration.
    /// </summary>
    public static Drawable MoveToY(this Drawable drawable, float newY, double duration = 0, EasingFunction easing = default)
    {
        drawable.addTransform(new MoveTransform
        {
            StartValue = drawable.Position,
            EndValue = new Vector2(drawable.Position.X, newY),
            Easing = easing
        }, duration);
        return drawable;
    }

    /// <summary>
    /// Moves the drawable by an offset relative to its current position over a duration.
    /// </summary>
    public static Drawable MoveToOffset(this Drawable drawable, Vector2 offset, double duration = 0, EasingFunction easing = default)
        => drawable.MoveTo(drawable.Position + offset, duration, easing);

    /// <summary>
    /// Resizes the drawable to a specific size over a duration.
    /// </summary>
    public static Drawable ResizeTo(this Drawable drawable, Vector2 newSize, double duration = 0, EasingFunction easing = default)
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
    /// Resizes the drawable to a uniform size over a duration.
    /// </summary>
    public static Drawable ResizeTo(this Drawable drawable, float newSize, double duration = 0, EasingFunction easing = default)
        => drawable.ResizeTo(new Vector2(newSize), duration, easing);

    /// <summary>
    /// Adjusts the drawable's scale to a specific value over a duration.
    /// </summary>
    public static Drawable ScaleTo(this Drawable drawable, Vector2 newScale, double duration = 0, EasingFunction easing = default)
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
    public static Drawable ScaleTo(this Drawable drawable, float newScale, double duration = 0, EasingFunction easing = default)
        => drawable.ScaleTo(new Vector2(newScale), duration, easing);

    /// <summary>
    /// Rotates the drawable to a specific angle over a duration.
    /// </summary>
    public static Drawable RotateTo(this Drawable drawable, float newRotation, double duration = 0, EasingFunction easing = default)
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
    /// Begins an infinite continuous spin at the given revolution duration.
    /// Each call to <see cref="Spin"/> replaces the previous spin transform.
    /// Pair with <c>.Loop()</c> on a <see cref="TransformSequence{T}"/> or just use directly —
    /// <c>Spin</c> internally marks itself as looping.
    /// </summary>
    /// <param name="revolutionDuration">Duration in ms for one full 360° revolution.</param>
    /// <param name="direction">Clockwise or counter-clockwise.</param>
    public static Drawable Spin(this Drawable drawable, double revolutionDuration, RotationDirection direction = RotationDirection.Clockwise)
    {
        double duration = direction == RotationDirection.Clockwise ? revolutionDuration : -revolutionDuration;
        var t = drawable.addTransform(new SpinTransform
        {
            RevolutionDuration = Math.Abs(revolutionDuration)
        }, Math.Abs(revolutionDuration));
        t.IsLooping = true;
        return drawable;
    }

    /// <summary>
    /// Fades the drawable to a specific alpha over a duration.
    /// </summary>
    public static Drawable FadeTo(this Drawable drawable, float newAlpha, double duration = 0, EasingFunction easing = default)
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
    public static Drawable FadeIn(this Drawable drawable, double duration = 0, EasingFunction easing = default) => drawable.FadeTo(1, duration, easing);

    /// <summary>
    /// Fades the drawable to zero alpha over a duration.
    /// </summary>
    public static Drawable FadeOut(this Drawable drawable, double duration = 0, EasingFunction easing = default) => drawable.FadeTo(0, duration, easing);

    /// <summary>
    /// Sets alpha to 0 then fades to full alpha over a duration.
    /// </summary>
    public static Drawable FadeInFromZero(this Drawable drawable, double duration = 0, EasingFunction easing = default)
    {
        drawable.Alpha = 0;
        return drawable.FadeTo(1, duration, easing);
    }

    /// <summary>
    /// Sets alpha to 0 then fades to full alpha over a duration. Alias for <see cref="FadeInFromZero"/>.
    /// </summary>
    public static Drawable FadeFromZero(this Drawable drawable, double duration = 0, EasingFunction easing = default)
        => drawable.FadeInFromZero(duration, easing);

    /// <summary>
    /// Fades the drawable to zero alpha over a duration.
    /// </summary>
    public static Drawable FadeToZero(this Drawable drawable, double duration = 0, EasingFunction easing = default)
        => drawable.FadeTo(0, duration, easing);

    /// <summary>
    /// Instantly flashes the drawable to a specific colour, then fades back to its original colour over a duration.
    /// </summary>
    public static Drawable FlashColour(this Drawable drawable, Color flashColour, double duration, EasingFunction easing = default)
    {
        drawable.addTransform(new FlashColorTransform
        {
            FlashColour = flashColour,
            Easing = easing
        }, duration);
        return drawable;
    }

    /// <summary>
    /// Fades the drawable's colour to a specific colour over a duration.
    /// </summary>
    public static Drawable FadeToColour(this Drawable drawable, Color newColour, double duration = 0, EasingFunction easing = default)
    {
        drawable.addTransform(new ColorTransform
        {
            EndValue = newColour,
            Easing = easing
        }, duration);
        return drawable;
    }

    /// <summary>
    /// Animates the container's edge-effect color to a specific color over a duration.
    /// </summary>
    public static T FadeEdgeEffectTo<T>(this T container, Color newColour, double duration = 0, EasingFunction easing = default) where T : Container
    {
        container.addTransform(new EdgeEffectColourTransform
        {
            EndValue = newColour,
            Easing = easing
        }, duration);
        return container;
    }

    /// <summary>
    /// Animates the alpha of the container's edge-effect color to a specific value over a duration,
    /// keeping the current RGB.
    /// </summary>
    public static T FadeEdgeEffectTo<T>(this T container, float newAlpha, double duration = 0, EasingFunction easing = default) where T : Container
    {
        var current = container.EdgeEffect.Colour;
        var target = Color.FromArgb((int)Math.Clamp(newAlpha * 255f, 0f, 255f), current);
        return container.FadeEdgeEffectTo(target, duration, easing);
    }

    /// <summary>
    /// Animates the container's edge-effect blur radius to a specific value over a duration.
    /// </summary>
    public static T TweenEdgeEffectRadiusTo<T>(this T container, float newRadius, double duration = 0, EasingFunction easing = default) where T : Container
    {
        container.addTransform(new EdgeEffectRadiusTransform
        {
            EndValue = newRadius,
            Easing = easing
        }, duration);
        return container;
    }

    /// <summary>
    /// Schedules the next transform to start after a delay from the end of the last transform.
    /// </summary>
    public static Drawable Wait(this Drawable drawable, double duration)
    {
        double latestEnd = drawable.GetLatestTransformEndTime();
        if (latestEnd < drawable.Clock.CurrentTime) latestEnd = drawable.Clock.CurrentTime;

        drawable.TimeUntilTransformsCanStart = latestEnd - drawable.Clock.CurrentTime + duration;
        return drawable;
    }

    /// <summary>
    /// Schedules the next transform to start immediately after the last transform finishes.
    /// Optionally adds an extra delay after that.
    /// </summary>
    public static Drawable Then(this Drawable drawable, double extraDelay = 0) => drawable.Wait(extraDelay);

    /// <summary>
    /// Loops all transforms that end at the latest end time, infinitely.
    /// </summary>
    public static Drawable Loop(this Drawable drawable, double pause = 0)
    {
        drawable.LoopLatestTransforms(0, pause);
        return drawable;
    }

    /// <summary>
    /// Loops all transforms that end at the latest end time, a finite number of times.
    /// </summary>
    public static Drawable Loop(this Drawable drawable, int count, double pause = 0)
    {
        drawable.LoopLatestTransforms(count, pause);
        return drawable;
    }

    /// <summary>
    /// Begins a transform sequence on this drawable, enabling advanced chaining,
    /// scoped looping, and completion callbacks.
    /// <example><code>
    /// sprite.TransformSequence()
    ///       .FadeIn(300)
    ///       .Then()
    ///       .Wait(500)
    ///       .FadeOut(300)
    ///       .OnComplete(s => s.Expire());
    /// </code></example>
    /// </summary>
    public static TransformSequence<T> TransformSequence<T>(this T drawable) where T : Drawable
        => new TransformSequence<T>(drawable);

    /// <summary>
    /// Runs <paramref name="builder"/> against the drawable as a scoped sequence, then loops
    /// that sequence infinitely with an optional pause between iterations.
    /// <example><code>
    /// startText.Loop(b => b.FadeTo(0.25f).Then().FadeTo(1, 1000), 1250);
    /// </code></example>
    /// </summary>
    /// <param name="builder">Action that defines the transforms making up one loop iteration.</param>
    /// <param name="duration">Duration of one full iteration in ms (sets EndTime of the looping block).</param>
    /// <param name="pause">Pause between iterations in ms.</param>
    public static Drawable Loop(this Drawable drawable, Action<Drawable> builder, double duration, double pause = 0)
    {
        int countBefore = drawable.TransformCount;
        double savedDelay = drawable.TimeUntilTransformsCanStart;

        bool savedSuppress = drawable.SuppressRetargetOnAdd;
        drawable.SuppressRetargetOnAdd = true;
        builder(drawable);
        drawable.SuppressRetargetOnAdd = savedSuppress;

        var added = new List<Transform>();
        drawable.CollectTransformsSince(countBefore, added);
        foreach (var t in added)
        {
            t.IsLooping = true;
            t.LoopCount = 0;
            t.LoopPause = pause;
            if (duration > 0)
                t.EndTime = t.StartTime + duration;
        }

        drawable.TimeUntilTransformsCanStart = savedDelay;
        return drawable;
    }

    /// <summary>
    /// Runs <paramref name="builder"/> and loops the result a finite number of times.
    /// </summary>
    public static Drawable Loop(this Drawable drawable, Action<Drawable> builder, double duration, int count, double pause = 0)
    {
        int countBefore = drawable.TransformCount;
        double savedDelay = drawable.TimeUntilTransformsCanStart;

        bool savedSuppress = drawable.SuppressRetargetOnAdd;
        drawable.SuppressRetargetOnAdd = true;
        builder(drawable);
        drawable.SuppressRetargetOnAdd = savedSuppress;

        var added = new List<Transform>();
        drawable.CollectTransformsSince(countBefore, added);
        if (added.Count == 0)
        {
            drawable.TimeUntilTransformsCanStart = savedDelay;
            return drawable;
        }

        // All transforms in the builder share the same loop origin — the earliest StartTime.
        // This ensures IsLoopComplete fires at the same wall-clock moment for every transform.
        double loopOrigin = double.MaxValue;
        foreach (var t in added)
            if (t.StartTime < loopOrigin) loopOrigin = t.StartTime;

        foreach (var t in added)
        {
            t.IsLooping = true;
            t.LoopCount = count;
            t.LoopPause = pause;
            t.LoopOrigin = loopOrigin;
            if (duration > 0)
                t.EndTime = t.StartTime + duration;
        }

        drawable.TimeUntilTransformsCanStart = savedDelay;
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
