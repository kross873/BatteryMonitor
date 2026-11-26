using System.Drawing.Drawing2D;

namespace BatteryMonitor;

// Only fixed public project/community URLs. Rendering and clicking share the
// same geometry. Nothing is opened until a complete click on the sign.
internal static class DomovoiSign
{
    internal static bool IsSign(DomovoiMood mood) =>
        mood is DomovoiMood.GitHubSign or DomovoiMood.TikTokSign or DomovoiMood.TelegramSign;

    internal static string? Url(DomovoiMood mood) => mood switch
    {
        DomovoiMood.GitHubSign => "https://github.com/kross873/BatteryMonitor",
        DomovoiMood.TikTokSign => "https://www.tiktok.com/@domovoi.dev",
        DomovoiMood.TelegramSign => "https://t.me/podkrovatyuuu",
        _ => null
    };

    internal static double Reveal(double elapsed, double remaining)
    {
        double t = Math.Clamp(Math.Min(elapsed / .65, remaining / .45), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static (PointF Left, PointF Right) Hands(DomovoiPose pose) =>
        (DomovoiArt.ModelPoint(new DomovoiGrip(DomovoiPart.LeftHand, new PointF(53,379)),pose),
         DomovoiArt.ModelPoint(new DomovoiGrip(DomovoiPart.RightHand, new PointF(307,379)),pose));

    internal static RectangleF Bounds(DomovoiPose pose, double amount)
    {
        var (left,right) = Hands(pose);
        float centerX = (left.X + right.X) * .5f;
        float centerY = (left.Y + right.Y) * .5f;
        float width = Math.Clamp(right.X - left.X + 25, 230, 395);
        float height = 20 + (float)amount * 102;
        return new RectangleF(centerX - width / 2, centerY - height + 20, width, height);
    }

    internal static bool Hit(PointF pixel, int width, int height, double angle, int facing,
        DomovoiPose pose, double amount)
    {
        if (amount < .8) return false;
        var q = DomovoiArt.Rotate(new PointF(pixel.X - width / 2f, pixel.Y - height / 2f), -angle);
        float scale = DomovoiArt.ScaleFor(width,height);
        var model = new PointF(q.X / (scale * facing) + 180, q.Y / scale + 215 - pose.Bob);
        return Bounds(pose,amount).Contains(model);
    }

    internal static void Paint(Graphics g, DomovoiPose pose, DomovoiMood mood, int facing, double amount)
    {
        var (title,handle,background,accent) = mood switch
        {
            DomovoiMood.GitHubSign => ("GitHub","BatteryMonitor",Color.FromArgb(36,38,49),Color.FromArgb(187,202,235)),
            DomovoiMood.TikTokSign => ("TikTok","@domovoi.dev",Color.FromArgb(30,28,39),Color.FromArgb(91,237,228)),
            _ => ("Telegram","@podkrovatyuuu",Color.FromArgb(30,94,144),Color.FromArgb(181,232,255))
        };
        var r = Bounds(pose,amount);
        float radius = Math.Min(18, r.Height / 2);
        float d = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(r.Left,r.Top,d,d,180,90);
        path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);
        path.AddArc(r.Left,r.Bottom-d,d,d,90,90);
        path.CloseFigure();
        using var fill = new SolidBrush(background);
        using var edge = new Pen(accent,5);
        g.FillPath(fill,path);
        g.DrawPath(edge,path);
        if (amount > .6)
        {
            var state = g.Save();
            g.SetClip(path,CombineMode.Intersect);
            // Keep lettering readable when the character faces left.
            if (facing < 0)
            {
                float cx = r.X + r.Width / 2;
                g.TranslateTransform(cx,0);
                g.ScaleTransform(-1,1);
                g.TranslateTransform(-cx,0);
            }
            int alpha = (int)(255 * Math.Clamp((amount-.6)/.4,0,1));
            using var white = new SolidBrush(Color.FromArgb(alpha, Color.White));
            using var sub = new SolidBrush(Color.FromArgb(alpha, accent));
            using var bigFont = new Font("Segoe UI", 42, FontStyle.Bold, GraphicsUnit.Pixel);
            using var smallFont = new Font("Segoe UI", 27, FontStyle.Regular, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(title,bigFont,white,new RectangleF(r.X+8,r.Y+9,r.Width-16,53),format);
            g.DrawString(handle,smallFont,sub,new RectangleF(r.X+8,r.Y+61,r.Width-16,38),format);
            g.Restore(state);
        }
        // Mittens sit in front of the board's bottom corners, at the exact
        // hand joints. The board follows the blended arm pose.
        var (left,right) = Hands(pose);
        using var mitten = new SolidBrush(Color.FromArgb(195,186,200));
        using var outline = new Pen(Color.FromArgb(18,18,27),4);
        foreach (var point in new[] {left,right})
        {
            var hand = new RectangleF(point.X-16,point.Y-12,32,24);
            g.FillEllipse(mitten,hand);
            g.DrawEllipse(outline,hand);
        }
    }
}
