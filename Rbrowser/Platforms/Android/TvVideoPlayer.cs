using Android.Graphics;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using AndroidX.Media3.Common;
using AndroidX.Media3.DataSource;
using AndroidX.Media3.ExoPlayer;
using AndroidX.Media3.ExoPlayer.Source;
using AndroidX.Media3.UI;
using Rbrowser.Core;
using System.Text.Json;
using Color=Android.Graphics.Color;

namespace Rbrowser.Platforms.Android;

public sealed partial class TvBrowser
{
    IExoPlayer? nativePlayer;
    PlayerView? nativePlayerView;
    FrameLayout? nativePlayerPanel;
    LinearLayout? nativePlayerHeader;
    string? videoReturnTab;
    long lastPlayerKey;
    string? playerFocusSignature;
    readonly HashSet<int> decoratedPlayerControls=[];
    readonly HashSet<string> videoNoticeTabs=[];
    global::Android.Views.View? videoNotice;
    int videoNoticeGeneration;
    bool videoNoticePending;
    global::Android.App.Dialog? playerExitDialog;
    void HideVideoNotice(){videoNoticeGeneration++;videoNoticePending=false;if(videoNotice!=null){videoNotice.Animate()?.Cancel();RemoveView(videoNotice);videoNotice.Dispose();videoNotice=null;}}
    bool CanShowVideoNotice()=>!stopped&&screen=="web"&&state.NativeVideoEnabled&&nativePlayerPanel==null&&fullscreen==null&&!videoFullscreen&&!dialogOpen&&toolbar?.Visibility!=ViewStates.Visible;
    void MaybeShowVideoNotice(int count)
    {
        if(count==0||active==null||videoNoticePending||videoNoticeTabs.Contains(active)||!CanShowVideoNotice())return;
        videoNoticePending=true;var generation=videoNoticeGeneration;var tab=active;var url=Url;
        PostDelayed(()=>{
            if(generation!=videoNoticeGeneration)return;videoNoticePending=false;
            if(active!=tab||Url!=url||!CanShowVideoNotice()||!mediaCatalogs.TryGetValue(tab,out var catalog)||catalog.Count==0)return;
            var box=Row();box.SetPadding(Dp(18),Dp(14),Dp(18),Dp(14));box.Focusable=false;box.Clickable=false;
            var background=new global::Android.Graphics.Drawables.GradientDrawable();background.SetColor(VideoSurface);background.SetCornerRadius(Dp(18));background.SetStroke(Dp(1),VideoBorder);box.Background=background;
            var icon=new ImageView(Context){Focusable=false};icon.SetImageDrawable(new IconDrawable("video-search",VideoAccent));box.AddView(icon,new LinearLayout.LayoutParams(Dp(34),Dp(34)){MarginEnd=Dp(14)});
            var copy=Column();copy.AddView(Label(I18n.Text("Видео найдено"),18,VideoAccent));copy.AddView(Label(I18n.Text("Нажмите «Назад» → «Видео на странице»"),13,muted));box.AddView(copy,new LinearLayout.LayoutParams(0,-2,1));
            box.ContentDescription=I18n.Text("Видео найдено")+". "+I18n.Text("Нажмите «Назад» → «Видео на странице»");
            videoNotice=box;videoNoticeTabs.Add(tab);box.Alpha=0;AddView(box,new LayoutParams(Math.Min(Dp(390),Width-Dp(48)),-2,GravityFlags.Bottom|GravityFlags.Right){RightMargin=Dp(24),BottomMargin=Dp(28)});box.Animate()?.Alpha(1)?.SetDuration(180)?.Start();
            PostDelayed(()=>{if(videoNotice==box)HideVideoNotice();},7000);
        },500);
    }
    void ConfirmPlayerExit()
    {
        if(nativePlayer==null||playerExitDialog!=null)return;
        var expected=nativePlayer;var resume=expected.PlayWhenReady;expected.Pause();var leaving=false;
        var root=Column();root.SetPadding(Dp(26),Dp(24),Dp(26),Dp(24));root.Background=Surface();root.AddView(Label(I18n.Text("Выйти из плеера?"),24,accent));root.AddView(Label(I18n.Text("Вы уверены, что хотите выйти из встроенного плеера?"),17,muted),new LinearLayout.LayoutParams(-1,-2){TopMargin=Dp(12),BottomMargin=Dp(22)});
        var dialog=new global::Android.App.Dialog(Context);var row=Row();var stay=Button(I18n.Text("Продолжить просмотр"),()=>dialog.Dismiss());row.AddView(stay,new LinearLayout.LayoutParams(0,Dp(54),1){MarginEnd=Dp(12)});
        row.AddView(Button(I18n.Text("Выйти из плеера"),()=>{leaving=true;dialog.Dismiss();CloseNativePlayer(true);}),new LinearLayout.LayoutParams(0,Dp(54),1));root.AddView(row);
        dialog.SetContentView(root);dialog.SetCancelable(true);dialogOpen=true;playerExitDialog=dialog;
        dialog.DismissEvent+=(_,_)=>{dialogOpen=false;if(playerExitDialog==dialog)playerExitDialog=null;if(!leaving&&!stopped&&nativePlayer==expected){if(resume)expected.Play();FocusPlayerControl();}dialog.Dispose();};
        dialog.Show();dialog.Window?.SetBackgroundDrawable(new global::Android.Graphics.Drawables.ColorDrawable(Color.Transparent));dialog.Window?.SetLayout(Math.Min(Dp(640),Width-Dp(48)),-2);FocusLater(stay);
    }
    global::Android.Views.View? PlayerControl(string name){var id=Resources?.GetIdentifier(name,"id",Context.PackageName)??0;return id==0?null:nativePlayerView?.FindViewById(id);}
    bool PlayerControlsVisible=>nativePlayerView?.IsControllerFullyVisible==true;
    void FocusPlayerControl(string name="exo_play_pause")
    {
        nativePlayerView?.ShowController();var view=PlayerControl(name);if(view?.Enabled!=true||view.Visibility!=ViewStates.Visible)view=PlayerControl("exo_play_pause");
        if(view!=null){view.Focusable=true;view.FocusableInTouchMode=true;view.RequestFocus();}
    }
    void ConfigurePlayerFocus()
    {
        var names=new[]{"exo_prev","exo_rew_with_amount","exo_play_pause","exo_ffwd_with_amount","exo_settings","exo_subtitle"};var all=names.Select(PlayerControl).Where(v=>v!=null).Cast<global::Android.Views.View>().ToList();
        var signature=string.Join(";",all.Select(v=>$"{v.Id}:{v.Enabled}:{v.Visibility}"));if(signature==playerFocusSignature)return;var first=playerFocusSignature==null;playerFocusSignature=signature;
        foreach(var view in all){if(decoratedPlayerControls.Add(view.Id)){var original=view.Foreground;var outline=new global::Android.Graphics.Drawables.GradientDrawable();outline.SetColor(Color.Transparent);outline.SetCornerRadius(Dp(14));outline.SetStroke(Dp(3),accent);view.FocusChange+=(_,e)=>view.Foreground=e.HasFocus?outline:original;}}
        var controls=all.Where(v=>v.Visibility==ViewStates.Visible&&v.Enabled).ToList();foreach(var view in controls){view.Focusable=true;view.FocusableInTouchMode=true;}
        for(var i=0;i<controls.Count;i++){controls[i].NextFocusLeftId=i>0?controls[i-1].Id:global::Android.Views.View.NoId;controls[i].NextFocusRightId=i+1<controls.Count?controls[i+1].Id:global::Android.Views.View.NoId;}
        var progress=PlayerControl("exo_progress");var play=PlayerControl("exo_play_pause");if(progress is DefaultTimeBar bar)bar.SetKeyTimeIncrement(10000);if(progress!=null&&play!=null){progress.Focusable=true;progress.NextFocusUpId=play.Id;foreach(var control in controls)control.NextFocusDownId=progress.Id;}
        if(first)Post(()=>FocusPlayerControl());
    }
    VideoCatalog ReadVideoSources(string tabId,string pageUrl,string value)
    {
        var catalog=mediaCatalogs.GetOrAdd(tabId,_=>new());catalog.ClearPreference();
        try{using var doc=JsonDocument.Parse(value);if(doc.RootElement.ValueKind==JsonValueKind.Array)foreach(var item in doc.RootElement.EnumerateArray()){
            if(item.ValueKind!=JsonValueKind.Object)continue;var url=item.TryGetProperty("url",out var u)&&u.ValueKind==JsonValueKind.String?u.GetString():null;
            if(url!=null){var referrer=item.TryGetProperty("referrer",out var r)&&r.ValueKind==JsonValueKind.String?r.GetString()??pageUrl:pageUrl;if(!state.ShouldBlock(pageUrl)||!filters.Engine.Blocks(url,referrer,"media"))catalog.Add(url,referrer,item.TryGetProperty("title",out var t)&&t.ValueKind==JsonValueKind.String?t.GetString()??"":"",item.TryGetProperty("preferred",out var p)&&p.ValueKind==JsonValueKind.True);}
        }}catch(JsonException){}return catalog;
    }
    void RefreshVideoIndicator()
    {
        if(!state.NativeVideoEnabled||active==null||Web==null)return;var id=active;var url=Url;
        Evaluate("window.__rbMedia?.collect()??[]",value=>{if(stopped||active!=id||Url!=url||screen!="web")return;ReadVideoSources(id,url,value);UpdateVideoIndicator();});
    }
    void UpdateVideoIndicator()
    {
        if(findVideoButton==null||screen!="web")return;var count=active!=null&&mediaCatalogs.TryGetValue(active,out var catalog)?catalog.Count:0;
        findVideoButton.Selected=count>0;findVideoButton.ContentDescription=count>0?I18n.Format("Видео на странице · найдено: {0}",count):I18n.Text("Видео на странице · открыть в плеере Rbrowser");
        findVideoButton.SetImageDrawable(new IconDrawable("video-search",count>0?VideoAccent:text));
        if(count>0){var fill=new global::Android.Graphics.Drawables.GradientDrawable();fill.SetColor(VideoSurface);fill.SetCornerRadius(Dp(14));fill.SetStroke(Dp(findVideoButton.HasFocus?3:1),findVideoButton.HasFocus?text:VideoBorder);findVideoButton.Background=fill;}
        else findVideoButton.Background=Surface(findVideoButton.HasFocus);
        MaybeShowVideoNotice(count);
    }

    void FindVideos()
    {
        if(!state.NativeVideoEnabled||active==null||Web==null)return;
        var tabId=active;var pageUrl=Url;
        Evaluate("window.__rbMedia?.collect()??[]",value=>{
            if(!state.NativeVideoEnabled||active!=tabId||screen!="web")return;
            var catalog=ReadVideoSources(tabId,pageUrl,value);
            videoReturnTab=tabId;ShowVideoList(catalog.Snapshot());
        });
    }
    void ShowVideoList(List<DetectedVideo> videos)
    {
        screen="videos";var page=Page(I18n.Text("Видео на странице"));
        page.AddView(Label(I18n.Text("Выберите поток, чтобы открыть его во встроенном плеере Rbrowser."),16,muted));
        page.AddView(Label(I18n.Text("Назад на пульте — вернуться на сайт"),13,muted));
        if(videos.Count==0){page.AddView(Label(I18n.Text("Доступные потоки пока не найдены"),21));page.AddView(Label(I18n.Text("Запустите нужное видео на сайте и повторите поиск. Встроенный плеер открывает доступные HLS, DASH, MP4 и WebM; blob/MSE и защищённые потоки не всегда доступны отдельно."),16,muted));return;}
        global::Android.Views.View? first=null;
        for(var i=0;i<videos.Count;i++){
            var video=videos[i];var uri=new Uri(video.Url);var name=string.IsNullOrWhiteSpace(video.Title)?state.Tabs.FirstOrDefault(t=>t.Id==videoReturnTab)?.Title??I18n.Text("Видео ")+(i+1):video.Title;
            if(name.Length>90)name=name[..90]+"…";
            var button=Button(name+"\n"+video.Kind+" · "+uri.Host,()=>OpenNativeVideo(video),I18n.Text("Открыть видео ")+(i+1)+" · "+video.Kind);
            button.Gravity=GravityFlags.Left|GravityFlags.CenterVertical;page.AddView(button,new LinearLayout.LayoutParams(-1,Dp(68)){BottomMargin=Dp(10)});first??=button;
        }
        FocusLater(first);
    }
    void ReturnFromVideoList(){var id=videoReturnTab;videoReturnTab=null;if(id!=null&&state.Tabs.Any(t=>t.Id==id))Activate(id);else ShowHome();}
    void OpenNativeVideo(DetectedVideo video)
    {
        if(!state.NativeVideoEnabled||VideoCatalog.Kind(video.Url)==null)return;
        HideVideoNotice();
#if TV_DIAGNOSTICS
        global::Android.Util.Log.Info("RbrowserDiag","Native media path: "+new Uri(video.Url).AbsolutePath);
#endif
        CloseNativePlayer(false);PauseWeb();ExitVideoFullscreen();
        var headers=new Dictionary<string,string>();
        if(Urls.IsWeb(video.Referrer)){headers["Referer"]=video.Referrer;headers["Origin"]=new Uri(video.Referrer).GetLeftPart(UriPartial.Authority);}
        var http=new DefaultHttpDataSource.Factory();http.SetUserAgent(Web?.Settings.UserAgentString??WebSettings.GetDefaultUserAgent(Context));http.SetDefaultRequestProperties(headers);
        var sources=new DefaultMediaSourceFactory(Context);sources.SetDataSourceFactory(new ResolvingDataSource.Factory(http,new MediaCookieResolver()));
        try{
            var builder=new ExoPlayerBuilder(Context);builder.SetMediaSourceFactory(sources);builder.SetSeekBackIncrementMs(10000);builder.SetSeekForwardIncrementMs(10000);nativePlayer=builder.Build()??throw new InvalidOperationException("Player unavailable");
            nativePlayer.SetAudioAttributes(AudioAttributes.Default,true);nativePlayer.SetHandleAudioBecomingNoisy(true);
            nativePlayerView=new PlayerView(Context){Player=nativePlayer,UseController=true,ControllerShowTimeoutMs=5000,Focusable=true,FocusableInTouchMode=true};
            nativePlayerPanel=new FrameLayout(Context);nativePlayerPanel.SetBackgroundColor(Color.Black);nativePlayerPanel.AddView(nativePlayerView,new LayoutParams(-1,-1));
            nativePlayerHeader=Column();nativePlayerHeader.SetPadding(Dp(24),Dp(12),Dp(24),Dp(12));nativePlayerHeader.SetBackgroundColor(Color.Argb(205,23,27,32));
            nativePlayerHeader.AddView(Label(I18n.Text("Rbrowser · Видеоплеер"),18,accent));
            var status=Label(I18n.Text("Загрузка видео…"),13,muted);status.ContentDescription=I18n.Text("Состояние встроенного плеера");nativePlayerHeader.AddView(status);
            nativePlayerHeader.AddView(Label(I18n.Text("Стрелки — выбрать кнопку · OK — нажать · Меню — параметры · Назад — сайт"),12,muted));
            nativePlayerPanel.AddView(nativePlayerHeader,new LayoutParams(-1,-2,GravityFlags.Top));chrome.Visibility=ViewStates.Gone;AddView(nativePlayerPanel,new LayoutParams(-1,-1));
            var mime=video.Kind switch{"HLS"=>"application/x-mpegURL","DASH"=>"application/dash+xml","WebM"=>"video/webm",_=>"video/mp4"};
            var item=new MediaItem.Builder();item.SetUri(video.Url);item.SetMimeType(mime);nativePlayer.SetMediaItem(item.Build());nativePlayer.Prepare();nativePlayer.Play();nativePlayerView.RequestFocus();nativePlayerView.ShowController();lastPlayerKey=global::Android.OS.SystemClock.UptimeMillis();
            nativePlayerView.SetShowNextButton(false);Post(()=>FocusPlayerControl());
            var expected=nativePlayer;
            void Tick(){if(nativePlayer!=expected||nativePlayerPanel==null)return;var position=Math.Max(0,expected.CurrentPosition);var duration=expected.Duration;
                if(expected.PlaybackState==3)ConfigurePlayerFocus();
                var statusText=expected.PlayerError!=null?I18n.Text("Не удалось воспроизвести поток. Можно вернуться к веб-плееру сайта."):expected.PlaybackState==2?I18n.Text("Загрузка видео…"):(expected.IsPlaying?I18n.Text("Воспроизведение"):I18n.Text("Пауза"))+" · "+Time(position)+(duration>0?" / "+Time(duration):" · LIVE");
                if(status.Text!=statusText){status.Text=statusText;nativePlayerPanel.ContentDescription=I18n.Text("Встроенный плеер · ")+statusText;}
                nativePlayerHeader!.Visibility=expected.PlayerError!=null||!expected.IsPlaying||global::Android.OS.SystemClock.UptimeMillis()-lastPlayerKey<5000?ViewStates.Visible:ViewStates.Gone;
                PostDelayed(Tick,500);
            }
            Tick();
        }catch(Exception ex)when(ex is Java.Lang.Exception or InvalidOperationException){CloseNativePlayer(false);Info(I18n.Text("Встроенный плеер"),I18n.Text("Не удалось открыть поток. Продолжите просмотр в веб-плеере сайта."));}
    }
    static string Time(long milliseconds){var t=TimeSpan.FromMilliseconds(milliseconds);return t.TotalHours>=1?$"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}":$"{t.Minutes:00}:{t.Seconds:00}";}
    bool NativePlayerRemote(KeyEvent e)
    {
        lastPlayerKey=global::Android.OS.SystemClock.UptimeMillis();if(nativePlayerHeader!=null)nativePlayerHeader.Visibility=ViewStates.Visible;
        if(e.KeyCode==Keycode.Back){if(e.Action==KeyEventActions.Up)ConfirmPlayerExit();return true;}
        if(e.KeyCode==Keycode.Menu){if(e.Action==KeyEventActions.Up){FocusPlayerControl("exo_settings");PlayerControl("exo_settings")?.PerformClick();}return true;}
        if(e.KeyCode is Keycode.MediaPlayPause or Keycode.MediaPlay or Keycode.MediaPause){if(e.Action==KeyEventActions.Up&&nativePlayer!=null){if(e.KeyCode==Keycode.MediaPlay||e.KeyCode==Keycode.MediaPlayPause&&!nativePlayer.PlayWhenReady)nativePlayer.Play();else nativePlayer.Pause();}return true;}
        if(nativePlayer!=null){
            if(e.KeyCode is Keycode.MediaRewind or Keycode.MediaFastForward){if(e.Action==KeyEventActions.Down&&e.RepeatCount%3==0){if(e.KeyCode==Keycode.MediaRewind)nativePlayer.SeekBack();else nativePlayer.SeekForward();}return true;}
            if(!PlayerControlsVisible&&(e.KeyCode is Keycode.DpadCenter or Keycode.Enter or Keycode.DpadLeft or Keycode.DpadRight or Keycode.DpadUp or Keycode.DpadDown)){if(e.Action==KeyEventActions.Up){if(e.KeyCode is Keycode.DpadCenter or Keycode.Enter){if(nativePlayer.PlayWhenReady)nativePlayer.Pause();else nativePlayer.Play();}FocusPlayerControl();}return true;}
        }
        return false;
    }
    void CloseNativePlayer(bool returnToSite)
    {
        var player=nativePlayer;nativePlayer=null;playerFocusSignature=null;decoratedPlayerControls.Clear();
        var exitDialog=playerExitDialog;playerExitDialog=null;exitDialog?.Dismiss();
        if(nativePlayerPanel!=null)RemoveView(nativePlayerPanel);
        if(nativePlayerView!=null){nativePlayerView.Player=null;nativePlayerView.Dispose();nativePlayerView=null;}
        player?.Release();player?.Dispose();
        if(nativePlayerPanel!=null){RemoveView(nativePlayerPanel);nativePlayerPanel.Dispose();nativePlayerPanel=null;}nativePlayerHeader=null;chrome.Visibility=ViewStates.Visible;
        if(returnToSite)ReturnFromVideoList();
    }
    sealed class MediaCookieResolver:Java.Lang.Object,ResolvingDataSource.IResolver
    {
        public DataSpec? ResolveDataSpec(DataSpec? dataSpec)
        {
            if(dataSpec?.Uri==null)return dataSpec;
            // Look up cookies for each resource URI, never reuse a site's cookies on another CDN.
            var cookie=CookieManager.Instance?.GetCookie(dataSpec.Uri.ToString());
            var headers=new Dictionary<string,string>();if(dataSpec.HttpRequestHeaders!=null)foreach(System.Collections.DictionaryEntry entry in dataSpec.HttpRequestHeaders)if(entry.Key is string key&&entry.Value is string value&&!key.Equals("Cookie",StringComparison.OrdinalIgnoreCase))headers[key]=value;if(!string.IsNullOrEmpty(cookie))headers["Cookie"]=cookie;
            return dataSpec.WithRequestHeaders(headers);
        }
    }
}
