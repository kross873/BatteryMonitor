using System.Drawing.Drawing2D;
using System.Globalization;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BatteryMonitor;

// Vector model from the domovoi animated series (episodes 2–6).
// Original 360-unit head/body artwork, with separate animated eyes and boots.
internal enum DomovoiMood { Idle, Held, Walking, Dizzy, Happy, Sleepy, Worried }

internal static class DomovoiArt
{
    private const string Hair = "M 64 138 Q 26 167 49 203 L 74 209 63 232 97 235 88 254 124 251 Q 148 281 181 264 Q 213 284 246 252 L 274 258 267 235 301 232 289 209 Q 326 188 297 155 L 270 124 Z";
    private const string Face = "M 81 126 Q 100 93 177 93 Q 255 93 279 126 L 270 184 Q 250 225 180 224 Q 102 220 87 184 Z";
    private const string Hat = "M 70 141 Q 57 108 84 74 Q 120 22 181 15 Q 226 4 254 34 L 296 105 Q 320 131 302 148 Q 262 163 224 149 Q 167 171 112 158 Z";
    private const string Brim = "M 63 144 Q 123 108 177 113 Q 245 106 299 139 Q 311 153 294 163 Q 243 147 180 154 Q 114 145 75 168 Q 53 163 63 144 Z";
    private const string Coat = "M 80 263 Q 130 232 180 239 Q 230 231 280 263 L 313 408 47 408 Z";
    private const string LeftArm = "M 92 283 Q 57 292 44 332 L 22 377 69 394 113 338";
    private const string RightArm = "M 268 283 Q 303 292 316 332 L 338 377 291 394 247 338";

    private static Color C(int rgb) => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

    // Restricted path reader: only the absolute M/L/Q/Z commands used by our
    // own fixed artwork. Quadratic curves are converted to cubic Beziers.
    private static GraphicsPath Path(string data)
    {
        var path = new GraphicsPath();
        string[] tokens = data.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        char cmd = 'M';
        PointF current = default, start = default;
        float N() => float.Parse(tokens[i++], CultureInfo.InvariantCulture);
        PointF P() => new(N(), N());
        while (i < tokens.Length)
        {
            if (char.IsLetter(tokens[i][0]))
            {
                cmd = tokens[i++][0];
                if (cmd == 'Z') { path.CloseFigure(); current = start; continue; }
            }
            switch (cmd)
            {
                case 'M':
                    current = start = P(); path.StartFigure(); cmd = 'L'; break;
                case 'L':
                    var next = P(); path.AddLine(current, next); current = next; break;
                case 'Q':
                    var q = P(); var end = P();
                    var a = new PointF(current.X + (q.X - current.X) * 2f / 3f, current.Y + (q.Y - current.Y) * 2f / 3f);
                    var b = new PointF(end.X + (q.X - end.X) * 2f / 3f, end.Y + (q.Y - end.Y) * 2f / 3f);
                    path.AddBezier(current, a, b, end); current = end; break;
                default: throw new InvalidOperationException("Invalid mascot path.");
            }
        }
        return path;
    }

    // Build a hit region directly from the rendered alpha, so rotating
    // limbs and the hat never get clipped by a fixed silhouette.
    internal static Region Shape(Bitmap image)
    {
        var area = new Rectangle(0, 0, image.Width, image.Height);
        var bits = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        using var path = new GraphicsPath(FillMode.Winding);
        try
        {
            var row = new byte[image.Width * 4];
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), row, 0, row.Length);
                int x = 0;
                while (x < image.Width)
                {
                    while (x < image.Width && row[x * 4 + 3] < 96) x++;
                    int left = x;
                    while (x < image.Width && row[x * 4 + 3] >= 96) x++;
                    if (x > left) path.AddRectangle(new Rectangle(left, y, x - left, 1));
                }
            }
        }
        finally { image.UnlockBits(bits); }
        return new Region(path);
    }

    internal static Bitmap Render(int width, int height, double time, double angle,
        double swing, DomovoiMood mood, int facing)
    {
        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(image);
            g.Clear(Color.Transparent);
            Paint(g, width, height, time, angle, swing, mood, facing);
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    private static void Paint(Graphics g, int width, int height, double time, double angle, double swing, DomovoiMood mood, int facing)
    {
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = Math.Min(width, height) * .72f / 430f;
            bool held = mood == DomovoiMood.Held, walking = mood == DomovoiMood.Walking;
            bool sleepy = mood == DomovoiMood.Sleepy, happy = mood == DomovoiMood.Happy;
            double wave = Math.Sin(time * (walking ? 12 : held ? 15 : 3));
            float bob = walking ? (float)(-Math.Abs(wave) * 10) : (float)(Math.Sin(time * 2.3) * (sleepy ? 2 : 4));
            g.TranslateTransform(width / 2f, height / 2f);
            g.RotateTransform((float)angle);
            g.ScaleTransform(scale * facing, scale);
            g.TranslateTransform(-180, -215 + bob);
            void Draw(string d, int fill = -1, int stroke = 0x12121b, float sw = 5)
            {
                using var path = Path(d);
                if (fill >= 0) { using var brush = new SolidBrush(C(fill)); g.FillPath(brush, path); }
                if (sw > 0) { using var pen = new Pen(C(stroke), sw) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }; g.DrawPath(pen, path); }
            }
            void Ellipse(float x, float y, float w, float h, int fill, int edge = 0x12121b, float sw = 4)
            {
                using var brush = new SolidBrush(C(fill)); g.FillEllipse(brush, x, y, w, h);
                if (sw > 0) { using var pen = new Pen(C(edge), sw); g.DrawEllipse(pen, x, y, w, h); }
            }
            void Joint(float x, float y, float rotation, Action draw)
            {
                var saved = g.Save();
                g.TranslateTransform(x, y);
                g.RotateTransform(rotation);
                g.TranslateTransform(-x, -y);
                draw();
                g.Restore(saved);
            }
            float kick = walking ? (float)(wave * 28) : held ? (float)(wave * 15 + swing * .3) : 0;
            // Legs are separate articulated pieces, not just sliding shoes.
            Joint(145, 388, kick, () => {
                Draw("M 134 381 L 160 381 161 408 135 408 Z", 0x403c50, sw: 3);
                Ellipse(127, 405, 39, 19, 0x373043, sw: 4);
            });
            Joint(215, 388, -kick, () => {
                Draw("M 200 381 L 226 381 225 408 199 408 Z", 0x403c50, sw: 3);
                Ellipse(194, 405, 39, 19, 0x373043, sw: 4);
            });
            Draw(Coat, 0x555368);
            Draw("M 145 253 L 180 298 215 253 M 180 298 L 180 383", -1, 0x918ca8, 4);
            float arm = held ? (float)(swing * .65 + wave * 12) : walking ? (float)(wave * 16) : (float)(wave * 2);
            Joint(92, 283, happy ? 26 : arm, () => {
                Draw(LeftArm, 0x66647c);
                Ellipse(23, 359, 60, 40, 0xc3bac8, sw: 5);
            });
            Joint(268, 283, happy ? -30 : -arm, () => {
                Draw(RightArm, 0x66647c);
                Ellipse(277, 359, 60, 40, 0xc3bac8, sw: 5);
            });

            // A separate head joint follows the body with a little lag.
            var head = g.Save();
            g.TranslateTransform(180, 255);
            g.RotateTransform(held ? (float)Math.Clamp(-swing * .18, -14, 14) :
                sleepy ? -7 : walking ? (float)(wave * 3) : 0);
            g.TranslateTransform(-180, -255);
            Draw(Hair, 0xa6a9bd);
            Draw(Face, 0x23232e);
            Draw(Hat, 0x464456);
            Draw("M 83 110 Q 100 49 174 29 Q 202 24 219 35 Q 165 49 137 106 Z", 0x5b596e, sw: 0);
            Draw("M 228 38 L 258 30 273 59 242 67 Z", 0x716d82, sw: 3);
            Draw("M 238 43 L 245 64 M 251 38 L 258 60", -1, 0xb6b1c1, 2);
            Draw(Brim, 0x646278);
            Draw("M 85 165 Q 112 156 141 162 Q 126 183 92 191 Z M 219 162 Q 254 153 278 165 L 273 192 Q 241 180 219 162 Z", 0xc3c4d4, sw: 0);
            Draw("M 179 205 Q 151 181 119 201 Q 104 215 91 211 Q 119 243 165 220 Q 175 216 179 205 Q 193 225 215 228 Q 245 232 266 211 Q 251 217 238 204 Q 209 182 179 205 Z", 0xd4d4df, sw: 4);
            Draw("M 113 233 Q 128 252 144 250 M 213 251 Q 230 249 242 237 M 169 245 Q 176 259 185 263", -1, 0x77788d, 3);

            bool blink = time % 4.4 > 4.22;
            if (sleepy || blink)
            {
                Draw("M 122 183 L 151 183 M 209 183 L 238 183", -1, 0xf4eaff, 6);
            }
            else if (mood == DomovoiMood.Dizzy)
            {
                Draw("M 124 169 L 150 194 M 150 169 L 124 194 M 210 169 L 236 194 M 236 169 L 210 194", -1, 0xf4eaff, 6);
            }
            else if (held || mood == DomovoiMood.Worried)
            {
                Ellipse(130, 166, 15, 25, 0xf3eaff, sw: 0);
                Ellipse(216, 166, 15, 25, 0xf3eaff, sw: 0);
                Draw("M 119 161 L 151 156 M 209 156 L 241 161", -1,
                    mood == DomovoiMood.Worried ? 0xf2b7b7 : 0xf4eaff, 5);
            }
            else if (happy)
            {
                Draw("M 118 187 Q 137 160 156 187 M 204 187 Q 224 160 243 187", -1, 0xfff4ff, 8);
            }
            else
                Draw("M 120 185 Q 137 167 153 185 M 207 185 Q 224 167 240 185", -1, 0xf4eaff, 6);
            g.Restore(head);
        }
        finally { g.Restore(state); }
    }
}
