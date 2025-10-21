// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using System.Numerics;
using Sakura.Framework.Extensions.ColorExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;

namespace Sakura.Framework.Graphics.Transforms;

public abstract class Transform : ITransform
{
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public Easing Easing { get; set; }
    public bool IsLooping { get; set; }

    public abstract void Apply(Drawable drawable, double time);

    protected double GetEasedProgress(double time)
    {
        if (time < StartTime) return 0;

        double duration = EndTime - StartTime;
        if (duration <= 0) return 1; // Instant transform, always at end

        double progress;
        if (IsLooping)
        {
            // Calculate progress within the current loop
            double timeInLoop = (time - StartTime) % duration;
            progress = timeInLoop / duration;
        }
        else
        {
            // Not looping, cap at end time
            time = Math.Min(time, EndTime);
            progress = (time - StartTime) / duration;
        }

        return EasingFunctions.Apply(Easing, progress);
    }
}

public class MoveTransform : Transform
{
    private bool valueCaptured;
    public Vector2 StartValue;
    public Vector2 EndValue;
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
    public override void Apply(Drawable drawable, double time)
    {
        if (!valueCaptured)
        {
            StartValue = drawable.Scale;
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
    public override void Apply(Drawable drawable, double time)
    {
        if (!valueCaptured)
        {
            StartValue = drawable.Alpha;
            valueCaptured = true;
        }
        drawable.Alpha = (float)(StartValue + (EndValue - StartValue) * GetEasedProgress(time));
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
