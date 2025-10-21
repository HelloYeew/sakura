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
    public double StartTime { get; set;  }
    public double EndTime { get; set;  }
    public EasingType Easing { get; set;  }

    public abstract void Apply(Drawable drawable, double time);

    protected double GetEasedProgress(double time)
    {
        if (time < StartTime) return 0;
        time = Math.Min(time, EndTime);
        double duration = EndTime - StartTime;
        double progress = duration > 0 ? (time - StartTime) / duration : 1;
        return EasingFunctions.Apply(Easing, progress);
    }
}

public class MoveTransform : Transform
{
    public Vector2 StartValue;
    public Vector2 EndValue;
    public override void Apply(Drawable d, double time)
    {
        d.Position = Vector2.Lerp(StartValue, EndValue, (float)GetEasedProgress(time));
    }
}

public class ResizeTransform : Transform
{
    public Vector2 StartValue;
    public Vector2 EndValue;
    public override void Apply(Drawable d, double time)
    {
        d.Size = Vector2.Lerp(StartValue, EndValue, (float)GetEasedProgress(time));
    }
}

public class ScaleTransform : Transform
{
    public Vector2 StartValue;
    public Vector2 EndValue;
    public override void Apply(Drawable d, double time)
    {
        d.Scale = Vector2.Lerp(StartValue, EndValue, (float)GetEasedProgress(time));
    }
}

public class AlphaTransform : Transform
{
    public float StartValue;
    public float EndValue;
    public override void Apply(Drawable d, double time)
    {
        d.Alpha = (float)(StartValue + (EndValue - StartValue) * GetEasedProgress(time));
    }
}

public class FlashColorTransform : Transform
{
    public Color FlashColour;
    private Color originalColour;
    private bool colourCaptured;

    public override void Apply(Drawable d, double time)
    {
        // On the first application, capture the drawable's current colour.
        // This will be our target to fade back to.
        if (!colourCaptured)
        {
            originalColour = d.Color;
            colourCaptured = true;
        }

        // Eased progress goes from 0 to 1 over the duration.
        double easedProgress = GetEasedProgress(time);

        // lerp from the flash colour (at progress 0) to the original colour (at progress 1).
        d.Color = ColorExtensions.Lerp(FlashColour, originalColour, (float)easedProgress);
    }
}
