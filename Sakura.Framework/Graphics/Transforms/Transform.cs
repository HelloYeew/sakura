// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Containers;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Maths;
using Sakura.Framework.Utilities;

namespace Sakura.Framework.Graphics.Transforms;

/// <summary>
/// The drawable property a <see cref="Transform"/> animates. Used to retarget an in-flight transform
/// on the same property in place (see <see cref="Transform.TryRetargetFrom"/>) rather than clearing and
/// recreating it, which would restart its timeline and can freeze rapidly-retargeted animations.
/// <see cref="None"/> transforms (callbacks, auto-size) never participate in retargeting.
/// </summary>
public enum TransformMember
{
    None,
    Position,
    Size,
    Scale,
    Rotation,
    Alpha,
    Color,
    EdgeEffectColour,
    EdgeEffectRadius,
}

public abstract class Transform : ITransform
{
    public double StartTime { get; set; }
    public double EndTime { get; set; }

    /// <summary>
    /// The drawable property this transform animates. Transforms with the same non <see cref="TransformMember.None"/>
    /// member can retarget one another in place while in flight.
    /// </summary>
    public virtual TransformMember Member => TransformMember.None;

    /// <summary>
    /// Redirects this in-flight transform toward <paramref name="incoming"/>'s destination while keeping
    /// this transform's <see cref="StartTime"/>, start value and <see cref="EndTime"/> — i.e. its
    /// animation timeline — so progress continues smoothly instead of restarting.
    /// Returns false (the default) when the two transforms are not the same concrete kind, in which case
    /// the caller adds <paramref name="incoming"/> as a new transform instead.
    /// </summary>
    public virtual bool TryRetargetFrom(Transform incoming) => false;

    /// <summary>
    /// The easing applied to this transform's progress. Accepts any built-in easing curve
    /// (via implicit conversion from the <c>Easing</c> enum) as well as parameterised curves such as
    /// <see cref="CubicBezierEasing"/> and <see cref="SpringEasing"/>.
    /// </summary>
    public EasingFunction Easing { get; set; }

    public bool IsLooping { get; set; }

    /// <summary>
    /// How many times to loop. 0 = infinite.
    /// Only meaningful when <see cref="IsLooping"/> is true.
    /// </summary>
    public int LoopCount { get; set; }

    /// <summary>
    /// Pause duration (ms) between loop iterations.
    /// Only meaningful when <see cref="IsLooping"/> is true.
    /// </summary>
    public double LoopPause { get; set; }

    /// <summary>
    /// The shared clock origin for loop completion checks.
    /// When multiple transforms belong to the same builder loop, they all use the
    /// earliest StartTime as the origin so <see cref="IsLoopComplete"/> fires simultaneously.
    /// Defaults to <see cref="StartTime"/> when not set explicitly.
    /// </summary>
    public double LoopOrigin { get; set; } = double.NaN;

    private double effectiveLoopOrigin => double.IsNaN(LoopOrigin) ? StartTime : LoopOrigin;

    public abstract void Apply(Drawable drawable, double time);

    /// <summary>
    /// Returns true if this looping transform has finished all its iterations at <paramref name="time"/>.
    /// Always false for infinite loops (LoopCount == 0).
    /// </summary>
    public bool IsLoopComplete(double time)
    {
        if (!IsLooping || LoopCount == 0) return false;

        double duration = EndTime - StartTime;
        if (duration <= 0) return true;

        double cycleLength = duration + LoopPause;
        double elapsed = time - effectiveLoopOrigin;
        return elapsed >= cycleLength * LoopCount;
    }

    protected double GetEasedProgress(double time)
    {
        if (time < StartTime) return 0;

        double duration = EndTime - StartTime;
        if (duration <= 0) return 1;

        double progress;
        if (IsLooping)
        {
            double cycleLength = duration + LoopPause;

            // Use LoopOrigin for the completion check so all transforms in a builder group
            // clamp to progress=1 at the same wall-clock moment.
            if (LoopCount > 0 && (time - effectiveLoopOrigin) >= cycleLength * LoopCount)
            {
                progress = 1;
            }
            else
            {
                double elapsed = time - StartTime;
                double timeInCycle = elapsed % cycleLength;

                // In the pause gap between iterations, hold at end value.
                if (timeInCycle >= duration)
                    progress = 1;
                else
                    progress = timeInCycle / duration;
            }
        }
        else
        {
            time = Math.Min(time, EndTime);
            progress = (time - StartTime) / duration;
        }

        return Easing.Apply(progress);
    }
}

public class MoveTransform : Transform
{
    private bool valueCaptured;
    public Vector2 StartValue;
    public Vector2 EndValue;

    public override TransformMember Member => TransformMember.Position;

    public override bool TryRetargetFrom(Transform incoming)
    {
        if (incoming is not MoveTransform m) return false;
        EndValue = m.EndValue;
        Easing = m.Easing;
        return true;
    }

    public override void Apply(Drawable drawable, double time)
    {
        if (!valueCaptured)
        {
            StartValue = drawable.Position;
            valueCaptured = true;
        }
        drawable.Position = Vector2.Lerp(StartValue, EndValue, (float)GetEasedProgress(time));
    }
}

public class ResizeTransform : Transform
{
    private bool valueCaptured;
    public Vector2 StartValue;
    public Vector2 EndValue;

    public override TransformMember Member => TransformMember.Size;

    public override bool TryRetargetFrom(Transform incoming)
    {
        if (incoming is not ResizeTransform r) return false;
        EndValue = r.EndValue;
        Easing = r.Easing;
        return true;
    }

    public override void Apply(Drawable drawable, double time)
    {
        if (!valueCaptured)
        {
            StartValue = drawable.Size;
            valueCaptured = true;
        }
        drawable.Size = Vector2.Lerp(StartValue, EndValue, (float)GetEasedProgress(time));
    }
}

public class ScaleTransform : Transform
{
    private bool valueCaptured;
    public Vector2 StartValue;
    public Vector2 EndValue;

    public override TransformMember Member => TransformMember.Scale;

    public override bool TryRetargetFrom(Transform incoming)
    {
        if (incoming is not ScaleTransform s) return false;
        EndValue = s.EndValue;
        Easing = s.Easing;
        return true;
    }

    public override void Apply(Drawable drawable, double time)
    {
        if (!valueCaptured)
        {
            StartValue = drawable.Scale;
            valueCaptured = true;
        }
        drawable.Scale = Vector2.Lerp(StartValue, EndValue, (float)GetEasedProgress(time));
    }
}

public class AlphaTransform : Transform
{
    private bool valueCaptured;
    public float StartValue;
    public float EndValue;

    public override TransformMember Member => TransformMember.Alpha;

    public override bool TryRetargetFrom(Transform incoming)
    {
        if (incoming is not AlphaTransform a) return false;
        EndValue = a.EndValue;
        Easing = a.Easing;
        return true;
    }

    public override void Apply(Drawable drawable, double time)
    {
        if (!valueCaptured)
        {
            StartValue = drawable.Alpha;
            valueCaptured = true;
        }
        float value = (float)(StartValue + (EndValue - StartValue) * GetEasedProgress(time));
        if (Precision.AlmostEquals(value, 0f))
            value = 0f;
        else if (Precision.AlmostEquals(value, 1f))
            value = 1f;
        drawable.Alpha = value;
    }
}

public class FlashColorTransform : Transform
{
    public Color FlashColour;
    private Color originalColour;
    private bool colourCaptured;

    public override void Apply(Drawable drawable, double time)
    {
        if (!colourCaptured)
        {
            originalColour = drawable.Color;
            colourCaptured = true;
        }

        double easedProgress = GetEasedProgress(time);

        // lerp from the flash colour (at progress 0) to the original colour (at progress 1).
        drawable.Color = ColorExtensions.Lerp(FlashColour, originalColour, (float)easedProgress);
    }
}

public class RotateTransform : Transform
{
    private bool valueCaptured;
    public float StartValue;
    public float EndValue;

    public override TransformMember Member => TransformMember.Rotation;

    public override bool TryRetargetFrom(Transform incoming)
    {
        if (incoming is not RotateTransform r) return false;
        EndValue = r.EndValue;
        Easing = r.Easing;
        return true;
    }

    public override void Apply(Drawable drawable, double time)
    {
        if (!valueCaptured)
        {
            StartValue = drawable.Rotation;
            valueCaptured = true;
        }
        drawable.Rotation = (float)(StartValue + (EndValue - StartValue) * GetEasedProgress(time));
    }
}

public class ColorTransform : Transform
{
    private bool valueCaptured;
    public Color StartValue;
    public Color EndValue;

    public override TransformMember Member => TransformMember.Color;

    public override bool TryRetargetFrom(Transform incoming)
    {
        if (incoming is not ColorTransform c) return false;
        EndValue = c.EndValue;
        Easing = c.Easing;
        return true;
    }

    public override void Apply(Drawable drawable, double time)
    {
        if (!valueCaptured)
        {
            StartValue = drawable.Color;
            valueCaptured = true;
        }

        double easedProgress = GetEasedProgress(time);

        drawable.Color = ColorExtensions.Lerp(StartValue, EndValue, (float)easedProgress);
    }
}

/// <summary>
/// Animates the <see cref="EdgeEffectParameters.Colour"/> of a <see cref="Container"/>'s edge effect.
/// </summary>
public class EdgeEffectColourTransform : Transform
{
    private bool valueCaptured;
    public Color StartValue;
    public Color EndValue;

    public override TransformMember Member => TransformMember.EdgeEffectColour;

    public override bool TryRetargetFrom(Transform incoming)
    {
        if (incoming is not EdgeEffectColourTransform e) return false;
        EndValue = e.EndValue;
        Easing = e.Easing;
        return true;
    }

    public override void Apply(Drawable drawable, double time)
    {
        var container = (Container)drawable;

        if (!valueCaptured)
        {
            StartValue = container.EdgeEffect.Colour;
            valueCaptured = true;
        }

        var ee = container.EdgeEffect;
        ee.Colour = ColorExtensions.Lerp(StartValue, EndValue, (float)GetEasedProgress(time));
        container.EdgeEffect = ee;
    }
}

/// <summary>
/// Animates the <see cref="EdgeEffectParameters.Radius"/> of a <see cref="Container"/>'s edge effect.
/// </summary>
public class EdgeEffectRadiusTransform : Transform
{
    private bool valueCaptured;
    public float StartValue;
    public float EndValue;

    public override TransformMember Member => TransformMember.EdgeEffectRadius;

    public override bool TryRetargetFrom(Transform incoming)
    {
        if (incoming is not EdgeEffectRadiusTransform e) return false;
        EndValue = e.EndValue;
        Easing = e.Easing;
        return true;
    }

    public override void Apply(Drawable drawable, double time)
    {
        var container = (Container)drawable;

        if (!valueCaptured)
        {
            StartValue = container.EdgeEffect.Radius;
            valueCaptured = true;
        }

        var ee = container.EdgeEffect;
        ee.Radius = (float)(StartValue + (EndValue - StartValue) * GetEasedProgress(time));
        container.EdgeEffect = ee;
    }
}

/// <summary>
/// Continuously spins the drawable at a constant angular velocity (degrees per revolution / duration).
/// Designed to be used with <see cref="TransformExtensions.Spin"/> and looped.
/// </summary>
public class SpinTransform : Transform
{
    /// <summary>Duration of one full 360° revolution in ms.</summary>
    public double RevolutionDuration { get; init; }

    public override void Apply(Drawable drawable, double time)
    {
        if (time < StartTime) return;
        if (RevolutionDuration <= 0) return;

        double elapsed = time - StartTime;
        drawable.Rotation = (float)(elapsed / RevolutionDuration * 360.0) % 360f;
    }
}

/// <summary>
/// Animates a <see cref="Container"/>'s size toward an auto-size target computed from its children.
/// Unlike <see cref="ResizeTransform"/>, the target can be re-pointed mid-flight (when children
/// change) without restarting the animation from a hard snap — the start value is re-captured from
/// the container's current size and the remaining duration is replayed toward the new target.
/// </summary>
public class AutoSizeTransform : Transform
{
    public Vector2 StartValue;
    public Vector2 EndValue;

    public override void Apply(Drawable drawable, double time)
    {
        if (drawable is not Container container)
            return;

        container.ApplyAutoSize(Vector2.Lerp(StartValue, EndValue, (float)GetEasedProgress(time)));
    }
}

/// <summary>
/// A zero-duration transform that fires completion/abort callbacks on behalf of a <see cref="TransformSequence{T}"/>.
/// </summary>
public class CallbackTransform<T> : Transform where T : Drawable
{
    public T Target { get; init; } = null!;
    public Action<T>? OnComplete { get; init; }
    public Action<T>? OnAbort { get; init; }

    private bool fired;

    public override void Apply(Drawable drawable, double time)
    {
        if (!fired && time >= EndTime)
        {
            fired = true;
            OnComplete?.Invoke(Target);
        }
    }
}
