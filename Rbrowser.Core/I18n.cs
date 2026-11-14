using System.Globalization;
using System.Text.Json;

namespace Rbrowser.Core;

public static class I18n
{
    static readonly Dictionary<string,string> english=LoadEnglish();
    public static string Language {get;private set;}="ru";
    public static string SpeechLocale=>Language=="ru"?"ru-RU":"en-US";
    public static string Resolve(string choice,string system)=>((choice is "en" or "ru"?choice:system).StartsWith("ru",StringComparison.OrdinalIgnoreCase))?"ru":"en";
    public static void Configure(string choice,string system)
    {
        Language=Resolve(choice,system);var culture=CultureInfo.GetCultureInfo(SpeechLocale);
        CultureInfo.DefaultThreadCurrentUICulture=culture;CultureInfo.DefaultThreadCurrentCulture=culture;CultureInfo.CurrentUICulture=culture;CultureInfo.CurrentCulture=culture;
    }
    public static string Text(string russian)=>Language=="en"&&english.TryGetValue(russian,out var value)?value:russian;
    public static string Format(string russian,params object?[] values)=>string.Format(CultureInfo.CurrentCulture,Text(russian),values);
    static Dictionary<string,string> LoadEnglish()
    {
        using var stream=typeof(I18n).Assembly.GetManifestResourceStream("Rbrowser.Core.Resources.strings.en.json");if(stream==null)return [];
        using var doc=JsonDocument.Parse(stream);return doc.RootElement.EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()??p.Name);
    }
}
