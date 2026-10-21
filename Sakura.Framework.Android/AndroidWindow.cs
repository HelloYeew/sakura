// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Sakura.Framework.Graphics.Primitives;
using Sakura.Framework.Platform;

namespace Sakura.Framework.Android;

/// <summary>
/// The window an app draws to on Android: SDL's, with the safe area taken from what covers the content.
/// </summary>
internal class AndroidWindow : SDLWindow
{
    private readonly SakuraActivity activity;

    public AndroidWindow(SakuraActivity activity)
    {
        this.activity = activity;
        activity.CoveredInsetsChanged += InvalidateSafeArea;
    }

    protected override MarginPadding? GetPlatformSafeAreaInsets() => activity.CoveredInsets;
}
