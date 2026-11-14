using Android.App;
using Android.Content;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Speech;
using Android.Views;
using Android.Widget;
using Rbrowser.Core;
using Color=Android.Graphics.Color;

namespace Rbrowser.Platforms.Android;

public sealed partial class TvBrowser
{
    SpeechRecognizer? voiceRecognizer;
    VoiceListener? voiceListener;
    Dialog? voiceDialog;
    bool voiceListening;
    async void Voice()
    {
        if(voiceListening||voiceDialog!=null)return;
        if(!SpeechRecognizer.IsRecognitionAvailable(Context)){StartSystemVoice();return;}
        var permission=await Permissions.CheckStatusAsync<Permissions.Microphone>();if(permission!=PermissionStatus.Granted)permission=await Permissions.RequestAsync<Permissions.Microphone>();
        if(permission!=PermissionStatus.Granted){StartSystemVoice();return;}
        StartVoiceRecognition();
    }
    void StartVoiceRecognition()
    {
        StopVoice();var root=Column();root.SetPadding(Dp(28),Dp(24),Dp(28),Dp(24));var fill=Surface();root.Background=fill;
        root.AddView(Label(I18n.Text("Голосовой ввод"),24,accent));var status=Label(I18n.Text("Слушаю…"),22);root.AddView(status,new LinearLayout.LayoutParams(-1,Dp(65)){TopMargin=Dp(16)});
        root.AddView(Label(I18n.Text("Произнесите адрес сайта или поисковый запрос. Поиск откроется автоматически."),15,muted));
        var buttons=Row();buttons.SetPadding(0,Dp(20),0,0);var cancel=Button(I18n.Text("Отмена"),StopVoice);buttons.AddView(cancel,new LinearLayout.LayoutParams(0,Dp(48),1){MarginEnd=Dp(10)});buttons.AddView(Button(I18n.Text("Клавиатура"),()=>{StopVoice();Search();}),new LinearLayout.LayoutParams(0,Dp(48),1));root.AddView(buttons);
        voiceDialog=new Dialog(Context);voiceDialog.SetContentView(root);voiceDialog.SetCancelable(true);dialogOpen=true;voiceDialog.DismissEvent+=(_,_)=>{dialogOpen=false;if(voiceListening)StopVoice();};voiceDialog.Show();voiceDialog.Window?.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));voiceDialog.Window?.SetLayout(Dp(660),-2);FocusLater(cancel);
        try{
            voiceRecognizer=SpeechRecognizer.CreateSpeechRecognizer(Context);voiceListener=new VoiceListener(this,status);voiceRecognizer!.SetRecognitionListener(voiceListener);voiceListening=true;
            var intent=SpeechIntent();intent.PutExtra(RecognizerIntent.ExtraPartialResults,true);voiceRecognizer.StartListening(intent);
        }catch(Java.Lang.Exception){StopVoice();StartSystemVoice();}
    }
    Intent SpeechIntent()
    {
        var intent=new Intent(RecognizerIntent.ActionRecognizeSpeech);intent.PutExtra(RecognizerIntent.ExtraLanguageModel,RecognizerIntent.LanguageModelFreeForm);intent.PutExtra(RecognizerIntent.ExtraLanguage,I18n.SpeechLocale);intent.PutExtra(RecognizerIntent.ExtraMaxResults,1);intent.PutExtra(RecognizerIntent.ExtraPrompt,I18n.Text("Поиск в Rbrowser"));return intent;
    }
    void StartSystemVoice()
    {
        try{Activity.StartActivityForResult(SpeechIntent(),701);}
        catch(ActivityNotFoundException){Info(I18n.Text("Голосовой ввод"),I18n.Text("Сервис распознавания речи недоступен. Проверьте микрофон и сервис Google на телевизоре."));}
        catch(Java.Lang.SecurityException){Info(I18n.Text("Голосовой ввод"),I18n.Text("Не удалось получить доступ к микрофону. Проверьте разрешение приложения в настройках телевизора."));}
    }
    void StopVoice()
    {
        voiceListening=false;voiceRecognizer?.Cancel();voiceRecognizer?.Destroy();voiceRecognizer?.Dispose();voiceRecognizer=null;voiceListener?.Dispose();voiceListener=null;
        var dialog=voiceDialog;voiceDialog=null;dialog?.Dismiss();dialog?.Dispose();
    }
    public void SpeechCancelled(){if(!stopped)hint.Text=I18n.Text("Голосовой ввод отменён · можно повторить или использовать клавиатуру");}
    sealed class VoiceListener(TvBrowser owner,TextView status):Java.Lang.Object,IRecognitionListener
    {
        bool Active=>owner.voiceListening&&ReferenceEquals(owner.voiceListener,this);
        public void OnReadyForSpeech(Bundle? parameters){if(Active)status.Text=I18n.Text("Говорите…");}
        public void OnBeginningOfSpeech(){if(Active)status.Text=I18n.Text("Распознаю речь…");}
        public void OnEndOfSpeech(){if(Active)status.Text=I18n.Text("Распознаю речь…");}
        public void OnRmsChanged(float value){}
        public void OnBufferReceived(byte[]? buffer){}
        public void OnEvent(int eventType,Bundle? parameters){}
        public void OnPartialResults(Bundle? results){var text=results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault();if(Active&&!string.IsNullOrWhiteSpace(text))status.Text=text;}
        public void OnResults(Bundle? results){if(!Active)return;owner.voiceListening=false;var value=results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition)?.FirstOrDefault(s=>!string.IsNullOrWhiteSpace(s));owner.Post(()=>{if(!ReferenceEquals(owner.voiceListener,this))return;owner.StopVoice();if(!string.IsNullOrWhiteSpace(value))owner.Speech(value);else owner.Info(I18n.Text("Голосовой ввод"),I18n.Text("Речь не распознана. Повторите запрос и проверьте микрофон пульта."));});}
        public void OnError(SpeechRecognizerError error){if(!Active)return;owner.voiceListening=false;var code=(int)error;owner.Post(()=>{if(!ReferenceEquals(owner.voiceListener,this))return;owner.StopVoice();if(code is 3 or 5 or 8 or 9)owner.StartSystemVoice();else owner.Info(I18n.Text("Голосовой ввод"),code is 1 or 2 or 4?I18n.Text("Ошибка соединения с сервисом распознавания. Проверьте сеть и повторите запрос."):I18n.Text("Речь не распознана. Повторите запрос и проверьте микрофон пульта."));});}
    }
}
