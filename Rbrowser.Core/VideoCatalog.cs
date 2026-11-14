namespace Rbrowser.Core;

public sealed record DetectedVideo(string Url,string Referrer,string Title,string Kind,bool Preferred=false);

// Bounded, per-page catalogue. It never stores blob URLs, media segments or a response body.
public sealed class VideoCatalog
{
    readonly object gate=new();
    readonly Dictionary<string,DetectedVideo> videos=[];
    public int Count {get{lock(gate)return videos.Count;}}
    public static string? Kind(string url)
    {
        if(url.Length>32768||!Urls.IsWeb(url)||!Uri.TryCreate(url,UriKind.Absolute,out var uri))return null;
        return Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch {".m3u8"=>"HLS",".mpd"=>"DASH",".mp4" or ".m4v"=>"MP4",".webm"=>"WebM",_=>null};
    }
    public void Add(string url,string referrer,string title="",bool preferred=false)
    {
        var kind=Kind(url);if(kind==null)return;
        var uri=new Uri(url);var key=uri.GetLeftPart(UriPartial.Path);
        lock(gate){if(videos.TryGetValue(key,out var previous)){if(string.IsNullOrWhiteSpace(title))title=previous.Title;preferred|=previous.Preferred;}videos.Remove(key);videos[key]=new(url,Urls.IsWeb(referrer)&&referrer.Length<=32768?referrer:"",title.Length>160?title[..160]:title,kind,preferred);if(videos.Count>60)videos.Remove(videos.Keys.First());}
    }
    public List<DetectedVideo> Snapshot(){lock(gate)return videos.Values.OrderBy(v=>v.Preferred?0:v.Kind=="HLS"&&v.Url.Contains("/channel/hls/",StringComparison.OrdinalIgnoreCase)?1:2).ToList();}
    public void ClearPreference(){lock(gate)foreach(var key in videos.Keys.ToArray())videos[key]=videos[key] with {Preferred=false};}
    public void Clear(){lock(gate)videos.Clear();}
}
