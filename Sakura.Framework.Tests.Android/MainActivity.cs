// This code is part of the Sakura framework project. Licensed under the MIT License.
// See the LICENSE file for full license text.

using Android.App;
using Android.Content.PM;
using Sakura.Framework.Android;

namespace Sakura.Framework.Tests.Android;

[Activity(
    Label = "Sakura Tests",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleInstance,
    Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
    // Rotating, resizing or folding would otherwise restart the activity; the app lays itself out again instead.
    ConfigurationChanges = ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation | ConfigChanges.Orientation
                           | ConfigChanges.ScreenLayout | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode
                           | ConfigChanges.Density)]
public class MainActivity : SakuraActivity
{
    // There is no managed entry assembly on Android to find the tests in, so they're named.
    protected override App CreateApp() => new TestApp(typeof(TestApp).Assembly);
}
