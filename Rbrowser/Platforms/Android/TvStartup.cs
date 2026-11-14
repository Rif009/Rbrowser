using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Rbrowser.Core;
using Color = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;
using RectF = Android.Graphics.RectF;
using View = Android.Views.View;

namespace Rbrowser.Platforms.Android;

public sealed partial class TvBrowser
{
    static readonly Color VideoAccent = Color.Rgb(205, 194, 255);
    static readonly Color VideoBorder = Color.Rgb(155, 136, 231);
    static readonly Color VideoSurface = Color.Rgb(46, 37, 74);
    FrameLayout? startupSplash;
    TextView? defaultBrowserButton;

    void ShowStartupSplash()
    {
        var splash = new FrameLayout(Context);
        splash.SetBackgroundColor(bg);
        var brand = Column();
        brand.SetGravity(GravityFlags.Center);
        var mark = new ImageView(Context);
        mark.SetImageResource(Resource.Drawable.rbrowser_icon);
        splash.AddView(new StartupOrbit(Context, brand, mark), new LayoutParams(-1, -1));
        brand.AddView(mark, new LinearLayout.LayoutParams(Dp(104), Dp(104)));
        brand.AddView(Label("Rbrowser", 34), new LinearLayout.LayoutParams(-2, -2) { TopMargin = Dp(26) });
        splash.AddView(brand, new LayoutParams(-2, -2, GravityFlags.Center) { BottomMargin = Dp(34) });

        var signature = Label("by rif009", 17, VideoAccent);
        splash.AddView(signature, new LayoutParams(-2, -2, GravityFlags.Bottom | GravityFlags.CenterHorizontal)
        {
            BottomMargin = Dp(34)
        });
        startupSplash = splash;
        AddView(splash, new LayoutParams(-1, -1));
    }

    void HideStartupSplash()
    {
        if (startupSplash is not { } splash) return;
        startupSplash = null;
        RemoveView(splash);
    }

    sealed class StartupOrbit(Context context, LinearLayout brand, ImageView mark) : View(context)
    {
        readonly long started = SystemClock.UptimeMillis();
        readonly Paint paint = new(PaintFlags.AntiAlias) { StrokeCap = Paint.Cap.Round };
        readonly RectF ring = new();

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            var density = Resources?.DisplayMetrics?.Density ?? 1;
            var centerX = brand.Left + mark.Left + mark.Width / 2f;
            var centerY = brand.Top + mark.Top + mark.Height / 2f;
            var radius = 66 * density;
            var angle = (SystemClock.UptimeMillis() - started) * .16f % 360;
            ring.Set(centerX - radius, centerY - radius, centerX + radius, centerY + radius);
            paint.SetStyle(Paint.Style.Stroke);
            paint.StrokeWidth = 2 * density;
            paint.Color = VideoSurface;
            canvas.DrawOval(ring, paint);
            paint.Color = VideoAccent;
            canvas.DrawArc(ring, angle, 85, false, paint);
            paint.Color = VideoBorder;
            canvas.DrawArc(ring, angle + 180, 38, false, paint);
            if (IsShown) PostInvalidateOnAnimation();
        }
    }

    internal void OpenPendingLink()
    {
        if (!ready || stopped || Activity is not MainActivity activity || activity.TakeLink() is not { } url)
            return;
        if (nativePlayerPanel != null) CloseNativePlayer(false);
        Open(url);
    }

    internal void RefreshDefaultBrowserButton()
    {
        if (screen != "settings" || defaultBrowserButton == null) return;
        var selected = Activity is MainActivity activity && activity.IsDefaultBrowser();
        var label = selected ? I18n.Text("Rbrowser уже выбран по умолчанию") : I18n.Text("Сделать браузером по умолчанию");
        defaultBrowserButton.Text = label;
        defaultBrowserButton.ContentDescription = label;
    }

    void ChooseDefaultBrowser()
    {
        if (Activity is MainActivity activity)
        {
            if (activity.IsDefaultBrowser()) return;
            if (activity.RequestDefaultBrowser()) return;
        }
        Info(I18n.Text("Браузер по умолчанию"), I18n.Text("На этом устройстве недоступен системный выбор браузера."));
    }
}
