using Android.App;
using Android.Content.PM;
using Sakura.Framework;
using Sakura.Framework.Android;

namespace MyApp.Android;

[Activity(
    Label = "MyApp",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleInstance,
    Theme = "@android:style/Theme.Black.NoTitleBar",
    // Rotating, resizing or folding would otherwise restart the activity; the app lays itself out again instead.
    ConfigurationChanges = ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation | ConfigChanges.Orientation
                           | ConfigChanges.ScreenLayout | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.UiMode
                           | ConfigChanges.Density)]
public class MainActivity : SakuraActivity
{
    protected override App CreateApp() => new MyAppApp();
}
