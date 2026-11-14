using System.Diagnostics;
using Rbrowser.Core;

namespace Rbrowser.Platforms.Android;
public sealed class FilterService
{
    public static readonly (string Name, string Url)[] Lists = [
        ("ublock-ads.txt", "https://ublockorigin.github.io/uAssets/filters/filters.txt"),
        ("ublock-privacy.txt", "https://ublockorigin.github.io/uAssets/filters/privacy.txt"),
        ("ublock-unbreak.txt", "https://ublockorigin.github.io/uAssets/filters/unbreak.txt"),
        ("ublock-quick-fixes.txt", "https://ublockorigin.github.io/uAssets/filters/quick-fixes.txt"),
        ("easylist.txt", "https://easylist.to/easylist/easylist.txt"),
        ("ruadlist.txt", "https://easylist-downloads.adblockplus.org/ruadlist.txt")];
    volatile FilterEngine engine = new();
    public FilterEngine Engine => engine;
    public string Status { get; private set; } = I18n.Text("Загрузка фильтров…");
    public event Action? Changed;
    public bool Ready {get;private set;}
    readonly SemaphoreSlim gate = new(1, 1);
    readonly string directory = Path.Combine(FileSystem.AppDataDirectory, "filters");
    string CachePath=>Path.Combine(directory,$"compiled-{AppInfo.BuildString}-v2.bin");
    public async Task Load()
    {
        await gate.WaitAsync();
        try
        {
            var timer=Stopwatch.StartNew();
            await Task.Run(async ()=>{
                var cached=CachePath;
                if(File.Exists(cached))try{using var s=File.OpenRead(cached);engine=FilterEngine.Load(s);return;}catch(Exception ex)when(ex is IOException or ArgumentException or IndexOutOfRangeException){File.Delete(cached);}
                // AssetInputStream crosses JNI on ReadByte: buffer once before BinaryReader's small reads.
                using var bundled=await FileSystem.OpenAppPackageFileAsync("filters/compiled-v2.bin");using var memory=new MemoryStream();await bundled.CopyToAsync(memory);memory.Position=0;engine=FilterEngine.Load(memory);
            });
            Ready=true;Status=I18n.Format("Правил: {0:N0} · загрузка {1:F1} с",engine.Supported,timer.Elapsed.TotalSeconds);
            global::Android.Util.Log.Info("Rbrowser",$"FILTERS_READY {timer.ElapsedMilliseconds}ms rules={engine.Supported}");Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
    async Task Compile()
    {
        using var pslStream = await FileSystem.OpenAppPackageFileAsync("filters/public_suffix_list.dat");
        var psl = await new StreamReader(pslStream).ReadToEndAsync();
        var compiled = new FilterEngine(psl);
        foreach (var list in Lists.Append((Name:"rbrowser-fixes.txt",Url:"")))
        {
            var path = Path.Combine(directory, list.Name);
            string text;
            if (File.Exists(path)) text = await File.ReadAllTextAsync(path);
            else { using var stream = await FileSystem.OpenAppPackageFileAsync("filters/" + list.Name); text = await new StreamReader(stream).ReadToEndAsync(); }
            await Task.Run(() => compiled.Add(text));
        }
        engine = compiled;
        Directory.CreateDirectory(directory);var cache=CachePath;
        using(var output=File.Create(cache+".tmp"))compiled.Save(output);File.Move(cache+".tmp",cache,true);
        Ready=true;
        Status = I18n.Format("Поддерживаемых правил: {0:N0} · неподдерживаемых: {1:N0}",compiled.Supported,compiled.Skipped);
        Changed?.Invoke();
    }
    public async Task Update()
    {
        if (!await gate.WaitAsync(0)) return;
        try
        {
            Directory.CreateDirectory(directory);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            var errors = 0;
            foreach (var list in Lists)
            {
                try
                {
                    var text = await http.GetStringAsync(list.Url);
                    if (text.Length < 100 || !text.Contains('!') || text.TrimStart().StartsWith('<')) throw new IOException("Invalid list");
                    var file = Path.Combine(directory, list.Name);
                    await File.WriteAllTextAsync(file + ".tmp", text); File.Move(file + ".tmp", file, true);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) { errors++; }
            }
            await Compile();
            Status += errors == 0 ? I18n.Text(" · обновлены ") + DateTime.Now.ToString("dd.MM HH:mm") : I18n.Format(" · не обновлено списков: {0}; сохранена предыдущая версия",errors);
            Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
}
