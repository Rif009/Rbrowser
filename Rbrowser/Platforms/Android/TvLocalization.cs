using Android.App;
using AndroidX.AppCompat.App;
using AndroidX.Core.OS;
using Rbrowser.Core;
using AlertDialog=Android.App.AlertDialog;

namespace Rbrowser.Platforms.Android;

public sealed partial class TvBrowser
{
    void ApplyInterfaceLanguage()
    {
        var system=global::Android.Content.Res.Resources.System?.Configuration?.Locales?.Get(0)?.ToLanguageTag()??"en";
        I18n.Configure(state.InterfaceLanguage,system);
        var tags=state.InterfaceLanguage is "en" or "ru"?state.InterfaceLanguage:"";
        if(AppCompatDelegate.ApplicationLocales.ToLanguageTags()!=tags)AppCompatDelegate.ApplicationLocales=LocaleListCompat.ForLanguageTags(tags)!;
    }
    void SelectInterfaceLanguage()
    {
        var choices=new[]{I18n.Text("Системный"),"English",I18n.Text("Русский")};var values=new[]{"system","en","ru"};
        AlertDialog? dialog=null;var builder=new AlertDialog.Builder(Context);builder.SetTitle(I18n.Text("Язык интерфейса"));builder.SetSingleChoiceItems(choices.Select(I18n.Text).ToArray(),Array.IndexOf(values,state.InterfaceLanguage),(sender,e)=>{dialog?.Dismiss();_ = ChangeInterfaceLanguage(values[e.Which]);});builder.SetNegativeButton(I18n.Text("Отмена"),(_,_)=>{});dialog=ShowDialog(builder);
    }
    async Task ChangeInterfaceLanguage(string language)
    {
        state.InterfaceLanguage=language;await store.Save(state);ApplyInterfaceLanguage();
        if(!stopped&&Current==this)ShowSettings();
    }
}
