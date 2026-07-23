// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Extensions.DrawableExtensions;
using Sakura.Framework.Graphics.Colors;
using Sakura.Framework.Graphics.Drawables;
using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Graphics.Transforms;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Extensions.TransformSequenceExtensions;

/// <summary>
/// Extension methods that mirror <c>TransformExtensions</c> but operate on a
/// <see cref="TransformSequence{T}"/>, so every call returns the sequence for continued chaining.
/// <example><code>
/// sprite.TransformSequence()
///       .FadeIn(200)
///       .Then()
///       .ScaleTo(1.2f, 300, Easing.OutBack)
///       .Then(100)
///       .FadeOut(200)
///       .OnComplete(s => s.Expire());
///
/// sprite.TransformSequence()
///       .FadeTo(0.25f)
///       .Then()
///       .FadeTo(1f, 1000)
///       .Loop(1250);
/// </code></example>
/// </summary>
public static class TransformSequenceExtensions
{
    public static TransformSequence<T> MoveTo<T>(this TransformSequence<T> seq, Vector2 position, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.MoveTo(position, duration, easing));

    public static TransformSequence<T> MoveToX<T>(this TransformSequence<T> seq, float x, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.MoveToX(x, duration, easing));

    public static TransformSequence<T> MoveToY<T>(this TransformSequence<T> seq, float y, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.MoveToY(y, duration, easing));

    public static TransformSequence<T> MoveToOffset<T>(this TransformSequence<T> seq, Vector2 offset, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.MoveToOffset(offset, duration, easing));

    public static TransformSequence<T> ResizeTo<T>(this TransformSequence<T> seq, Vector2 size, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.ResizeTo(size, duration, easing));

    public static TransformSequence<T> ResizeTo<T>(this TransformSequence<T> seq, float size, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.ResizeTo(size, duration, easing));

    public static TransformSequence<T> ScaleTo<T>(this TransformSequence<T> seq, Vector2 scale, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.ScaleTo(scale, duration, easing));

    public static TransformSequence<T> ScaleTo<T>(this TransformSequence<T> seq, float scale, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.ScaleTo(scale, duration, easing));

    public static TransformSequence<T> RotateTo<T>(this TransformSequence<T> seq, float rotation, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.RotateTo(rotation, duration, easing));

    public static TransformSequence<T> Spin<T>(this TransformSequence<T> seq, double revolutionDuration, RotationDirection direction = RotationDirection.Clockwise) where T : Drawable
        => seq.Append(d => d.Spin(revolutionDuration, direction));

    public static TransformSequence<T> FadeTo<T>(this TransformSequence<T> seq, float alpha, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.FadeTo(alpha, duration, easing));

    public static TransformSequence<T> FadeIn<T>(this TransformSequence<T> seq, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.FadeIn(duration, easing));

    public static TransformSequence<T> FadeOut<T>(this TransformSequence<T> seq, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.FadeOut(duration, easing));

    public static TransformSequence<T> FadeInFromZero<T>(this TransformSequence<T> seq, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.FadeInFromZero(duration, easing));

    public static TransformSequence<T> FadeToColor<T>(this TransformSequence<T> seq, Color color, double duration = 0, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.FadeToColor(color, duration, easing));

    public static TransformSequence<T> FlashColor<T>(this TransformSequence<T> seq, Color flashColor, double duration, EasingFunction easing = default) where T : Drawable
        => seq.Append(d => d.FlashColor(flashColor, duration, easing));
}
