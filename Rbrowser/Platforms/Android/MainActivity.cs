using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.App.Roles;
using Android.Views;
namespace Rbrowser;
[Activity(Theme = "@style/Rbrowser.MainTheme", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.Landscape, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(["android.intent.action.MAIN"], Categories = ["android.intent.category.LEANBACK_LAUNCHER"])]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryAppBrowser, Intent.CategoryDefault])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataSchemes = ["http", "https"])]
public class MainActivity : MauiAppCompatActivity
{
    string? pendingLink;
    internal const int BrowserRoleRequest = 702;

    protected override void OnCreate(Android.OS.Bundle? state)
    {
        CaptureLink(Intent);
        base.OnCreate(state);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        Window?.SetFlags(WindowManagerFlags.Fullscreen, WindowManagerFlags.Fullscreen);
    }

    void CaptureLink(Intent? intent)
    {
        var url = intent?.DataString;
        if (intent?.Action == Intent.ActionView && Core.Urls.IsWeb(url))
            pendingLink = url;
    }

    internal string? TakeLink()
    {
        var url = pendingLink;
        pendingLink = null;
        return url;
    }

    internal bool IsDefaultBrowser() =>
        OperatingSystem.IsAndroidVersionAtLeast(29) &&
        GetSystemService(RoleService) is RoleManager roles &&
        roles.IsRoleAvailable(RoleManager.RoleBrowser) &&
        roles.IsRoleHeld(RoleManager.RoleBrowser);

    internal bool RequestDefaultBrowser()
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29) &&
                GetSystemService(RoleService) is RoleManager roles &&
                roles.IsRoleAvailable(RoleManager.RoleBrowser))
            {
                StartActivityForResult(roles.CreateRequestRoleIntent(RoleManager.RoleBrowser), BrowserRoleRequest);
                return true;
            }
        }
        catch (ActivityNotFoundException) { }
        catch (Java.Lang.SecurityException) { }

        try
        {
            StartActivity(new Intent(global::Android.Provider.Settings.ActionManageDefaultAppsSettings));
            return true;
        }
        catch (ActivityNotFoundException) { return false; }
        catch (Java.Lang.SecurityException) { return false; }
    }
    public override bool DispatchKeyEvent(KeyEvent? e) => e != null && Platforms.Android.TvBrowser.Current?.Remote(e) == true || base.DispatchKeyEvent(e);
    protected override void OnPause() { Platforms.Android.TvBrowser.Current?.Pause(); base.OnPause(); }
    protected override void OnResume() { base.OnResume(); Platforms.Android.TvBrowser.Current?.Resume(); }
    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        CaptureLink(intent);
        Platforms.Android.TvBrowser.Current?.OpenPendingLink();
#if TV_DIAGNOSTICS
        var count = intent?.GetIntExtra("rb-history-preview", 0) ?? 0;
        if (count > 0) Platforms.Android.TvBrowser.Current?.PreviewHistory(count);
#endif
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, global::Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode,resultCode,data);
        if (requestCode == BrowserRoleRequest) Platforms.Android.TvBrowser.Current?.RefreshDefaultBrowserButton();
        if(requestCode==701){var value=data?.GetStringArrayListExtra(global::Android.Speech.RecognizerIntent.ExtraResults)?.FirstOrDefault(s=>!string.IsNullOrWhiteSpace(s));if(resultCode==Result.Ok&&!string.IsNullOrWhiteSpace(value))Platforms.Android.TvBrowser.Current?.Speech(value);else Platforms.Android.TvBrowser.Current?.SpeechCancelled();}
    }
}
