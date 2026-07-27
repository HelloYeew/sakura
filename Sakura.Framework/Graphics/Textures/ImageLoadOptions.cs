// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Controls how an <see cref="IImageLoader"/> reduces an image while decoding it, so a large source
/// is never decoded, allocated and uploaded at far more pixels than can actually be shown.
/// </summary>
public readonly struct ImageLoadOptions
{
    /// <summary>
    /// Approximate on-screen size (px) the image will be displayed at, used to cap the decoded
    /// resolution. <c>null</c> decodes at full resolution.
    /// </summary>
    public Vector2? TargetSize { get; }

    /// <summary>
    /// How the image will fill its target box on screen.
    /// </summary>
    /// <remarks>
    /// Only <see cref="TextureFillMode.Fill"/> changes which pixels are kept: the centre band that a
    /// Fill actually displays is cropped before scaling, so the parts clipped off screen are never
    /// decoded or uploaded. Every other mode scales the whole image to fit inside
    /// <see cref="TargetSize"/>, preserving aspect ratio.
    /// </remarks>
    public TextureFillMode FillMode { get; }

    public ImageLoadOptions(Vector2? targetSize, TextureFillMode fillMode = TextureFillMode.Fit)
    {
        TargetSize = targetSize;
        FillMode = fillMode;
    }

    /// <summary>
    /// Decode at the source's full resolution.
    /// </summary>
    public static ImageLoadOptions FullSize => new ImageLoadOptions(null);

    /// <summary>
    /// Cap the longest edge at <paramref name="maxDimension"/> px, preserving aspect ratio. Values
    /// &lt;= 0 mean "no limit".
    /// </summary>
    public static ImageLoadOptions MaxDimension(int maxDimension) => new ImageLoadOptions(maxDimension > 0 ? new Vector2(maxDimension) : null);

    /// <summary>
    /// Reduce to (at most) <paramref name="targetSize"/>, cropping the center band to the target
    /// aspect first, the correct choice for a texture drawn with <see cref="TextureFillMode.Fill"/>.
    /// </summary>
    public static ImageLoadOptions FillTarget(Vector2 targetSize) => new ImageLoadOptions(targetSize, TextureFillMode.Fill);

    /// <summary>
    /// Whether a usable target size is set (at least one pixel on both axes).
    /// </summary>
    internal bool HasTarget => TargetSize is
    {
        X: >= 1,
        Y: >= 1
    };

    /// <summary>
    /// Whether the centre band should be cropped to the target aspect before scaling.
    /// </summary>
    internal bool CropToFill => FillMode == TextureFillMode.Fill;
}
