// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using System;
using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Textures;

/// <summary>
/// Reads the pixels of an image that was just decoded, before they are uploaded.
/// </summary>
/// <param name="rgba">The decoded pixels, RGBA with 8 bits per channel, row by row with no padding.</param>
/// <param name="width">The decoded width, after any reduction.</param>
/// <param name="height">The decoded height, after any reduction.</param>
public delegate void PixelInspector(ReadOnlySpan<byte> rgba, int width, int height);

/// <summary>
/// How <see cref="ITextureManager.CreateFromStream"/> should decode, label and share a texture.
/// </summary>
/// <remarks>
/// This exists so a game or an application does not have to own the steps between "I have an encoded image" and "I have a
/// GPU texture". Doing it by hand means a direct dependency on an imaging library, a hand-rolled
/// downscale, an unpooled pixel buffer, and a texture the framework's tooling cannot see — every one of
/// which was a real cost in the game this was built for.
/// </remarks>
public readonly struct TextureCreationOptions
{
    /// <summary>
    /// How far to reduce the image while decoding it. Defaults to a full-resolution decode.
    /// </summary>
    public ImageLoadOptions Decode { get; init; }

    /// <summary>
    /// A human-readable label for the texture viewer and statistics. Worth setting: without it the
    /// viewer can only show dimensions.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// When set, the texture is reference counted under this key and reused by every caller that asks
    /// for the same key, instead of being decoded and uploaded once per display.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hit skips reading the stream entirely, so the caller pays nothing beyond opening it. Balance
    /// every call that returns a shared texture with <see cref="ITextureManager.ReleaseSharedTexture"/>
    /// for the same key, and do not dispose the returned texture directly — someone else may still be
    /// drawing it.
    /// </para>
    /// <para>
    /// The key must identify the <em>decoded result</em>, not just the source image: two displays of the
    /// same image at different <see cref="Decode"/> sizes are different textures and must not collide.
    /// <see cref="ShareKeyFor"/> builds a correct key.
    /// </para>
    /// </remarks>
    public string? ShareKey { get; init; }

    /// <summary>
    /// Called with the decoded pixels before they are uploaded, for anything that wants to learn something
    /// from the image (an average or dominant colour, say) without decoding it a second time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs on the loading thread, after <see cref="Decode"/> has reduced the image, so it sees the same
    /// pixels the texture will hold. The span is only valid for the duration of the call: copy out what you
    /// need, and keep the work short, since the upload waits for it.
    /// </para>
    /// <para>
    /// Not called when nothing is decoded: on a <see cref="ShareKey"/> hit, or by a texture manager that
    /// does not decode at all (the headless one). Callers that need the result every time must cache it
    /// themselves. An exception thrown here is logged and does not stop the texture being created.
    /// </para>
    /// </remarks>
    public PixelInspector? InspectPixels { get; init; }

    /// <summary>
    /// Decode at full resolution with no sharing.
    /// </summary>
    public static TextureCreationOptions Default => new TextureCreationOptions
    {
        Decode = ImageLoadOptions.FullSize
    };

    /// <summary>
    /// Builds a share key that distinguishes the same source decoded at different sizes or fill modes,
    /// which produce different pixels and must not be shared with each other.
    /// </summary>
    /// <param name="sourceKey">Identifies the source image (a path, or a model's ID).</param>
    /// <param name="targetSize">The decode target size, or null for a full-resolution decode.</param>
    /// <param name="fillMode">The fill mode the decode was cropped for.</param>
    public static string ShareKeyFor(string sourceKey, Vector2? targetSize, TextureFillMode fillMode)
    {
        string size = targetSize is { } target
            ? $"{(int)target.X}x{(int)target.Y}"
            : "full";

        return $"{sourceKey}|{size}|{fillMode}";
    }
}
