using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Speech;
using Android.Views;
using Android.Views.InputMethods;
using Android.Webkit;
using Android.Widget;
using Rbrowser.Core;
using System.Text.Json;
using AView = Android.Views.View;
using Color = Android.Graphics.Color;
using Orientation = Android.Widget.Orientation;
using WebView = Android.Webkit.WebView;
using Button = Android.Widget.Button;
using ImageButton = Android.Widget.ImageButton;
using ScrollView = Android.Widget.ScrollView;
using Paint = Android.Graphics.Paint;
using Tab = Rbrowser.Core.Tab;
using AndroidX.WebKit;

namespace Rbrowser.Platforms.Android;

public sealed partial class TvBrowser : FrameLayout
{
    public static TvBrowser? Current { get; private set; }
    readonly Color bg = Color.Rgb(23, 27, 32), surface = Color.Rgb(35, 41, 49), text = Color.Rgb(237, 241, 245), muted = Color.Rgb(165, 175, 189), accent = Color.Rgb(162, 200, 213);
    readonly StateStore store = new(FileSystem.AppDataDirectory);
    readonly FilterService filters = new();
    BrowserState state = new();
    readonly LinearLayout chrome;
    readonly FrameLayout content;
    readonly TextView hint;
    LinearLayout? toolbar;
    LinearLayout? toolbarIcons;
    ImageButton? toolbarBack;
    ImageButton? cursorButton,desktopButton,findVideoButton;
    string? settingsReturnTab;
    TextView? address;
    AView? homeFocus;
    readonly Dictionary<string, WebView> live = [];
    readonly Dictionary<string, string> pageUrls = [];
    readonly Dictionary<string,AView> favoriteViews = [];
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> blocked = [];
    string? active;
    string navigationScript = "";
    string twitchScript = "";
    string mediaScript = "";
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,VideoCatalog> mediaCatalogs=[];
    string screen = "home";
    bool editing, cursor, inFrame, dialogOpen, ready, stopped,videoFullscreen;
    float cursorX, cursorY;
    readonly Pointer pointer;
    AView? fullscreen;
    WebChromeClient.ICustomViewCallback? fullscreenCallback;
    long lastOk;
    Activity Activity => Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? throw new InvalidOperationException(I18n.Text("Нет активного окна"));
    public new Context Context => base.Context!;
    public TvBrowser(Context context) : base(context)
    {
        Current = this; SetBackgroundColor(bg); Focusable = false;
        chrome = Column(); AddView(chrome, new LayoutParams(-1, -1));
        content = new FrameLayout(context) { Focusable = false };
        hint = Label(I18n.Text("Загрузка Rbrowser…"), 12, muted);
        chrome.AddView(content, new LinearLayout.LayoutParams(-1, 0, 1));
        hint.SetPadding(Dp(30), Dp(8), Dp(30), Dp(10)); chrome.AddView(hint);
        pointer = new Pointer(context, accent) { Visibility = ViewStates.Gone, Clickable = false, Focusable = false };
        filters.Changed += FiltersChanged;
        ShowStartupSplash();
        _ = Initialize();
    }
    protected override void OnMeasure(int widthMeasureSpec,int heightMeasureSpec)
    {
        // MAUI first measures native handlers with AT_MOST; this host fills its assigned viewport.
        // Measure children at that full size so later native arrangement cannot leave them wrap-sized.
        var width=MeasureSpec.GetMode(widthMeasureSpec)==MeasureSpecMode.Unspecified?widthMeasureSpec:MeasureSpec.MakeMeasureSpec(MeasureSpec.GetSize(widthMeasureSpec),MeasureSpecMode.Exactly);
        var height=MeasureSpec.GetMode(heightMeasureSpec)==MeasureSpecMode.Unspecified?heightMeasureSpec:MeasureSpec.MakeMeasureSpec(MeasureSpec.GetSize(heightMeasureSpec),MeasureSpecMode.Exactly);
        base.OnMeasure(width,height);
    }
    async Task Initialize()
    {
        try
        {
            state = await store.Load();cursor=state.CursorMode;
            ApplyInterfaceLanguage();
            using var stream = await FileSystem.OpenAppPackageFileAsync("navigation.js"); navigationScript = await new StreamReader(stream).ReadToEndAsync();
            using(var twitch=await FileSystem.OpenAppPackageFileAsync("twitch/twitch-adblock.js"))twitchScript=await new StreamReader(twitch).ReadToEndAsync();
            using(var media=await FileSystem.OpenAppPackageFileAsync("media-detector.js"))mediaScript=await new StreamReader(media).ReadToEndAsync();
            await filters.Load();
            if (stopped) return;
            ready = true; ShowHome(); OpenPendingLink();
        }
        catch (Exception ex) { hint.Text = I18n.Text("Не удалось запустить: ") + ex.Message; }
        finally { HideStartupSplash(); }
    }
    void FiltersChanged() => Post(() => { if (!stopped && screen == "settings") ShowSettings(); });
    int Dp(float n) => (int)(n * (Resources?.DisplayMetrics?.Density ?? 1) + .5f);
    float PageWidth => (Resources?.DisplayMetrics?.WidthPixels??1920)/(Resources?.DisplayMetrics?.Density??2)-72;
    LinearLayout Column() => new(Context) { Orientation = Orientation.Vertical, Focusable = false };
    LinearLayout Row() {var row=new LinearLayout(Context) { Orientation = Orientation.Horizontal, Focusable = false };row.SetGravity(GravityFlags.CenterVertical);return row;}
    TextView Label(string value, float size = 16, Color? color = null)
    {
        var label = new TextView(Context) { Text = value, TextSize = size, Focusable = false };
        label.SetTextColor(color ?? text); label.SetSingleLine(false); return label;
    }
    GradientDrawable Surface(bool focused = false, bool selected = false)
    {
        var drawable = new GradientDrawable(); drawable.SetColor(focused ? Color.Rgb(48, 65, 78) : surface);
        drawable.SetCornerRadius(Dp(14)); drawable.SetStroke(Dp(focused ? 3 : 1), focused || selected ? accent : Color.Rgb(53, 62, 74)); return drawable;
    }
    void FocusStyle(AView view, string description, bool selected = false)
    {
        view.Id = AView.GenerateViewId(); view.Focusable = true; view.FocusableInTouchMode = true;
        view.ContentDescription = description; view.Selected=selected;view.Background = Surface(false, selected);
        view.FocusChange += (_, e) => { view.Background = Surface(e.HasFocus, view.Selected); if (e.HasFocus) {hint.Text = view.ContentDescription + I18n.Text("   ·   OK — выбрать   ·   Назад — вернуться");view.Post(()=>{if(view.IsAttachedToWindow&&view.HasFocus)view.RequestRectangleOnScreen(new global::Android.Graphics.Rect(0,-Dp(8),view.Width,view.Height+Dp(8)),false);});} };
    }
    Button Button(string title, Action action, string? description = null)
    {
        var b = new Button(Context) { Text = title, TextSize = 14, Gravity = GravityFlags.Center, TransformationMethod = null };
        b.BackgroundTintList=null;b.StateListAnimator=null;b.Elevation=0;
        b.SetTextColor(text); b.SetPadding(Dp(15), Dp(9), Dp(15), Dp(9)); b.SetMinHeight(Dp(46));
        FocusStyle(b, description ?? title); b.Click += (_, _) => action(); return b;
    }
    ImageButton Icon(string name, string description, Action action, bool selected = false)
    {
        var b = new ImageButton(Context); b.SetImageDrawable(new IconDrawable(name, selected ? accent : text)); b.SetPadding(Dp(13),Dp(13),Dp(13),Dp(13));
        FocusStyle(b, description, selected); b.Click += (_, _) => action(); return b;
    }
    LinearLayout FavoriteCard(Bookmark favorite)
    {
        var card=Column();card.SetGravity(GravityFlags.Center);card.SetPadding(Dp(10),Dp(10),Dp(10),Dp(10));card.Clickable=true;
        var initial=string.IsNullOrWhiteSpace(favorite.Title)?"R":System.Globalization.StringInfo.GetNextTextElement(favorite.Title).ToUpperInvariant();
        var logo=Label(favorite.Title.StartsWith("Twitch",StringComparison.OrdinalIgnoreCase)?"twitch":initial,30,accent);logo.Gravity=GravityFlags.Center;
        var title=Label(favorite.Title,16);title.Gravity=GravityFlags.Center;title.SetSingleLine(true);title.Ellipsize=global::Android.Text.TextUtils.TruncateAt.End;
        var host=Label(new Uri(favorite.Url).Host,12,muted);host.Gravity=GravityFlags.Center;host.SetSingleLine(true);host.Ellipsize=global::Android.Text.TextUtils.TruncateAt.Middle;
        card.AddView(logo);card.AddView(title);card.AddView(host);FocusStyle(card,(editing?I18n.Text("Изменить "):I18n.Text("Открыть "))+favorite.Title);
        card.Click+=(_,_)=>{if(editing)EditBookmark(favorite);else Open(favorite.Url);};return card;
    }
    void AddIcon(LinearLayout row, ImageButton icon)
    {
        row.AddView(icon, new LinearLayout.LayoutParams(Dp(48),Dp(48)) { MarginEnd = Dp(8) });
    }
    LinearLayout Heading(LinearLayout page, string title)
    {
        var row = Row(); row.SetPadding(0, Dp(18), 0, Dp(12)); row.AddView(Label(title,18),new LinearLayout.LayoutParams(0,-2,1));page.AddView(row);return row;
    }
    LinearLayout Page(string title)
    {
        HideVideoNotice();
        ExitVideoFullscreen();inFrame=false;hint.Visibility=ViewStates.Visible;
        HideFullscreen(); PauseWeb(); DetachWeb(); pointer.Visibility = ViewStates.Gone;
        content.RemoveAllViews(); toolbar = null; address = null;cursorButton=null;desktopButton=null;findVideoButton=null;
        var scroll = new ScrollView(Context) { FillViewport = true, Focusable = false };scroll.SetClipToPadding(false);
        content.AddView(scroll, new LayoutParams(-1,-1));
        var page = Column(); page.SetPadding(Dp(36),Dp(20),Dp(36),Dp(20));scroll.AddView(page,new ScrollView.LayoutParams(-1,-2));
        var brand = Row(); var mark = new ImageView(Context){ContentDescription="Rbrowser"};mark.SetImageResource(Resource.Drawable.rbrowser_icon);
        brand.AddView(mark,new LinearLayout.LayoutParams(Dp(44),Dp(44)){MarginEnd=Dp(10)});brand.AddView(Label("browser",23),new LinearLayout.LayoutParams(0,-2,1));
        AddIcon(brand, Icon("history",I18n.Text("История посещений"),ShowHistory));
        AddIcon(brand, Icon("settings",I18n.Text("Настройки Rbrowser"),ShowSettings));page.AddView(brand);
        if(title.Length>0){var heading=Label(title,28);heading.SetPadding(0,Dp(22),0,Dp(8));page.AddView(heading);}
        return page;
    }
    void FocusLater(AView? view) { if(view!=null) Post(()=>view.RequestFocus()); }
    public void ShowHome()
    {
        screen="home";hint.Visibility=ViewStates.Visible;favoriteViews.Clear();var page=Page(I18n.Text("Что будем смотреть?"));
        var search=Row();search.SetPadding(0,Dp(10),0,Dp(6));var input=Button(I18n.Text("Поиск или адрес сайта"),()=>Search());search.AddView(input,new LinearLayout.LayoutParams(0,Dp(52),1){MarginEnd=Dp(10)});AddIcon(search,Icon("mic",I18n.Text("Голосовой ввод"),Voice));page.AddView(search);homeFocus=input;
        var heading=Heading(page,I18n.Text("Избранное"));heading.AddView(Button(editing?I18n.Text("Готово"):I18n.Text("Редактировать"),()=>{editing=!editing;ShowHome();},I18n.Text("Редактировать избранное: название, адрес, порядок, удаление")));
        var columns=Math.Clamp((int)(PageWidth/204),1,4);var tileWidth=Dp((PageWidth-columns*14)/columns);
        var grid=new GridLayout(Context){ColumnCount=columns,RowCount=Math.Max(1,(state.Favorites.Count+columns-1)/columns),Focusable=false};page.AddView(grid);
        foreach(var favorite in state.Favorites.ToList())
        {
            var tile=Column();var b=FavoriteCard(favorite);favoriteViews[favorite.Id]=b;tile.AddView(b,new LinearLayout.LayoutParams(-1,Dp(138)));
            if(editing)
            {
                var tools=Row();
                AddIcon(tools,Icon("back",I18n.Text("Переместить ")+favorite.Title+I18n.Text(" влево"),()=>MoveBookmark(favorite,-1)));
                AddIcon(tools,Icon("clear",I18n.Text("Удалить ")+favorite.Title,()=>{var index=state.Favorites.IndexOf(favorite);state.Favorites.Remove(favorite);Save();ShowHome();var next=state.Favorites.ElementAtOrDefault(Math.Max(0,Math.Min(index,state.Favorites.Count-1)));if(next!=null)FocusLater(favoriteViews.GetValueOrDefault(next.Id));}));
                AddIcon(tools,Icon("forward",I18n.Text("Переместить ")+favorite.Title+I18n.Text(" вправо"),()=>MoveBookmark(favorite,1)));tile.AddView(tools);
            }
            if(homeFocus==input&&!editing)homeFocus=b;
            grid.AddView(tile,new GridLayout.LayoutParams { Width=tileWidth,Height=-2,LeftMargin=0,RightMargin=Dp(14),BottomMargin=Dp(12) });
        }
        if(state.Favorites.Count==0)page.AddView(Label(I18n.Text("Откройте сайт и нажмите звезду, чтобы сохранить его."),14,muted));
        if(state.Tabs.Count>0)
        {
            var row=Heading(page,I18n.Format("Открытые вкладки · {0}",state.Tabs.Count));var closeAll=Button(I18n.Text("Закрыть все"),CloseAll);row.AddView(closeAll);var cards=AddTabCards(page);
            foreach(var card in cards)card.NextFocusUpId=closeAll.Id;
            if(cards.Count>0)closeAll.NextFocusDownId=cards[0].Id;
            var lastFavorite=favoriteViews.Values.LastOrDefault();if(lastFavorite!=null){lastFavorite.NextFocusDownId=closeAll.Id;closeAll.NextFocusUpId=lastFavorite.Id;}
        }
        hint.Text=I18n.Text("↑ ↓ ← → — навигация   ·   OK — открыть   ·   Назад — выход");FocusLater(homeFocus);
    }
    void MoveBookmark(Bookmark favorite,int delta)
    {
        var i=state.Favorites.IndexOf(favorite);var j=i+delta;if(j<0||j>=state.Favorites.Count)return;
        (state.Favorites[i],state.Favorites[j])=(state.Favorites[j],state.Favorites[i]);Save();ShowHome();FocusLater(favoriteViews.GetValueOrDefault(favorite.Id));
    }
    void EditBookmark(Bookmark bookmark)
    {
        ShowEditor(I18n.Text("Изменить избранное"),[(I18n.Text("Название"),bookmark.Title,false),(I18n.Text("Адрес сайта"),bookmark.Url,true)],I18n.Text("Сохранить"),values=>{
            var i=state.Favorites.IndexOf(bookmark);if(i<0)return false;state.Favorites[i]=bookmark with{Title=values[0].Trim(),Url=Urls.Resolve(values[1])};Save();ShowHome();return true;
        });
    }
    List<AView> AddTabCards(LinearLayout page)
    {
        var cards=new List<AView>();
        var columns=Math.Clamp((int)(PageWidth/269),1,3);var tileWidth=Dp((PageWidth-columns*14)/columns);
        var grid=new GridLayout(Context){ColumnCount=columns,Focusable=false};page.AddView(grid);
        foreach(var tab in state.Tabs.ToList())
        {
            var tile=new FrameLayout(Context);var card=Button(tab.Title+"\n"+new Uri(tab.Url).Host,()=>Activate(tab.Id),I18n.Text("Открыть вкладку ")+tab.Title);card.TextSize=16;card.SetPadding(Dp(12),Dp(46),Dp(12),Dp(12));card.SetMaxLines(3);card.Ellipsize=global::Android.Text.TextUtils.TruncateAt.End;
            cards.Add(card);
            tile.AddView(card,new LayoutParams(-1,Dp(140)));var close=Icon("close",I18n.Text("Закрыть вкладку ")+tab.Title,()=>CloseTab(tab.Id));tile.AddView(close,new LayoutParams(Dp(44),Dp(44),GravityFlags.Top|GravityFlags.Right){TopMargin=Dp(6),RightMargin=Dp(6)});
            grid.AddView(tile,new GridLayout.LayoutParams{Width=tileWidth,Height=Dp(140),RightMargin=Dp(14),BottomMargin=Dp(14)});
        }
        return cards;
    }
    void ShowTabs()
    {
        screen="tabs";var page=Page(I18n.Text("Открытые вкладки"));var tools=Heading(page,I18n.Format("Вкладок: {0}",state.Tabs.Count));var create=Button(I18n.Text("Новая вкладка"),()=>Search());tools.AddView(create);tools.AddView(Button(I18n.Text("Закрыть все"),CloseAll));AddTabCards(page);FocusLater(create);
    }
    void CloseTab(string id)
    {
        DestroyTab(id);state.Tabs.RemoveAll(t=>t.Id==id);if(active==id)active=null;Save();if(screen=="tabs")ShowTabs();else ShowHome();
    }
    void CloseAll()
    {
        foreach(var id in live.Keys.ToArray())DestroyTab(id);state.Tabs.Clear();active=null;Save();ShowHome();
    }
    void ShowHistory()
    {
        if(state.PruneHistory())Save();RenderHistory(state.History);
    }
    void RenderHistory(List<Visit> history,int index=0,string focus="",bool preview=false)
    {
        const int size=50;index=Math.Clamp(index,0,Math.Max(0,(history.Count-1)/size));
        screen="history";var page=Page(I18n.Text("История посещений"));var tools=Heading(page,I18n.Format("Записей: {0}",history.Count));var clear=Button(I18n.Text("Очистить историю"),()=>{history.Clear();if(!preview)Save();RenderHistory(history,preview:preview);});tools.AddView(clear);
        AView? pageFocus=null;
        if(history.Count>size){
            var navigation=Row();var previous=Button(I18n.Text("Предыдущие"),()=>RenderHistory(history,index-1,"previous",preview));previous.Enabled=index>0;
            var next=Button(I18n.Text("Следующие"),()=>RenderHistory(history,index+1,"next",preview));next.Enabled=(index+1)*size<history.Count;
            navigation.AddView(previous,new LinearLayout.LayoutParams(0,Dp(50),1){MarginEnd=Dp(12)});navigation.AddView(next,new LinearLayout.LayoutParams(0,Dp(50),1));page.AddView(navigation);
            page.AddView(Label(I18n.Format("Показаны {0}–{1} из {2}",index*size+1,Math.Min(history.Count,(index+1)*size),history.Count),14,muted));
            pageFocus=focus=="previous"?(previous.Enabled?previous:next):focus=="next"?(next.Enabled?next:previous):null;
        }
        foreach(var visit in history.Skip(index*size).Take(size))
        {
            var b=Button(visit.Title+"\n"+new Uri(visit.Url).Host+" · "+visit.At.LocalDateTime.ToString("dd.MM HH:mm"),()=>Open(visit.Url));b.Gravity=GravityFlags.CenterVertical|GravityFlags.Left;
            b.SetMaxLines(2);b.Ellipsize=global::Android.Text.TextUtils.TruncateAt.End;
            page.AddView(b,new LinearLayout.LayoutParams(-1,Dp(70)){BottomMargin=Dp(8)});
        }
        if(history.Count==0)page.AddView(Label(I18n.Text("История пуста"),16,muted));FocusLater(pageFocus??clear);
    }
    string HistoryRetentionLabel()=>state.HistoryRetentionDays switch{7=>I18n.Text("7 дней"),90=>I18n.Text("3 месяца (90 дней)"),365=>I18n.Text("1 год (365 дней)"),_=>I18n.Text("1 месяц (30 дней)")};
    void SelectHistoryRetention()
    {
        var values=new[]{7,30,90,365};var labels=new[]{I18n.Text("7 дней"),I18n.Text("1 месяц (30 дней)"),I18n.Text("3 месяца (90 дней)"),I18n.Text("1 год (365 дней)")};
        AlertDialog? dialog=null;var builder=new AlertDialog.Builder(Context);builder.SetTitle(I18n.Text("Срок хранения истории"));builder.SetSingleChoiceItems(labels,Array.IndexOf(values,state.HistoryRetentionDays),(_,e)=>{dialog?.Dismiss();state.HistoryRetentionDays=values[e.Which];state.PruneHistory();Save();ShowSettings();});builder.SetNegativeButton(I18n.Text("Отмена"),(_,_)=>{});dialog=ShowDialog(builder);
    }
#if TV_DIAGNOSTICS
    public void PreviewHistory(int count)
    {
        var now=DateTimeOffset.Now;var sample=Enumerable.Range(0,Math.Clamp(count,1,50000)).Select(i=>new Visit($"History preview {i+1}",$"https://example.com/history/{i+1}",now.AddSeconds(-i))).ToList();var timer=System.Diagnostics.Stopwatch.StartNew();RenderHistory(sample,preview:true);global::Android.Util.Log.Info("RbrowserDiag",$"History preview: {sample.Count} visits, 50 rows, build {timer.ElapsedMilliseconds} ms");
    }
#endif
    void ShowSettings()
    {
        if(screen!="settings")settingsReturnTab=screen=="web"?active:null;
        screen="settings";var page=Page(I18n.Text("Настройки"));
        page.AddView(Button(I18n.Text("Язык интерфейса: ")+(state.InterfaceLanguage=="en"?"English":state.InterfaceLanguage=="ru"?I18n.Text("Русский"):I18n.Text("Системный")),SelectInterfaceLanguage),new LinearLayout.LayoutParams(-1,Dp(52)){BottomMargin=Dp(12)});
        page.AddView(Button(I18n.Text("Хранить историю: ")+HistoryRetentionLabel(),SelectHistoryRetention),new LinearLayout.LayoutParams(-1,Dp(52)){BottomMargin=Dp(12)});
        page.AddView(Label(I18n.Text("Старые посещения удаляются автоматически. При сокращении срока записи за его пределами удалятся сразу."),14,muted));
        Heading(page,I18n.Text("Блокировка рекламы"));
        var global=Button(I18n.Text("Блокировка на всех сайтах: ")+(state.BlockAdsEnabled?I18n.Text("включена"):I18n.Text("выключена")),()=>{state.BlockAdsEnabled=!state.BlockAdsEnabled;ResetWebViews();Save();ShowSettings();},I18n.Text("Включить или выключить блокировку глобально"));page.AddView(global,new LinearLayout.LayoutParams(-1,Dp(52)));
        page.AddView(Label(I18n.Text("Локальные фильтры рекламы на страницах и в поддерживаемых видеопотоках."),14,muted));
        Heading(page,I18n.Text("Управление"));page.AddView(Button(I18n.Text("Режим: ")+(state.CursorMode?I18n.Text("курсор"):I18n.Text("пульт")),()=>{state.CursorMode=!state.CursorMode;cursor=state.CursorMode;Save();ShowSettings();}),new LinearLayout.LayoutParams(-1,Dp(52)));
        page.AddView(Label(I18n.Text("Пульт: стрелки выбирают элементы, OK открывает. Курсор: стрелки перемещают указатель, OK нажимает."),14,muted));
        Heading(page,I18n.Text("Версия сайтов"));page.AddView(Button(I18n.Text("ПК-режим по умолчанию: ")+(state.DesktopModeDefault?I18n.Text("включён"):I18n.Text("выключен")),()=>{state.DesktopModeDefault=!state.DesktopModeDefault;ResetWebViews();Save();ShowSettings();}),new LinearLayout.LayoutParams(-1,Dp(52)));
        page.AddView(Label(I18n.Text("Применяется ко всем сайтам без отдельного выбора версии. Кнопка монитора на панели меняет режим текущего сайта."),14,muted));
        Heading(page,I18n.Text("Видео"));page.AddView(Button(I18n.Text("Встроенный плеер и поиск видео: ")+(state.NativeVideoEnabled?I18n.Text("включены"):I18n.Text("выключены")),()=>{state.NativeVideoEnabled=!state.NativeVideoEnabled;ResetWebViews();mediaCatalogs.Clear();Save();ShowSettings();}),new LinearLayout.LayoutParams(-1,Dp(52)));
        page.AddView(Label(I18n.Text("Кнопка «Видео на странице» ищет доступные потоки и открывает их в плеере Rbrowser. Для некоторых сайтов сначала нужно запустить видео на странице."),14,muted));
        Heading(page,I18n.Text("Фильтры рекламы"));page.AddView(Label("uBlock Origin uAssets · EasyList · RU AdList",16));
        page.AddView(Label(filters.Status,14,muted));var update=Button(I18n.Text("Обновить фильтры"),()=>_ = UpdateFilters());page.AddView(update,new LinearLayout.LayoutParams(-1,Dp(50)){TopMargin=Dp(14)});
        page.AddView(Label(I18n.Text("Правила работают локально, в том числе без загрузки списков из сети."),14,muted));
        Heading(page,I18n.Text("Кнопки пульта"));page.AddView(Label(I18n.Text("Стрелки — выбор элементов. OK — открыть. Назад на сайте — панель, затем переход назад. Долгое OK — панель. Menu — панель."),16));
        Heading(page,I18n.Text("О приложении"));page.AddView(Label($"Rbrowser {AppInfo.VersionString} · MAUI / Android WebView",14));page.AddView(Button(I18n.Text("Лицензии и поддержка фильтров"),()=>Info(I18n.Text("Фильтры"),I18n.Text("uAssets — GPL-3.0, EasyList — GPL-3.0 или CC BY-SA-3.0, RU AdList — CC BY 3.0, Public Suffix List — MPL-2.0.\n\nПоддерживаются сетевые правила и CSS-фильтры, включая нативный :has(). Scriptlets, процедурные селекторы, redirect/rewrite и изменение ответов пока не поддерживаются. Видеореклама Twitch может оставаться."))));
        defaultBrowserButton = Button(I18n.Text("Сделать браузером по умолчанию"), ChooseDefaultBrowser);
        page.AddView(defaultBrowserButton, new LinearLayout.LayoutParams(-1, Dp(52)) { TopMargin = Dp(12) });
        RefreshDefaultBrowserButton();
        page.AddView(Button(I18n.Text("Автор: rif009 · GitHub"), () => Open("https://github.com/Rif009/Rbrowser")),
            new LinearLayout.LayoutParams(-1, Dp(52)) { TopMargin = Dp(12) });
        FocusLater(global);
    }
    void ResetWebViews(){ExitVideoFullscreen();foreach(var id in live.Keys.ToArray())DestroyTab(id);}
    async Task UpdateFilters(){hint.Text=I18n.Text("Обновление списков…");try{await filters.Update();if(screen=="settings")ShowSettings();}catch(Exception ex){Info(I18n.Text("Фильтры"),ex.Message);}}
    void Search(string initial="",bool editCurrent=false)
    {
        var targetTab=editCurrent?active:null;
        ShowEditor(editCurrent?I18n.Text("Изменить адрес"):I18n.Text("Открыть сайт"),[(I18n.Text("Адрес или поисковый запрос"),initial,true)],I18n.Text("Открыть"),values=>{var url=Urls.Resolve(values[0]);if(targetTab!=null&&active==targetTab&&Web!=null){Web.LoadUrl(url);HideToolbar();}else Open(url);return true;});
    }
    void ShowEditor(string title,(string Label,string Value,bool Uri)[] fields,string action,Func<string[],bool> submit)
    {
        var dialog=new Dialog(Context);var previous=Activity.CurrentFocus;var inputs=new List<EditText>();
        var root=Column();root.SetPadding(Dp(24),Dp(22),Dp(24),Dp(22));var background=new GradientDrawable();background.SetColor(bg);background.SetCornerRadius(Dp(22));root.Background=background;
        var header=Row();header.AddView(Label("R",24,accent),new LinearLayout.LayoutParams(Dp(34),-2));header.AddView(Label(title,22));root.AddView(header);
        var error=Label("",12,Color.Rgb(239,160,150));
        void Submit(){var values=inputs.Select(v=>v.Text?.Trim()??"").ToArray();if(values.Any(string.IsNullOrWhiteSpace)||!submit(values)){error.Text=I18n.Text("Заполните все поля");return;}dialog.Dismiss();}
        foreach(var field in fields)
        {
            var label=Label(field.Label,13,muted);label.SetPadding(0,Dp(18),0,Dp(8));root.AddView(label);
            var input=new EditText(Context){Text=field.Value,Hint=field.Uri?I18n.Text("example.com или запрос"):I18n.Text("Название сайта"),TextSize=18,InputType=global::Android.Text.InputTypes.ClassText|(field.Uri?global::Android.Text.InputTypes.TextVariationUri:global::Android.Text.InputTypes.TextFlagCapSentences)};
            input.SetSingleLine(true);input.SetPadding(Dp(16),Dp(10),Dp(16),Dp(10));input.SetTextColor(text);input.SetHintTextColor(muted);input.BackgroundTintList=null;input.Typeface=Typeface.Create("sans-serif",TypefaceStyle.Normal);input.ImeOptions=ImeAction.Go;input.SetSelectAllOnFocus(true);FocusStyle(input,field.Label);
            var row=Row();row.AddView(input,new LinearLayout.LayoutParams(0,Dp(56),1){MarginEnd=Dp(8)});AddIcon(row,Icon("clear",I18n.Text("Очистить ")+field.Label.ToLowerInvariant(),()=>{input.Text="";input.RequestFocus();((InputMethodManager?)Context.GetSystemService(Context.InputMethodService))?.ShowSoftInput(input,ShowFlags.Implicit);}));root.AddView(row);inputs.Add(input);
            input.EditorAction+=(_,e)=>{if(e.ActionId is ImeAction.Go or ImeAction.Done or ImeAction.Search){Submit();e.Handled=true;}};
        }
        root.AddView(error,new LinearLayout.LayoutParams(-1,Dp(24)));
        var footer=Row();footer.SetGravity(GravityFlags.Right);footer.AddView(Button(I18n.Text("Отмена"),dialog.Dismiss),new LinearLayout.LayoutParams(Dp(130),Dp(48)){MarginEnd=Dp(10)});
        var confirm=Button(action,Submit);confirm.SetTextColor(bg);
        void PaintConfirm(bool focused){var fill=new GradientDrawable();fill.SetColor(accent);fill.SetCornerRadius(Dp(12));fill.SetStroke(Dp(focused?3:1),focused?text:accent);confirm.Background=fill;}
        PaintConfirm(false);confirm.FocusChange+=(_,e)=>PaintConfirm(e.HasFocus);footer.AddView(confirm,new LinearLayout.LayoutParams(Dp(170),Dp(48)));root.AddView(footer);
        var scroll=new ScrollView(Context);scroll.AddView(root,new ScrollView.LayoutParams(-1,-2));dialog.SetContentView(scroll);dialog.SetCancelable(true);dialogOpen=true;
        dialog.DismissEvent+=(_,_)=>{dialogOpen=false;if(previous?.IsAttachedToWindow==true)FocusLater(previous);else if(screen=="home")FocusLater(homeFocus);};
        dialog.Window?.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));dialog.Show();dialog.Window?.SetLayout(Math.Min(Dp(660),(Resources?.DisplayMetrics?.WidthPixels??1920)-Dp(60)),-2);dialog.Window?.SetSoftInputMode(SoftInput.AdjustResize|SoftInput.StateAlwaysVisible);inputs[0].RequestFocus();
    }
    public void Speech(string value){if(!string.IsNullOrWhiteSpace(value))Open(Urls.Resolve(value.Trim()));}
    AlertDialog ShowDialog(AlertDialog.Builder builder)
    {
        var previous=Activity.CurrentFocus;dialogOpen=true;var dialog=builder.Create()!;dialog.DismissEvent+=(_,_)=>{dialogOpen=false;if(previous?.IsAttachedToWindow==true)FocusLater(previous);else if(screen=="home")FocusLater(homeFocus);};dialog.Show();return dialog;
    }
    void Info(string title,string message){var builder=new AlertDialog.Builder(Context);builder.SetTitle(title);builder.SetMessage(message);builder.SetPositiveButton("OK",(_,_)=>{});ShowDialog(builder);}
    public void Open(string url)
    {
        if(!Urls.IsWeb(url)){Info(I18n.Text("Адрес"),I18n.Text("Поддерживаются адреса http и https."));return;}
        var existing=state.Tabs.FirstOrDefault(t=>t.Url==url);
        if(existing!=null){Activate(existing.Id);return;}
        var tab=new Tab(Guid.NewGuid().ToString("N"),new Uri(url).Host,url);state.Tabs.Add(tab);Save();Activate(tab.Id);
    }
    WebView? Web => active!=null&&live.TryGetValue(active,out var web)?web:null;
    string Url => active!=null&&pageUrls.TryGetValue(active,out var url)?url:state.Tabs.FirstOrDefault(t=>t.Id==active)?.Url??"https://www.google.com/";
    void Activate(string id)
    {
        if(!state.Tabs.Any(t=>t.Id==id))return;
        ExitVideoFullscreen();
        HideFullscreen();PauseWeb();DetachWeb();active=id;screen="web";inFrame=false;content.RemoveAllViews();
        var layout=Column();content.AddView(layout,new LayoutParams(-1,-1));
        var holder=new FrameLayout(Context);layout.AddView(holder,new LinearLayout.LayoutParams(-1,0,1));
        var created=false;
        if(!live.TryGetValue(id,out var web))
        {
            if(live.Count>=2)DestroyTab(live.Keys.First(k=>k!=id));
            web=CreateWeb(id);live[id]=web;created=true;
        }
        holder.AddView(web,new LayoutParams(-1,-1));holder.AddView(pointer,new LayoutParams(-1,-1));
        toolbar=Column();toolbar.SetPadding(Dp(24),Dp(8),Dp(20),0);toolbar.SetBackgroundColor(bg);
        var addressRow=Row();var addressButton=Button(Url,()=>Search(Url,true),I18n.Text("Изменить адрес текущего сайта"));addressButton.Gravity=GravityFlags.Left|GravityFlags.CenterVertical;addressButton.TextSize=14;addressButton.SetSingleLine(true);addressButton.Ellipsize=global::Android.Text.TextUtils.TruncateAt.Middle;address=addressButton;
        addressRow.AddView(addressButton,new LinearLayout.LayoutParams(-1,Dp(48)));toolbar.AddView(addressRow);
        toolbarIcons=Row();toolbarIcons.SetPadding(0,Dp(8),0,Dp(6));toolbarIcons.SetGravity(GravityFlags.Center);
        toolbarBack=Icon("down",I18n.Text("Свернуть панель"),HideToolbar);AddIcon(toolbarIcons,toolbarBack);
        findVideoButton=null;if(state.NativeVideoEnabled){findVideoButton=Icon("video-search",I18n.Text("Видео на странице · открыть в плеере Rbrowser"),FindVideos);findVideoButton.FocusChange+=(_,_)=>UpdateVideoIndicator();AddIcon(toolbarIcons,findVideoButton);}
        cursorButton=Icon(cursor?"remote":"cursor",cursor?I18n.Text("Переключить на пульт"):I18n.Text("Переключить на курсор"),ToggleCursor,cursor);AddIcon(toolbarIcons,cursorButton);
        desktopButton=Icon("desktop",state.IsDesktop(Url)?I18n.Text("ПК-режим включён · переключить на обычный сайт"):I18n.Text("ПК-режим выключен · переключить на версию для ПК"),ToggleDesktop,state.IsDesktop(Url));AddIcon(toolbarIcons,desktopButton);
        AddIcon(toolbarIcons,Icon("home",I18n.Text("Главная"),ShowHome));AddIcon(toolbarIcons,Icon("back",I18n.Text("Назад по истории"),GoBack));AddIcon(toolbarIcons,Icon("forward",I18n.Text("Вперёд"),()=>{if(Web?.CanGoForward()==true)Web.GoForward();}));AddIcon(toolbarIcons,Icon("refresh",I18n.Text("Обновить"),()=>Web?.Reload()));
        AddIcon(toolbarIcons,Icon("tabs",I18n.Text("Открытые вкладки"),ShowTabs));AddIcon(toolbarIcons,Icon("star",I18n.Text("Сохранить в избранное"),BookmarkPage));
        AddIcon(toolbarIcons,Icon("play",I18n.Text("Видео: воспроизведение / пауза"),()=>VideoAction("toggle")));AddIcon(toolbarIcons,Icon("fullscreen",I18n.Text("Полный экран видео"),()=>VideoAction("fullscreen")));
        toolbarIcons.AddView(new AView(Context),new LinearLayout.LayoutParams(0,1,1));AddIcon(toolbarIcons,Icon("history",I18n.Text("История посещений"),ShowHistory));AddIcon(toolbarIcons,Icon("settings",I18n.Text("Настройки Rbrowser"),ShowSettings));toolbar.AddView(toolbarIcons);layout.AddView(toolbar);
        ApplySite(web);web.OnResume();if(WebViewFeature.IsFeatureSupported("MUTE_AUDIO"))WebViewCompat.SetAudioMuted(web,false);CookieManager.Instance?.SetAcceptThirdPartyCookies(web,true);
        cursorX=Dp(300);cursorY=Dp(180);pointer.Move(cursorX,cursorY);pointer.Visibility=cursor?ViewStates.Visible:ViewStates.Gone;
        if(created)web.LoadUrl(Url);HideToolbar();
    }
    WebView CreateWeb(string id)
    {
#if DEBUG || TV_DIAGNOSTICS
        WebView.SetWebContentsDebuggingEnabled(true);
#endif
        var web=new WebView(Context){FocusableInTouchMode=false,Focusable=false};web.SetBackgroundColor(bg);
        if(WebViewFeature.IsFeatureSupported("DOCUMENT_START_SCRIPT"))
        {
            if(state.NativeVideoEnabled)WebViewCompat.AddDocumentStartJavaScript(web,"window.__rbMediaEnabled=true;\n"+mediaScript,new HashSet<string>{"*"});
            var enabled=state.BlockAdsEnabled&&state.TwitchVideoAdsEnabled?"true":"false";
            var excluded=string.Join(',',state.Sites.Where(p=>!p.Value.BlockAds).Select(p=>JsonSerializer.Serialize(p.Key,StateJsonContext.Default.String)));
            var diagnostics="false";
#if DEBUG || TV_DIAGNOSTICS
            diagnostics="true";
#endif
            WebViewCompat.AddDocumentStartJavaScript(web,"window.__rbTwitchConfig={enabled:"+enabled+",excludedHosts:["+excluded+"],diagnostics:"+diagnostics+"};\n"+twitchScript,new HashSet<string>{"https://twitch.tv","https://*.twitch.tv"});
        }
        web.Settings.JavaScriptEnabled=true;web.Settings.DomStorageEnabled=true;web.Settings.MediaPlaybackRequiresUserGesture=true;
        web.Settings.SetSupportMultipleWindows(true);web.Settings.JavaScriptCanOpenWindowsAutomatically=false;web.Settings.AllowFileAccess=false;web.Settings.AllowContentAccess=false;
        web.Settings.MixedContentMode=MixedContentHandling.NeverAllow;web.Settings.BuiltInZoomControls=true;web.Settings.DisplayZoomControls=false;web.Settings.SetSupportZoom(true);
        if(WebViewFeature.IsFeatureSupported("DOCUMENT_START_SCRIPT"))WebViewCompat.AddDocumentStartJavaScript(web,"""
            window.addEventListener('message',event=>{
              if(event.data?.__rb_media!=='pause')return;
              document.querySelectorAll('video,audio').forEach(v=>v.pause());
              document.querySelectorAll('iframe').forEach(f=>f.contentWindow?.postMessage({__rb_media:'pause'},'*'));
            });
            """,new HashSet<string>{"*"});
        web.SetWebViewClient(new BrowserClient(this,id));web.SetWebChromeClient(new ChromeClient(this,id));CookieManager.Instance?.SetAcceptCookie(true);return web;
    }
    void ApplySite(WebView web)
    {
        var prefs=state.For(Url);web.Settings.TextZoom=prefs.Zoom;
        var agent=state.IsDesktop(Url)?"Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36":WebSettings.GetDefaultUserAgent(Context);
        if(web.Settings.UserAgentString!=agent)web.Settings.UserAgentString=agent;
        web.Settings.UseWideViewPort=true;web.Settings.LoadWithOverviewMode=true;
        UpdateModeButtons();
    }
    void UpdateModeButtons()
    {
        if(cursorButton!=null){cursorButton.Selected=cursor;cursorButton.ContentDescription=cursor?I18n.Text("Переключить на пульт"):I18n.Text("Переключить на курсор");cursorButton.SetImageDrawable(new IconDrawable(cursor?"remote":"cursor",cursor?accent:text));cursorButton.Background=Surface(cursorButton.HasFocus,cursor);}
        if(desktopButton!=null){var enabled=state.IsDesktop(Url);desktopButton.Selected=enabled;desktopButton.ContentDescription=enabled?I18n.Text("ПК-режим включён · переключить на обычный сайт"):I18n.Text("ПК-режим выключен · переключить на версию для ПК");desktopButton.SetImageDrawable(new IconDrawable("desktop",enabled?accent:text));desktopButton.Background=Surface(desktopButton.HasFocus,enabled);}
    }
    void ToggleCursor(){cursor=!cursor;state.CursorMode=cursor;inFrame=false;pointer.Visibility=cursor?ViewStates.Visible:ViewStates.Gone;UpdateModeButtons();Save();HideToolbar();}
    void ToggleDesktop(){state.SetDesktop(Url,!state.IsDesktop(Url));Save();if(Web!=null){ApplySite(Web);Web.Reload();}HideToolbar();}
    void BookmarkPage()
    {
        if(state.Favorites.Any(b=>b.Url==Url)){hint.Text=I18n.Text("Страница уже в избранном");return;}
        state.Favorites.Add(new(Guid.NewGuid().ToString("N"),Web?.Title??new Uri(Url).Host,Url));Save();hint.Text=I18n.Text("Сохранено в избранном");
    }
    void ShowToolbar(){if(screen!="web"||toolbar==null)return;HideVideoNotice();ExitVideoFullscreen();inFrame=false;UpdateModeButtons();toolbar.Visibility=ViewStates.Visible;hint.Visibility=ViewStates.Visible;UpdateVideoIndicator();RefreshVideoIndicator();FocusLater(toolbarBack);}
    void HideToolbar(){
        // WebView's native focus search can select the first HTML input and scroll to it.
        // Remote page actions are routed by the Activity; only an actual input tap needs native text focus.
        var web=Web;if(web!=null){web.FocusableInTouchMode=false;web.Focusable=false;}
        // Park focus on the shell before removing the focused toolbar button.
        // Otherwise Android's fallback focus traversal may enter the WebView's HTML inputs.
        DescendantFocusability=DescendantFocusability.BlockDescendants;FocusableInTouchMode=true;Focusable=true;RequestFocus();DescendantFocusability=DescendantFocusability.AfterDescendants;
        if(toolbar!=null)toolbar.Visibility=ViewStates.Gone;hint.Visibility=ViewStates.Gone;
        if(web!=null)HidePageKeyboard(web);
        hint.Text=cursor?I18n.Text("Курсор · Назад — панель"):I18n.Text("Стрелки — выбрать элемент   ·   OK — открыть   ·   Назад — панель   ·   Долгое OK — панель");
        UpdateVideoIndicator();
    }
    void GoBack(){if(Web?.CanGoBack()==true){Web.GoBack();HideToolbar();}else ShowHome();}
    void DetachWeb(){foreach(var web in live.Values)if(web.Parent is ViewGroup parent)parent.RemoveView(web);if(pointer.Parent is ViewGroup p)p.RemoveView(pointer);}
    void PauseWeb()
    {
        var web=Web;if(web==null)return;web.EvaluateJavascript("window.__rb?.pause()",null);web.OnPause();
        if(WebViewFeature.IsFeatureSupported("MUTE_AUDIO"))WebViewCompat.SetAudioMuted(web,true);
        else if(active!=null)DestroyTab(active); // Older WebViews: unload to guarantee no background sound.
    }
    public void Pause(){if(voiceListening)StopVoice();nativePlayer?.Pause();PauseWeb();CookieManager.Instance?.Flush();Save();}
    public void Resume(){RefreshDefaultBrowserButton();if(screen!="web")return;if(Web==null&&active!=null){Activate(active);return;}Web?.OnResume();if(Web!=null&&WebViewFeature.IsFeatureSupported("MUTE_AUDIO"))WebViewCompat.SetAudioMuted(Web,false);}
    void DestroyTab(string id){videoNoticeTabs.Remove(id);if(active==id)HideVideoNotice();if(live.Remove(id,out var web)){web.StopLoading();web.OnPause();if(web.Parent is ViewGroup parent)parent.RemoveView(web);web.Destroy();web.Dispose();}pageUrls.Remove(id);blocked.TryRemove(id,out _);mediaCatalogs.TryRemove(id,out _);}
    void Save()=>_ = SaveSafely();
    async Task SaveSafely(){try{await store.Save(state);}catch(IOException){hint.Text=I18n.Text("Не удалось сохранить данные. Проверьте свободное место.");}}
    public void Shutdown(){if(stopped)return;stopped=true;HideStartupSplash();HideVideoNotice();StopVoice();CloseNativePlayer(false);Save();HideFullscreen();foreach(var id in live.Keys.ToArray())DestroyTab(id);filters.Changed-=FiltersChanged;if(Current==this)Current=null;}
    void Evaluate(string js,Action<string>? callback=null)=>Web?.EvaluateJavascript(js,callback==null?null:new JsResult(callback));
    void ActivateTarget(string expression)
    {
        var targetWeb=Web;
        Evaluate(expression,value=>{
            if(targetWeb==null||Web!=targetWeb||value is "null" or "undefined"){if(value=="null")hint.Text=I18n.Text("На этой странице видео не найдено");return;}
            try{using var result=JsonDocument.Parse(value);var t=result.RootElement;if(t.TryGetProperty("kind",out var handled)&&handled.GetString()=="handled"){HidePageKeyboard(targetWeb);return;}var width=t.GetProperty("width").GetSingle();var height=t.GetProperty("height").GetSingle();if(width<=0||height<=0)return;
                TapAt(targetWeb,t.GetProperty("x").GetSingle()*targetWeb.Width/width,t.GetProperty("y").GetSingle()*targetWeb.Height/height,t.GetProperty("kind").GetString()=="input");
                if(t.GetProperty("kind").GetString() is "frame" or "video")inFrame=true;
            }catch(JsonException){}
        });
    }
    void VideoAction(string command){HideToolbar();if(command=="fullscreen")Evaluate("window.__rb?.cinemaToggle()",v=>videoFullscreen=v=="true");else Evaluate("window.__rb?.video('toggle')");}
    void ExitVideoFullscreen(){if(videoFullscreen)Evaluate("window.__rb?.cinemaExit()");videoFullscreen=false;}
    public bool Remote(KeyEvent e)
    {
        if(!ready||dialogOpen)return false;
        var key=e.KeyCode;
        if(nativePlayerPanel!=null)return NativePlayerRemote(e);
        if(screen=="videos"&&key==Keycode.Back){if(e.Action==KeyEventActions.Up)ReturnFromVideoList();return true;}
        if(key==Keycode.Menu){if(e.Action==KeyEventActions.Up)ShowToolbar();return true;}
        if(key==Keycode.Back)
        {
            if(screen=="home"&&!editing&&fullscreen==null&&!inFrame)return false;
            if(e.Action!=KeyEventActions.Up)return true;
            if(fullscreen!=null){HideFullscreen();return true;}
            if(videoFullscreen){Evaluate("window.__rb?.cinemaExit()");videoFullscreen=false;HideToolbar();return true;}
            if(inFrame){inFrame=false;ShowToolbar();return true;}
            if(screen=="settings"&&settingsReturnTab!=null){var id=settingsReturnTab;settingsReturnTab=null;Activate(id);return true;}
            if(screen=="web"){if(toolbar?.Visibility==ViewStates.Visible)GoBack();else ShowToolbar();return true;}
            if(screen!="home"||editing){editing=false;ShowHome();return true;}
            return false;
        }
        if(screen!="web")return false;
        if(key is Keycode.MediaPlayPause or Keycode.MediaPlay or Keycode.MediaPause){if(e.Action==KeyEventActions.Up)Evaluate("window.__rb?.video('toggle')");return true;}
        if(fullscreen!=null||videoFullscreen||inFrame)
        {
            if(key is Keycode.DpadCenter or Keycode.Enter){if(e.Action==KeyEventActions.Up)Evaluate("window.__rb?.video('toggle')");return true;}
            if(key is Keycode.DpadLeft or Keycode.DpadRight){if(e.Action==KeyEventActions.Down&&e.RepeatCount%3==0)Evaluate("window.__rb?.video('"+(key==Keycode.DpadLeft?"left":"right")+"')");return true;}
            if(key==Keycode.DpadUp&&fullscreen==null){if(e.Action==KeyEventActions.Up)VideoAction("fullscreen");return true;}
            if(key==Keycode.DpadDown&&fullscreen==null){if(e.Action==KeyEventActions.Up)ShowToolbar();return true;}
            return false;
        }
        if(toolbar?.Visibility==ViewStates.Visible&&fullscreen==null)return false;
        if(key is Keycode.DpadCenter or Keycode.Enter)
        {
            if(e.Action==KeyEventActions.Down){if(e.RepeatCount==0)lastOk=SystemClock.UptimeMillis();if(e.RepeatCount>0&&SystemClock.UptimeMillis()-lastOk>550){lastOk=0;ShowToolbar();}return true;}
            if(lastOk==0)return true;
            if(cursor){Tap();return true;}
            ActivateTarget("window.__rb?.target()");return true;
        }
        var direction=key switch{Keycode.DpadLeft=>"left",Keycode.DpadRight=>"right",Keycode.DpadUp=>"up",Keycode.DpadDown=>"down",_=>""};
        if(direction.Length==0)return false;
        if(e.Action==KeyEventActions.Down)
        {
            if(cursor){var step=Dp(Math.Min(35,10+e.RepeatCount*2));cursorX=Math.Clamp(cursorX+(direction=="left"?-step:direction=="right"?step:0),0,Web?.Width??Width);cursorY=Math.Clamp(cursorY+(direction=="up"?-step:direction=="down"?step:0),0,Web?.Height??Height);pointer.Move(cursorX,cursorY);if(cursorY>=(Web?.Height??Height)-Dp(20))Web?.ScrollBy(0,Dp(35));if(cursorY<Dp(20))Web?.ScrollBy(0,-Dp(35));}
            else Evaluate("window.__rb?.move('"+direction+"')");
        }
        return true;
    }
    void Tap()
    {
        var web=Web;if(web==null||web.Width<=0||web.Height<=0)return;var x=cursorX;var y=cursorY;
        var fx=(x/web.Width).ToString(System.Globalization.CultureInfo.InvariantCulture);var fy=(y/web.Height).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Evaluate("window.__rb?.pointerTarget("+fx+","+fy+")",value=>{
            if(Web!=web)return;try{using var doc=JsonDocument.Parse(value);var t=doc.RootElement;
                if(t.ValueKind==JsonValueKind.Object&&t.TryGetProperty("kind",out var action)&&action.GetString()=="handled"){HidePageKeyboard(web);return;}
                if(t.ValueKind==JsonValueKind.Object&&t.TryGetProperty("kind",out var kind)&&kind.ValueKind==JsonValueKind.String&&kind.GetString()=="link"&&t.TryGetProperty("url",out var href)&&href.ValueKind==JsonValueKind.String){
                    var url=href.GetString();if(Urls.IsWeb(url)){((InputMethodManager?)Context.GetSystemService(Context.InputMethodService))?.HideSoftInputFromWindow(web.WindowToken,HideSoftInputFlags.None);web.LoadUrl(url!);return;}
                }
                if(t.ValueKind==JsonValueKind.Object&&t.TryGetProperty("kind",out var input)&&input.GetString()=="input"){TapAt(web,x,y,true);return;}
            }catch(JsonException){}TapAt(web,x,y);
        });
    }
    void HidePageKeyboard(WebView web){web.FocusableInTouchMode=false;web.Focusable=false;((InputMethodManager?)Context.GetSystemService(Context.InputMethodService))?.HideSoftInputFromWindow(web.WindowToken,HideSoftInputFlags.None);}
    void TapAt(WebView web,float x,float y,bool editable=false){if(editable){web.Focusable=true;web.FocusableInTouchMode=true;}var time=SystemClock.UptimeMillis();using var down=MotionEvent.Obtain(time,time,MotionEventActions.Down,x,y,0);using var up=MotionEvent.Obtain(time,time+45,MotionEventActions.Up,x,y,0);if(down==null||up==null)return;down.SetSource(InputSourceType.Touchscreen);up.SetSource(InputSourceType.Touchscreen);web.DispatchTouchEvent(down);web.DispatchTouchEvent(up);}
    void ShowFullscreen(AView view,WebChromeClient.ICustomViewCallback callback)
    {
        HideVideoNotice();
        if(fullscreen!=null){callback.OnCustomViewHidden();return;}fullscreen=view;fullscreenCallback=callback;chrome.Visibility=ViewStates.Gone;AddView(view,new LayoutParams(-1,-1));view.RequestFocus();
    }
    void HideFullscreen(){if(fullscreen==null)return;RemoveView(fullscreen);fullscreen=null;fullscreenCallback?.OnCustomViewHidden();fullscreenCallback=null;chrome.Visibility=ViewStates.Visible;Web?.RequestFocus();}
    void PageStarted(string id,string url)
    {
        videoNoticeTabs.Remove(id);if(active==id)HideVideoNotice();
        pageUrls[id]=url;if(active==id){address?.SetText(url,TextView.BufferType.Normal);inFrame=false;videoFullscreen=false;if(Web!=null)ApplySite(Web);}blocked[id]=0;
        mediaCatalogs.GetOrAdd(id,_=>new()).Clear();
        if(active==id)UpdateVideoIndicator();
        if(Urls.IsWeb(url)){var index=state.Tabs.FindIndex(t=>t.Id==id);if(index>=0){state.Tabs[index]=state.Tabs[index] with {Url=url,Title=new Uri(url).Host};Save();}}
    }
    void PageFinished(string id,WebView view,string url)
    {
        if(!Urls.IsWeb(url))return;var title=view.Title??new Uri(url).Host;var i=state.Tabs.FindIndex(t=>t.Id==id);if(i<0)return;state.Tabs[i]=state.Tabs[i] with {Url=url,Title=title};state.Record(title,url);Save();
        InjectPageTools(view,url);
        if(active==id&&screen=="web"){hint.Text=I18n.Text("Готово · заблокировано запросов: ")+blocked.GetValueOrDefault(id)+I18n.Text(" · Назад — панель");}
    }
    void InjectPageTools(WebView view,string url)
    {
        if(!Urls.IsWeb(url))return;view.EvaluateJavascript(navigationScript,null);var css=state.ShouldBlock(url)?filters.Engine.Css(new Uri(url).IdnHost):"";view.EvaluateJavascript("window.__rb?.css("+JsonSerializer.Serialize(css,StateJsonContext.Default.String)+")",null);
        view.EvaluateJavascript("window.__rbTwitch?.setEnabled("+(state.ShouldBlock(url)&&state.TwitchVideoAdsEnabled?"true":"false")+")",null);
        if(state.NativeVideoEnabled)view.EvaluateJavascript(mediaScript,null);
    }
    sealed class JsResult(Action<string> action) : Java.Lang.Object,IValueCallback {public void OnReceiveValue(Java.Lang.Object? value)=>action(value?.ToString()??"");}
    sealed class Pointer(Context context,Color color):AView(context)
    {
        float x,y;readonly Paint paint=new(PaintFlags.AntiAlias){Color=color};
        public void Move(float a,float b){x=a;y=b;Invalidate();}
        protected override void OnDraw(Canvas canvas){base.OnDraw(canvas);paint.SetStyle(Paint.Style.Fill);canvas.DrawCircle(x,y,8,paint);paint.SetStyle(Paint.Style.Stroke);paint.StrokeWidth=2;canvas.DrawCircle(x,y,12,paint);}
    }
    sealed class BrowserClient(TvBrowser owner,string id):WebViewClient
    {
        readonly VideoCatalog catalog=owner.mediaCatalogs.GetOrAdd(id,_=>new());
        volatile string page="";volatile bool enabled=true;
        public override void OnPageStarted(WebView? view,string? url,Bitmap? favicon){page=url??"";enabled=owner.state.For(page).BlockAds;owner.PageStarted(id,page);}
        public override void OnPageFinished(WebView? view,string? url){if(view!=null&&url!=null)owner.PageFinished(id,view,url);}
        public override void OnPageCommitVisible(WebView? view,string? url){if(view!=null&&url!=null)owner.InjectPageTools(view,url);}
        public override bool ShouldOverrideUrlLoading(WebView? view,IWebResourceRequest? request)
        {
            var url=request?.Url?.ToString();if(Urls.IsWeb(url))return false;if(request?.IsForMainFrame==true)owner.Info(I18n.Text("Ссылка"),I18n.Text("Этот адрес открывается отдельным приложением. Скопируйте обычную ссылку http или https."));return true;
        }
        public override WebResourceResponse? ShouldInterceptRequest(WebView? view,IWebResourceRequest? request)
        {
            if(request==null||request.IsForMainFrame)return null;
            var url=request.Url?.ToString()??"";var headers=request.RequestHeaders;
            string Header(string key)=>headers?.FirstOrDefault(k=>k.Key.Equals(key,StringComparison.OrdinalIgnoreCase)).Value??"";
            var dest=Header("Sec-Fetch-Dest");var ext=Uri.TryCreate(url,UriKind.Absolute,out var u)?System.IO.Path.GetExtension(u.AbsolutePath).ToLowerInvariant():"";
            var type=dest switch{"script"=>"script","style"=>"stylesheet","image"=>"image","font"=>"font","video" or "audio"=>"media","iframe" or "frame"=>"subdocument","empty"=>"xmlhttprequest",_=>ext switch{".js"=>"script",".css"=>"stylesheet",".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg"=>"image",".woff" or ".woff2" or ".ttf"=>"font",".mp4" or ".m3u8" or ".ts"=>"media",_=>"other"}};
            var source=Header("Referer");if(!Urls.IsWeb(source))source=page;
            var isBlocked=enabled&&owner.state.BlockAdsEnabled&&owner.filters.Engine.Blocks(url,source,type);
            if(!isBlocked){if(owner.state.NativeVideoEnabled&&VideoCatalog.Kind(url)!=null){catalog.Add(url,source);owner.Post(()=>{if(!owner.stopped&&owner.active==id)owner.UpdateVideoIndicator();});}return null;}
            owner.blocked.AddOrUpdate(id,1,(_,count)=>count+1);
            return new WebResourceResponse("text/plain","UTF-8",204,"No Content",new Dictionary<string,string>{{"Access-Control-Allow-Origin","*"}},new MemoryStream([]));
        }
        public override void OnReceivedError(WebView? view,IWebResourceRequest? request,WebResourceError? error){if(request?.IsForMainFrame==true)owner.hint.Text=I18n.Text("Страница не загружена: ")+error?.Description+I18n.Text(" · Назад — панель");}
        public override void OnReceivedSslError(WebView? view,SslErrorHandler? handler,global::Android.Net.Http.SslError? error){handler?.Cancel();owner.hint.Text=I18n.Text("Ошибка сертификата сайта. Загрузка остановлена.");}
        public override bool OnRenderProcessGone(WebView? view,RenderProcessGoneDetail? detail)
        {
            owner.Post(()=>{var wasActive=owner.active==id;owner.DestroyTab(id);if(wasActive){owner.active=null;owner.ShowHome();owner.Info(I18n.Text("Вкладка остановлена"),I18n.Text("Процесс страницы завершился. Вкладка сохранена на главной — её можно открыть снова."));}});return true;
        }
    }
    sealed class ChromeClient(TvBrowser owner,string id):WebChromeClient
    {
        public override void OnShowCustomView(AView? view,ICustomViewCallback? callback){if(view!=null&&callback!=null)owner.ShowFullscreen(view,callback);}
        public override void OnHideCustomView()=>owner.HideFullscreen();
        public override void OnProgressChanged(WebView? view,int progress){if(owner.active==id&&owner.screen=="web"&&progress<100)owner.hint.Text=I18n.Format("Загрузка · {0}%",progress);}
        public override bool OnCreateWindow(WebView? view,bool isDialog,bool isUserGesture,Message? resultMsg)
        {
            if(!isUserGesture||resultMsg?.Obj is not WebView.WebViewTransport transport)return false;
            var temporary=new WebView(owner.Context);temporary.SetWebViewClient(new PopupClient(owner,temporary));transport.WebView=temporary;resultMsg.SendToTarget();return true;
        }
        public override void OnPermissionRequest(PermissionRequest? request)=>request?.Deny();
    }
    sealed class PopupClient(TvBrowser owner,WebView temporary):WebViewClient
    {
        bool handled;
        public override bool ShouldOverrideUrlLoading(WebView? view,IWebResourceRequest? request)
        {
            var url=request?.Url?.ToString();if(handled||!Urls.IsWeb(url))return true;handled=true;
            owner.Post(()=>{var builder=new AlertDialog.Builder(owner.Context);builder.SetTitle(I18n.Text("Открыть новую вкладку?"));builder.SetMessage(url);builder.SetPositiveButton(I18n.Text("Открыть"),(_,_)=>owner.Open(url!));builder.SetNegativeButton(I18n.Text("Отмена"),(_,_)=>{});owner.ShowDialog(builder);temporary.Destroy();temporary.Dispose();});return true;
        }
    }
}
