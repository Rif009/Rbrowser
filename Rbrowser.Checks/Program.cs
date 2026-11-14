using Rbrowser.Core;
using System.Diagnostics;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
var engine=new FilterEngine("com\nco.uk\ngithub.io\n*.ck\n!www.ck");
engine.Add("""
||ads.example.com^
@@||ads.example.com/allowed.js$script
||ads.example.com/forced.js$script,important
@@||ads.example.com/forced.js$script
||typed.example^$script,3p,domain=example.co.uk|~safe.example.co.uk
||unsupported.example^$redirect=noop.js
||disabled.example^
||disabled.example^$badfilter
adfragment
/*.php?uid=$script,domain=vost.pw
vost.pw##.admachina-banner
!#if cap_html_filtering
||conditional.example^
!#else
||otherbranch.example^
!#endif
example.com##.banner
example.com#@#.banner
example.com##.advert
example.com##+js(noop)
""");
Check(engine.Blocks("https://ads.example.com/ad.js","https://site.com","script"),"domain blocking");
Check(!engine.Blocks("https://ads.example.com.evil.test/ad.js","https://site.com","script"),"host boundary");
Check(!engine.Blocks("https://ads.example.com/allowed.js","https://site.com","script"),"exception");
Check(engine.Blocks("https://ads.example.com/forced.js","https://site.com","script"),"important rule precedence");
Check(engine.Blocks("https://typed.example/ad.js","https://www.example.co.uk","script"),"type and source domain");
Check(!engine.Blocks("https://typed.example/ad.js","https://www.example.co.uk","image"),"resource type exclusion");
Check(!engine.Blocks("https://typed.example/ad.js","https://safe.example.co.uk","script"),"excluded source");
Check(!engine.Blocks("https://unsupported.example/","https://site.com","script"),"unsupported modifiers must not broaden blocking");
Check(!engine.Blocks("https://disabled.example/","https://site.com","script"),"badfilter disables rule");
Check(engine.Blocks("https://site.com/prefixadfragmentsuffix","https://site.com","other"),"token index preserves substring semantics");
Check(engine.Blocks("https://v14.vost.pw/a65e2320c4635c23e62be422be.php?uid=a276","https://v14.vost.pw/","script"),"Vost ad fallback path is not mistaken for regex");
Check(!engine.Blocks("https://v14.vost.pw/frame.php?play=123","https://v14.vost.pw/","subdocument"),"Vost player is preserved");
Check(!engine.Blocks("https://conditional.example/","https://site.com","other"),"conditional directives");
Check(!engine.Blocks("https://otherbranch.example/","https://site.com","other"),"conditional else");
Check(!engine.Css("example.com").Contains(".banner"),"cosmetic exception");
Check(engine.Css("www.example.com").Contains(".advert"),"domain cosmetics");
Check(!engine.Css("evil-example.com").Contains(".advert"),"cosmetic domain boundary");
engine.Add("vost.pw##div.ad:has(iframe[src*=ad])\nvost.pw##div:has-text(sponsor)");
Check(engine.Css("v14.vost.pw").Contains(":has(iframe")&&!engine.Css("v14.vost.pw").Contains(":has-text"),"native CSS has supported, procedural scriptlets skipped");
using(var prepared=new MemoryStream()){
engine.Save(prepared);prepared.Position=0;var roundtrip=FilterEngine.Load(prepared);
Check(!roundtrip.Blocks("https://ads.example.com/allowed.js","https://site.com","script"),"prepared exceptions");
Check(roundtrip.Blocks("https://ads.example.com/forced.js","https://site.com","script"),"prepared important rules");
Check(!roundtrip.Blocks("https://disabled.example/","https://site.com","script"),"prepared badfilter");
Check(roundtrip.Css("v14.vost.pw").Contains(":has(iframe"),"prepared native cosmetic selectors");
}
var suffix=new PublicSuffix("com\nco.uk\ngithub.io\n*.ck\n!www.ck");
Check(suffix.Site("a.example.co.uk")=="example.co.uk","public suffix co.uk");
Check(suffix.Site("a.user.github.io")=="user.github.io","private suffix");
Check(suffix.Site("a.www.ck")=="www.ck","PSL exception");
Check(Urls.Resolve("twitch.tv")=="https://twitch.tv/","address normalization");
Check(Urls.Resolve("аниме онлайн").StartsWith("https://www.google.com/search?q="),"search");
Check(!Urls.IsWeb("javascript:alert(1)"),"unsafe scheme");
var temp=Path.Combine(Path.GetTempPath(),"rbrowser-check-"+Guid.NewGuid().ToString("N"));
try {
var store=new StateStore(temp);var state=new BrowserState();state.Favorites.Clear();state.Favorites.Add(new("vost","Vost","https://v14.vost.pw/"));state.Tabs.Add(new("one","Twitch","https://twitch.tv/"));state.For("https://twitch.tv/").BlockAds=false;state.Record("Twitch","https://twitch.tv/");
state.BlockAdsEnabled=false;state.TwitchVideoAdsEnabled=false;state.CursorMode=true;state.NativeVideoEnabled=false;state.InterfaceLanguage="en";
await store.Save(state);var loaded=await store.Load();Check(loaded.Favorites.Count==0&&loaded.Tabs.Count==1&&!loaded.For("https://twitch.tv/").BlockAds&&loaded.History.Count==1,"persistent browser session");
Check((await store.Load()).DefaultsVersion==1,"starter bookmark migration is persisted without deleting session data");
Check(!loaded.BlockAdsEnabled&&!loaded.TwitchVideoAdsEnabled&&loaded.CursorMode&&!loaded.NativeVideoEnabled,"global ad, player and control settings persist");
Check(loaded.InterfaceLanguage=="en","interface language choice persists");
Check(!loaded.ShouldBlock("https://example.com/"),"global off overrides site-enabled default");loaded.BlockAdsEnabled=true;
Check(loaded.ShouldBlock("https://example.com/")&&!loaded.ShouldBlock("https://twitch.tv/"),"site exceptions survive global toggle");
Check(!loaded.IsDesktop("https://example.com/"),"desktop default starts off");loaded.DesktopModeDefault=true;
Check(loaded.IsDesktop("https://example.com/")&&loaded.IsDesktop("https://new.example/"),"global desktop default applies to visited and new sites");
loaded.SetDesktop("https://example.com/",false);Check(!loaded.IsDesktop("https://example.com/"),"per-site normal mode overrides global desktop default");
loaded.SetDesktop("https://explicit.example/",true);loaded.DesktopModeDefault=false;
Check(loaded.IsDesktop("https://explicit.example/")&&!loaded.IsDesktop("https://new.example/"),"per-site desktop mode survives global default off");
loaded.For("https://legacy.example/").Desktop=true;Check(loaded.IsDesktop("https://legacy.example/"),"legacy desktop preference is preserved");
loaded.ResetDesktop("https://legacy.example/");Check(!loaded.IsDesktop("https://legacy.example/"),"reset returns legacy site to global default");
loaded.DesktopModeDefault=true;await store.Save(loaded);var desktopLoaded=await store.Load();
Check(desktopLoaded.DesktopModeDefault&&!desktopLoaded.IsDesktop("https://example.com/")&&desktopLoaded.IsDesktop("https://explicit.example/"),"global desktop default and both override values persist");
desktopLoaded.ResetDesktop("https://example.com/");Check(desktopLoaded.IsDesktop("https://example.com/"),"reset normal exception inherits global desktop mode");
} finally {if(Directory.Exists(temp))Directory.Delete(temp,true);}
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Rbrowser/Resources/Raw/filters"));
var historyNow=DateTimeOffset.UtcNow;
Check(new BrowserState().CursorMode&&new BrowserState().Favorites.Count==0,"fresh installation defaults to cursor and empty favorites");
var defaults=new BrowserState{CursorMode=false,Favorites=[new("vost","Vost","https://v14.vost.pw/"),new("twitch","Twitch","https://www.twitch.tv/"),new("mine","My site","https://example.com/")]};
Check(defaults.ApplyDefaults()&&defaults.Favorites.Count==1&&defaults.Favorites[0].Id=="mine"&&!defaults.CursorMode,"migration removes unchanged starter bookmarks but preserves personal choices");
Check(!defaults.ApplyDefaults(),"starter migration is idempotent");
defaults=new BrowserState{Favorites=[new("vost","Renamed by user","https://v14.vost.pw/"),new("twitch","Twitch","https://example.com/")]};defaults.ApplyDefaults();Check(defaults.Favorites.Count==2,"edited starter bookmarks are preserved");
var historyState=new BrowserState();Check(historyState.HistoryRetentionDays==30,"history defaults to one month");
historyState.History=[new("Recent","https://example.com/recent",historyNow.AddDays(-29)),new("Boundary","https://example.com/boundary",historyNow.AddDays(-30)),new("Expired","https://example.com/expired",historyNow.AddDays(-31))];
Check(historyState.PruneHistory(historyNow)&&historyState.History.Count==2&&historyState.History[1].Title=="Boundary","retention keeps boundary and removes expired visits");
historyState.HistoryRetentionDays=7;historyState.PruneHistory(historyNow);Check(historyState.History.Count==0,"reducing retention removes older visits immediately");
historyState.HistoryRetentionDays=90;historyState.History=[new("Two months","https://example.com/old",historyNow.AddDays(-60))];Check(!historyState.PruneHistory(historyNow)&&historyState.History.Count==1,"three-month retention keeps older visits");
historyState.HistoryRetentionDays=-1;historyState.PruneHistory(historyNow);Check(historyState.HistoryRetentionDays==30&&historyState.History.Count==0,"invalid retention safely falls back to month");
historyState.History=Enumerable.Range(0,20000).Select(i=>new Visit($"Visit {i}",$"https://example.com/{i}",historyNow.AddMinutes(-i))).ToList();
var historyTimer=Stopwatch.StartNew();historyState.Record("Latest","https://example.com/latest");Check(historyState.History.Count==20001&&historyState.History[0].Title=="Latest","large recent history is not truncated to old 500-entry cap");
historyState.Record("Latest updated","https://example.com/latest");Check(historyState.History.Count==20001&&historyState.History[0].Title=="Latest updated","consecutive duplicate visit updates in large history");
var historyDirectory=Path.Combine(Path.GetTempPath(),"rbrowser-history-"+Guid.NewGuid().ToString("N"));
try{
 var historyStore=new StateStore(historyDirectory);historyState.HistoryRetentionDays=90;historyState.History.Add(new("Expired","https://example.com/expired",historyNow.AddDays(-100)));await historyStore.Save(historyState);
 var restoredHistory=await historyStore.Load();Check(restoredHistory.HistoryRetentionDays==90&&restoredHistory.History.Count==20001,"large history persistence and expiry on startup");
 var disk=await File.ReadAllTextAsync(Path.Combine(historyDirectory,"browser.json"));Check(!disk.Contains("https://example.com/expired"),"startup expiry is persisted to disk");
 Console.WriteLine($"History stress: {restoredHistory.History.Count} visits, record/save/load/prune {historyTimer.ElapsedMilliseconds} ms, JSON {new FileInfo(Path.Combine(historyDirectory,"browser.json")).Length:N0} bytes");
}finally{if(Directory.Exists(historyDirectory))Directory.Delete(historyDirectory,true);}
Check(new BrowserState().InterfaceLanguage=="system","interface follows system language by default");
Check(I18n.Resolve("system","ru-RU")=="ru"&&I18n.Resolve("system","en-GB")=="en","system locale resolution");
Check(I18n.Resolve("en","ru-RU")=="en"&&I18n.Resolve("ru","en-US")=="ru","explicit locale overrides system");
Check(I18n.Resolve("system","de-DE")=="en","unsupported system locale falls back to English");
I18n.Configure("en","ru-RU");Check(I18n.Text("Настройки")=="Settings"&&I18n.Text("Голосовой ввод")=="Voice search","embedded English resources loaded");
Check(I18n.Format("Видео на странице · найдено: {0}",3)=="Videos on this page · found: 3","translated dynamic stream count");
Check(I18n.Text("Пользовательское название") == "Пользовательское название","unknown user content is preserved");
Check(I18n.SpeechLocale=="en-US","speech follows selected interface language");
I18n.Configure("system","ru-RU");Check(I18n.Text("Настройки")=="Настройки"&&I18n.SpeechLocale=="ru-RU","system Russian and speech locale restored");
Check(new BrowserState().NativeVideoEnabled,"native video discovery is enabled by default");
Check(VideoCatalog.Kind("https://cdn.example/live.M3U8?token=secret")=="HLS"&&VideoCatalog.Kind("https://cdn.example/video.mpd")=="DASH","HLS and DASH source recognition");
Check(VideoCatalog.Kind("blob:https://example.com/id")==null&&VideoCatalog.Kind("file:///video.mp4")==null&&VideoCatalog.Kind("https://cdn.example/segment.ts")==null,"blob, local files and segments excluded");
Check(VideoCatalog.Kind("https://cdn.example/api?file=video.mp4")==null,"filename in unrelated query is not mistaken for video");
var catalog=new VideoCatalog();catalog.Add("https://cdn.example/video.mp4?token=old","https://frame.example/player/","Episode");catalog.Add("https://cdn.example/video.mp4?token=new","https://frame.example/player/");
Check(catalog.Snapshot().Count==1&&catalog.Snapshot()[0].Url.EndsWith("token=new")&&catalog.Snapshot()[0].Title=="Episode","media catalogue refreshes signed URL while preserving title");
Check(catalog.Snapshot()[0].Referrer=="https://frame.example/player/","media frame referrer retained");
catalog.Add("https://cdn.example/current-episode.mp4","https://frame.example/",preferred:true);Check(catalog.Snapshot()[0].Url.EndsWith("current-episode.mp4"),"currently selected media precedes older episode requests");
catalog.ClearPreference();Check(catalog.Snapshot().All(v=>!v.Preferred),"rescan resets stale current-episode priority");
for(var i=0;i<80;i++)catalog.Add($"https://cdn.example/{i}.mp4","");Check(catalog.Snapshot().Count==60,"media catalogue bounded to 60 sources");catalog.Clear();Check(catalog.Snapshot().Count==0,"navigation clears media catalogue");
if(Directory.Exists(root)){
var timer=Stopwatch.StartNew();var real=new FilterEngine(await File.ReadAllTextAsync(Path.Combine(root,"public_suffix_list.dat")));
foreach(var file in Directory.GetFiles(root,"*.txt"))real.Add(await File.ReadAllTextAsync(file));
Check(real.Supported>10000,"bundled real filter lists are loaded");
Check(real.Blocks("https://googleads.g.doubleclick.net/pagead/ads?client=1","https://example.com/","script"),"real-list ad request");
Check(real.Blocks("https://v14.vost.pw/a65e2320c4635c23e62be422be.php?uid=a276","https://v14.vost.pw/","script"),"regional list blocks Vost fallback");
Check(real.Blocks("https://piwelefko.win/vd/stpre/id","https://v14.vost.pw/","other"),"Vost ad scripts with unknown WebView resource type");
Check(!real.Blocks("https://v14.vost.pw/mplay/index.php?id=2147421308","https://v14.vost.pw/","subdocument"),"regional filters preserve Vost video frame");
var init=timer.Elapsed;timer.Restart();for(var i=0;i<1000;i++)real.Blocks("https://static-cdn.jtvnw.net/jtv_user_pictures/example.png","https://www.twitch.tv/","image");
Console.WriteLine($"Real filters: {real.Supported} supported, {real.Skipped} skipped; load {init.TotalMilliseconds:N0} ms; 1000 requests {timer.ElapsedMilliseconds} ms");
using var snapshot=new MemoryStream();real.Save(snapshot);snapshot.Position=0;timer.Restart();var loadedFilters=FilterEngine.Load(snapshot);
Check(loadedFilters.Supported==real.Supported&&loadedFilters.Blocks("https://googleads.g.doubleclick.net/pagead/ads?client=1","https://example.com/","script"),"prepared filters preserve network rules");
Check(loadedFilters.Css("example.com")==real.Css("example.com"),"prepared filters preserve cosmetics");
Console.WriteLine($"Prepared snapshot load {timer.ElapsedMilliseconds} ms; {snapshot.Length:N0} bytes");
if(args.Contains("--compile")){using var output=File.Create(Path.Combine(root,"compiled-v2.bin"));real.Save(output);Console.WriteLine("Bundled filter snapshot generated");}
}
Console.WriteLine($"PASS: {checks} checks");
