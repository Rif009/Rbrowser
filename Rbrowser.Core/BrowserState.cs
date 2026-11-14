using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rbrowser.Core;

public sealed record Bookmark(string Id, string Title, string Url);
public sealed record Tab(string Id, string Title, string Url);
public sealed record Visit(string Title, string Url, DateTimeOffset At);
public sealed class SitePreferences
{
    public bool BlockAds { get; set; } = true;
    public bool Desktop { get; set; }
    public bool? DesktopOverride { get; set; }
    public int Zoom { get; set; } = 100;
}
public sealed class BrowserState
{
    public bool BlockAdsEnabled {get;set;} = true;
    public bool TwitchVideoAdsEnabled {get;set;} = true;
    public bool CursorMode {get;set;} = true;
    public bool DesktopModeDefault {get;set;}
    public bool NativeVideoEnabled {get;set;} = true;
    public string InterfaceLanguage {get;set;} = "system";
    public int HistoryRetentionDays {get;set;} = 30;
    public int DefaultsVersion {get;set;}
    public List<Bookmark> Favorites { get; set; } = [];
    public List<Tab> Tabs { get; set; } = [];
    public List<Visit> History { get; set; } = [];
    public Dictionary<string, SitePreferences> Sites { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public SitePreferences For(string url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.IdnHost : "";
        if (!Sites.TryGetValue(host, out var p)) Sites[host] = p = new();
        return p;
    }
    public bool ShouldBlock(string url)=>BlockAdsEnabled&&For(url).BlockAds;
    // Legacy Desktop=true remains an explicit preference; old implicit false follows the new default.
    public bool IsDesktop(string url){var prefs=For(url);return prefs.DesktopOverride??(prefs.Desktop||DesktopModeDefault);}
    public void SetDesktop(string url,bool enabled)=>For(url).DesktopOverride=enabled;
    public void ResetDesktop(string url){var prefs=For(url);prefs.Desktop=false;prefs.DesktopOverride=null;}
    public bool ApplyDefaults()
    {
        if(DefaultsVersion>=1)return false;
        Favorites.RemoveAll(b=>b is {Id:"vost",Title:"Vost",Url:"https://v14.vost.pw/"} or {Id:"twitch",Title:"Twitch",Url:"https://www.twitch.tv/"});
        DefaultsVersion=1;return true;
    }
    public void Record(string title, string url)
    {
        if (!Urls.IsWeb(url)) return;
        if (History.FirstOrDefault()?.Url == url) History.RemoveAt(0);
        History.Insert(0, new(title, url, DateTimeOffset.Now));
        PruneHistory();
    }
    public bool PruneHistory(DateTimeOffset? now=null)
    {
        if(HistoryRetentionDays is not (7 or 30 or 90 or 365))HistoryRetentionDays=30;
        var cutoff=(now??DateTimeOffset.Now).AddDays(-HistoryRetentionDays);
        return History.RemoveAll(v=>v.At<cutoff||!Urls.IsWeb(v.Url))>0;
    }
}
public static class Urls
{
    public static bool IsWeb(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" && !string.IsNullOrEmpty(u.Host);
    public static string Resolve(string input)
    {
        input = input.Trim();
        if (IsWeb(input)) return new Uri(input).AbsoluteUri;
        if (!input.Contains(' ') && input.Contains('.') && !input.Contains("://") && IsWeb("https://" + input)) return new Uri("https://" + input).AbsoluteUri;
        return "https://www.google.com/search?q=" + Uri.EscapeDataString(input);
    }
}
public sealed class StateStore(string directory)
{
    readonly string path = Path.Combine(directory, "browser.json");
    readonly SemaphoreSlim gate = new(1, 1);
    public async Task<BrowserState> Load()
    {
        try { var state=JsonSerializer.Deserialize(await File.ReadAllTextAsync(path),StateJsonContext.Default.BrowserState) ?? new();var changed=state.ApplyDefaults();changed|=state.PruneHistory();if(changed){try{await Save(state);}catch(IOException){/* Keep the successfully loaded session if maintenance cannot be persisted yet. */}}return state; }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }
    public async Task Save(BrowserState state)
    {
        // Serialize before yielding: the UI owns mutations, the writer owns the file.
        var json = JsonSerializer.Serialize(state, StateJsonContext.Default.BrowserState);
        await gate.WaitAsync();
        try { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(path + ".tmp", json); File.Move(path + ".tmp", path, true); }
        finally { gate.Release(); }
    }
}
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BrowserState))]
[JsonSerializable(typeof(string))]
public partial class StateJsonContext : JsonSerializerContext { }
