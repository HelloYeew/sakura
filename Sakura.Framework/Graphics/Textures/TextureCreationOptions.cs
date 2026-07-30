// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Maths;

namespace Sakura.Framework.Graphics.Textures;

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
