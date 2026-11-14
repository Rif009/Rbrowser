using Android.Graphics;
using Android.Graphics.Drawables;
using Paint = Android.Graphics.Paint;
using RectF = Android.Graphics.RectF;

namespace Rbrowser.Platforms.Android;
// One vector line icon system, drawn in device pixels by Android. No fonts or glyph dependencies.
public sealed class IconDrawable(string name, global::Android.Graphics.Color tint) : Drawable
{
    readonly Paint paint = new(PaintFlags.AntiAlias) { StrokeWidth = 1.7f, StrokeCap = Paint.Cap.Round, StrokeJoin = Paint.Join.Round };
    public override void Draw(Canvas canvas)
    {
        canvas.Save(); canvas.Translate(Bounds.Left, Bounds.Top); canvas.Scale(Bounds.Width() / 24f, Bounds.Height() / 24f);
        paint.Color = tint; paint.SetStyle(Paint.Style.Stroke);
        void Line(float x,float y,float a,float b)=>canvas.DrawLine(x,y,a,b,paint);
        void Shape(params float[] points) { using var p = new global::Android.Graphics.Path(); p.MoveTo(points[0],points[1]); for(var i=2;i<points.Length;i+=2)p.LineTo(points[i],points[i+1]); canvas.DrawPath(p,paint); }
        switch(name)
        {
            case "home": Shape(3,11,12,3,21,11); Shape(5,10,5,21,10,21,10,15,14,15,14,21,19,21,19,10); break;
            case "back": Shape(12,5,5,12,12,19); Line(5,12,21,12); break;
            case "forward": Shape(12,5,19,12,12,19); Line(3,12,19,12); break;
            case "refresh": canvas.DrawArc(new RectF(4,4,20,20),40,290,false,paint);Shape(15,3,20,4,19,9);break;
            case "tabs": canvas.DrawRoundRect(new RectF(3,7,18,21),2,2,paint);Shape(7,3,22,3,22,17);break;
            case "history": canvas.DrawArc(new RectF(4,4,21,21),-90,300,false,paint);Shape(3,4,3,10,9,10);Shape(12,7,12,13,16,15);break;
            case "star": Shape(12,2,15,8,22,9,17,14,18,21,12,18,6,21,7,14,2,9,9,8,12,2);break;
            case "shield": case "shield-off": Shape(12,2,21,6,20,14,17,19,12,22,7,19,4,14,3,6,12,2);if(name=="shield")Shape(7,12,11,16,17,9);else Line(3,3,21,21);break;
            case "cursor": Shape(5,2,5,19,10,15,14,22,18,20,14,13,21,12,5,2);break;
            case "remote": canvas.DrawRoundRect(new RectF(6,1,18,23),4,4,paint);canvas.DrawCircle(12,9,4,paint);Line(12,7,12,11);Line(10,9,14,9);Line(10,17,14,17);break;
            case "desktop": canvas.DrawRoundRect(new RectF(2,3,22,17),2,2,paint);Line(12,17,12,22);Line(7,22,17,22);break;
            case "video-search": canvas.DrawRoundRect(new RectF(2,3,19,16),2,2,paint);Shape(7,6,13,9.5f,7,13,7,6);canvas.DrawCircle(17,17,4,paint);Line(20,20,23,23);break;
            case "settings": Line(3,6,21,6);Line(3,12,21,12);Line(3,18,21,18);canvas.DrawCircle(8,6,2.5f,paint);canvas.DrawCircle(16,12,2.5f,paint);canvas.DrawCircle(10,18,2.5f,paint);break;
            case "close": Line(5,5,19,19);Line(19,5,5,19);break;
            case "clear": Line(5,6,19,6);Shape(7,6,8,21,16,21,17,6);Shape(9,6,9,3,15,3,15,6);Line(10,10,10,17);Line(14,10,14,17);break;
            case "edit": Shape(4,16,16,4,20,8,8,20,3,21,4,16);Line(13,7,17,11);break;
            case "search": canvas.DrawCircle(10,10,7,paint);Line(15,15,22,22);break;
            case "mic": canvas.DrawRoundRect(new RectF(9,2,15,15),3,3,paint);canvas.DrawArc(new RectF(5,6,19,19),0,180,false,paint);Line(12,19,12,23);break;
            case "plus": Line(12,4,12,20);Line(4,12,20,12);break;
            case "menu": Line(4,6,20,6);Line(4,12,20,12);Line(4,18,20,18);break;
            case "play": Shape(8,4,20,12,8,20,8,4);break;
            case "fullscreen":Shape(9,3,3,3,3,9);Shape(15,3,21,3,21,9);Shape(3,15,3,21,9,21);Shape(15,21,21,21,21,15);break;
            case "down": Shape(4,8,12,16,20,8);break;
        }
        canvas.Restore();
    }
    public override void SetAlpha(int alpha) => paint.Alpha = alpha;
    public override void SetColorFilter(ColorFilter? colorFilter) => paint.SetColorFilter(colorFilter);
    public override int Opacity => (int)Format.Translucent;
}
