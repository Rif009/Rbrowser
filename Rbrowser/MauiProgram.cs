using Microsoft.Maui.Handlers;
namespace Rbrowser;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>().ConfigureMauiHandlers(h => h.AddHandler<BrowserHost, BrowserHostHandler>()).Build();
}
public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new ContentPage { BackgroundColor = Color.FromArgb("#171B20"), Content = new BrowserHost() });
}
public sealed class BrowserHost : View { }
public sealed class BrowserHostHandler : ViewHandler<BrowserHost, Platforms.Android.TvBrowser>
{
    public BrowserHostHandler() : base(ViewHandler.ViewMapper) { }
    protected override Platforms.Android.TvBrowser CreatePlatformView() => new(Context);
    protected override void DisconnectHandler(Platforms.Android.TvBrowser platformView) { platformView.Shutdown(); base.DisconnectHandler(platformView); }
}
