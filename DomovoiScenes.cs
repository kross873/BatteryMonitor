using System.Drawing.Drawing2D;

namespace BatteryMonitor;

// Small procedural props. All coordinates are in the existing 360x430 model;
// no bitmap assets, extra windows, per-frame region changes or new timers.
internal static class DomovoiScenes
{
    private static Color C(int rgb) => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    internal static void PaintProps(Graphics g, DomovoiPose pose, DomovoiMood mood, double elapsed)
    {
        float a = (float)Math.Clamp(elapsed / .6, 0, 1);
        if (a <= 0) return;
        float w = (float)Math.Sin(elapsed * 4);
        float pulse = Math.Abs(w);
        var right = DomovoiArt.ModelPoint(new DomovoiGrip(DomovoiPart.RightHand, new PointF(307,379)), pose);
        var left = DomovoiArt.ModelPoint(new DomovoiGrip(DomovoiPart.LeftHand, new PointF(53,379)), pose);
        void Line(int color, float width, params PointF[] p)
        {
            if (p.Length < 2) return;
            using var pen = new Pen(C(color), width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, p);
        }
        void Oval(float x, float y, float width, float height, int color, int edge = -1)
        {
            using var fill = new SolidBrush(C(color)); g.FillEllipse(fill,x,y,width,height);
            if (edge >= 0) { using var pen = new Pen(C(edge),4); g.DrawEllipse(pen,x,y,width,height); }
        }
        void Box(float x,float y,float width,float height,int color,int edge)
        {
            using var brush = new SolidBrush(C(color)); using var pen = new Pen(C(edge),4);
            g.FillRectangle(brush,x,y,width,height); g.DrawRectangle(pen,x,y,width,height);
        }
        void Star(float x,float y,float size,int color)
        {
            var p = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4; double radius = size * (i % 2 == 0 ? 1 : .24);
                p[i] = new(x + (float)(radius*Math.Cos(angle)),y + (float)(radius*Math.Sin(angle)));
            }
            using var fill = new SolidBrush(C(color)); g.FillPolygon(fill,p);
        }
        switch (mood)
        {
            case DomovoiMood.ChargeRising:
                // A little green growth gauge raised in the right mitten.
                {
                    float x = right.X - 22, y = right.Y - 68;
                    Box(x,y,44,60,0x283e36,0x9dd8ae);
                    float h = 9 + 37 * (float)((elapsed % 2.6) / 2.6);
                    using var green = new SolidBrush(C(0x91d9a7));
                    g.FillRectangle(green,x+7,y+51-h,30,h);
                    Line(0xf2ffd6,5,new(x+22,y+34),new(x+22,y+8));
                    Line(0xf2ffd6,5,new(x+12,y+18),new(x+22,y+8),new(x+32,y+18));
                }
                break;
            case DomovoiMood.ChargeFull:
                // Raised arms with small, soft celebration sparks.
                for (int i=0;i<6;i++)
                {
                    float x = i < 3 ? 30 + i*23 : 270+(i-3)*23;
                    float y = 90 + (i%3)*42 - pulse*13;
                    Star(x,y,(8+5*(float)Math.Abs(Math.Sin(elapsed*3+i)))*a,i%2==0?0xfad082:0x98daba);
                }
                break;
            case DomovoiMood.DeviceFound:
                // A compact device in the right hand, checked with a green tick.
                {
                    float x=right.X-30,y=right.Y-52;
                    Box(x,y,60,44,0x3c3959,0xb6a4d9);
                    Oval(x+7,y+11,10,10,0x9bdcb7);
                    Line(0xb5efd0,5,new(x+24,y+25),new(x+32,y+32),new(x+48,y+13));
                }
                break;
            case DomovoiMood.SignalLost:
                // Question mark floating beside the searching head.
                {
                    float x=34,y=103+9*w;
                    using var pen=new Pen(C(0xfad082),7){StartCap=LineCap.Round,EndCap=LineCap.Round};
                    g.DrawArc(pen,x,y,32,32,195,260);
                    Line(0xfad082,6,new(x+16,y+32),new(x+16,y+43));
                    Oval(x+12,y+48,8,8,0xfad082);
                }
                break;
            case DomovoiMood.CompatibilityInspect:
                // Lens follows the actual hand; the magnifier is not a clickable sign.
                {
                    float x=right.X,y=right.Y-37;
                    Line(0xb8a6d5,10,new(right.X,right.Y),new(x,y+10));
                    Oval(x-26,y-26,52,52,0x4b466c,0xe1cef0);
                    Line(0xdcd2ff,4,new(x-13,y-9),new(x-4,y-17));
                }
                break;
            case DomovoiMood.UpdateDelivery:
                // A parcel with a downward arrow, cradled between the two mittens.
                {
                    float x=(left.X+right.X)*.5f-47, y=(left.Y+right.Y)*.5f-54;
                    Box(x,y,94,62,0xa68660,0xf0ce98);
                    Line(0xf5dfaf,7,new(x+47,y+2),new(x+47,y+20));
                    Line(0x3b2c42,6,new(x+47,y+22),new(x+47,y+48));
                    Line(0x3b2c42,6,new(x+33,y+36),new(x+47,y+50),new(x+61,y+36));
                }
                break;
            case DomovoiMood.WidgetTune:
                // A tiny control strip with three animated slider knobs.
                {
                    float x=(left.X+right.X)*.5f-51,y=(left.Y+right.Y)*.5f-38;
                    Box(x,y,102,48,0x403252,0xd1b4f0);
                    for(int i=0;i<3;i++)
                    {
                        float xx=x+19+i*32;float yy=y+10+22*(.5f+.5f*(float)Math.Sin(elapsed*3+i*2));
                        Line(0xa291b9,4,new(xx,y+8),new(xx,y+40));
                        Oval(xx-6,yy-5,12,10,0xfad082);
                    }
                }
                break;
            case DomovoiMood.BatteryCriticalSign:
                Star(35,145,9+6*pulse,0xffb375);
                break;
            case DomovoiMood.BatteryEmptySign:
                Star(30,110,10+7*pulse,0xff899c);
                Star(325,115,10+7*(1-pulse),0xff899c);
                break;
            case DomovoiMood.TelegramSign:
                // Brief paper-plane accent on the outside of the hat.
                {
                    float x=298, y=75-22*pulse;
                    using var brush=new SolidBrush(C(0xc4e8ff));
                    g.FillPolygon(brush,new PointF[]{new(x-24,y+12),new(x+22,y),new(x+1,y+28),new(x-3,y+17)});
                    Line(0x427b9c,3,new(x-3,y+17),new(x+22,y));
                }
                break;
            case DomovoiMood.TikTokSign:
                // Music notes bounce gently while the sign sways.
                {
                    float y=100-15*pulse;
                    Line(0xa9efe5,5,new(315,y+22),new(315,y-7),new(331,y));
                    Oval(303,y+17,16,12,0xa9efe5);
                }
                break;
        }
    }
}
