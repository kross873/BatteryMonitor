using System.Drawing.Drawing2D;
using System.Globalization;

namespace BatteryMonitor;

// Vector model from the domovoi animated series (episodes 2–6).
// Original 360-unit head/body artwork, with separate animated eyes and boots.
internal enum DomovoiMood { Idle, Held, Walking, Dizzy, Happy, Sleepy, Worried, Petting, Wave, Dance, Stretch, Annoyed, Sitting, Tea, Coding, Perched, GitHubSign, TikTokSign, TelegramSign, BatterySign, BatteryCriticalSign, BatteryEmptySign, ChargeRising, ChargeFull, DeviceFound, SignalLost, CompatibilityInspect, UpdateDelivery, WidgetTune, Guide }

internal enum DomovoiPart { Body, Head, LeftHand, RightHand, LeftFoot, RightFoot, Hat, Beard }
internal readonly record struct DomovoiGrip(DomovoiPart Part, PointF Point);
internal readonly record struct DomovoiPose(float Bob, float LeftArm, float RightArm, float LeftLeg, float RightLeg, float Head, float Hat = 0, float Beard = 0);

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

    private static readonly Dictionary<string, GraphicsPath> Paths = new();
    private static GraphicsPath Cached(string d)
    {
        if (!Paths.TryGetValue(d, out var path)) Paths[d] = path = Path(d);
        return path; // immutable after creation; UI thread only
    }

    internal static float ScaleFor(int width, int height) => Math.Min(width, height) * .72f / 430f;
    internal static DomovoiPose Pose(double time, double swing, DomovoiMood mood)
    {
        bool held = mood == DomovoiMood.Held, walking = mood == DomovoiMood.Walking;
        bool sleepy = mood == DomovoiMood.Sleepy, happy = mood == DomovoiMood.Happy;
        double wave = Math.Sin(time * (walking ? 12 : held ? 11 : 3));
        float bob = held ? 0 : walking ? (float)(-Math.Abs(wave) * 10) : (float)(Math.Sin(time * 2.3) * (sleepy ? 2 : 4));
        float kick = walking ? (float)(wave * 28) : held ? (float)(wave * 9 + swing * .35) : 0;
        float arm = held ? (float)(swing * .65 + wave * 8) : walking ? (float)(wave * 16) : (float)(wave * 2);
        return new(bob, happy ? 26 : arm, happy ? -30 : -arm, kick, -kick,
            held ? (float)Math.Clamp(-swing * .18, -14, 14) : sleepy ? -7 : walking ? (float)(wave * 3) : 0);
    }

    internal static DomovoiPose Blend(DomovoiPose a, DomovoiPose b, double dt)
    {
        float t = (float)(1 - Math.Exp(-15 * Math.Max(0, dt)));
        float L(float x, float y) => x + (y - x) * t;
        return new(L(a.Bob,b.Bob), L(a.LeftArm,b.LeftArm), L(a.RightArm,b.RightArm),
            L(a.LeftLeg,b.LeftLeg), L(a.RightLeg,b.RightLeg), L(a.Head,b.Head), L(a.Hat,b.Hat), L(a.Beard,b.Beard));
    }

    internal static DomovoiPose ActionPose(double time, double swing, DomovoiMood mood, double actionTime)
    {
        var pose = Pose(time, swing, mood);
        float w = (float)Math.Sin(actionTime * 7);
        // Each event has a separate choreography; use elapsed action time rather
        // than the global clock so every test starts with the same entrance.
        float slow = (float)Math.Sin(actionTime * 3.0);
        float pulse = (float)Math.Abs(Math.Sin(actionTime * 4.2));
        float beat = (float)Math.Sin(actionTime * 5.0);
        float enter = (float)Math.Clamp(actionTime / .7, 0, 1);
        return mood switch
        {
            DomovoiMood.Wave => pose with { RightArm = -122 + w * 13, Head = -5, LeftArm = 6 },
            DomovoiMood.Dance => pose with { Bob = -12 * Math.Abs(w), LeftArm = 35 + w * 30,
                RightArm = -35 + w * 30, LeftLeg = w * 26, RightLeg = -w * 26, Head = w * 7 },
            DomovoiMood.Stretch => pose with { LeftArm = 110, RightArm = -110, Head = -7, Bob = -6 },
            DomovoiMood.Petting => pose with { Head = w * 3, Bob = 2, LeftArm = 12, RightArm = -12 },
            DomovoiMood.Annoyed => pose with { LeftArm = 18, RightArm = -18, Head = -5 },
            DomovoiMood.Sitting => pose with { Bob = 15, LeftLeg = 65, RightLeg = -65, LeftArm = -10, RightArm = 10, Head = 3 },
            DomovoiMood.Perched => pose with { Bob = 12, LeftLeg = 28 + slow * 9, RightLeg = -28 + slow * 9, LeftArm = -4, RightArm = 4, Head = -4 + slow * 2 },
            DomovoiMood.Tea => pose with { Bob = 12, LeftLeg = 50, RightLeg = -50, LeftArm = -9, RightArm = -42 - 25 * (float)(.5 + .5 * Math.Sin(actionTime * 2.1)), Head = 6 },
            DomovoiMood.Coding => pose with { Bob = 14, LeftLeg = 52, RightLeg = -52, LeftArm = -23 + w * 4, RightArm = 23 + w * 4, Head = 7 + w * 1.3f },
            // Social boards keep the shared hands/bounds hit-testing but have distinct motion.
            DomovoiMood.GitHubSign => pose with { LeftArm = 32 + slow * 2, RightArm = -32 - slow * 2, Head = slow * 5, Bob = 1 },
            DomovoiMood.TikTokSign => pose with { LeftArm = 33 + beat * 5, RightArm = -33 + beat * 5, Head = beat * 7, Bob = -7 * pulse, LeftLeg = 9 * beat, RightLeg = -9 * beat },
            DomovoiMood.TelegramSign => pose with { LeftArm = 48 - 14 * enter + slow * 2, RightArm = -48 + 14 * enter + slow * 2, Head = -9 + enter * 5, Bob = -3 },
            // 20%: quiet reminder. 10%: lean forward and insist. 5%: crouch and shake the SOS board.
            DomovoiMood.BatterySign => pose with { LeftArm = 26 + slow * 2, RightArm = -26 - slow * 2, Head = 7 + slow * 4, Bob = 4 },
            DomovoiMood.BatteryCriticalSign => pose with { LeftArm = 41 + beat * 4, RightArm = -41 - beat * 4, Head = -9 + pulse * 4, Bob = -5 * pulse },
            DomovoiMood.BatteryEmptySign => pose with { LeftArm = 30 + w * 5, RightArm = -30 + w * 5, LeftLeg = 28, RightLeg = -28, Head = w * 6, Bob = 12 + pulse * 3, Hat = -w * 5 },
            DomovoiMood.ChargeRising => pose with { LeftArm = 11, RightArm = -65 - 30 * pulse, Head = -7 - 4 * pulse, Bob = -4 * pulse },
            DomovoiMood.ChargeFull => pose with { LeftArm = 105 + beat * 10, RightArm = -105 - beat * 10, LeftLeg = 8 * beat, RightLeg = -8 * beat, Head = -5 + slow * 5, Bob = -9 * pulse },
            DomovoiMood.DeviceFound => pose with { LeftArm = -7, RightArm = -22 - 12 * pulse, Head = 9 - 18 * pulse, Bob = 4 },
            DomovoiMood.SignalLost => pose with { LeftArm = 65 + slow * 6, RightArm = -12, Head = -15 * slow, Bob = 3 },
            DomovoiMood.CompatibilityInspect => pose with { LeftArm = 5, RightArm = -57 + slow * 9, Head = 12 + slow * 4, Bob = 6 },
            DomovoiMood.UpdateDelivery => pose with { LeftArm = 25 + slow * 3, RightArm = -28, Head = -6, Bob = 2 - 5 * pulse },
            DomovoiMood.WidgetTune => pose with { LeftArm = 23, RightArm = -36 + beat * 9, Head = 7 + beat * 2, Bob = 5 },
            DomovoiMood.Guide => pose with { LeftArm = 12, RightArm = -88 + slow * 5, Head = -8, Bob = 0 },
            _ => pose
        };
    }

    private static (float X, float Y, float Angle) JointFor(DomovoiPart part, DomovoiPose pose) => part switch
    {
        DomovoiPart.Head => (180, 255, pose.Head),
        DomovoiPart.LeftHand => (92, 283, pose.LeftArm),
        DomovoiPart.RightHand => (268, 283, pose.RightArm),
        DomovoiPart.LeftFoot => (145, 388, pose.LeftLeg),
        DomovoiPart.RightFoot => (215, 388, pose.RightLeg),
        _ => (0, 0, 0)
    };

    internal static PointF Rotate(PointF p, double angle)
    {
        double r = angle * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return new((float)(p.X * c - p.Y * s), (float)(p.X * s + p.Y * c));
    }

    private static PointF JointPoint(PointF p, DomovoiPart part, DomovoiPose pose, bool inverse)
    {
        var joint = JointFor(part, pose);
        var q = Rotate(new PointF(p.X - joint.X, p.Y - joint.Y), inverse ? -joint.Angle : joint.Angle);
        return new(q.X + joint.X, q.Y + joint.Y);
    }

    private static PointF SecondaryPoint(PointF p, DomovoiPart part, DomovoiPose pose, bool inverse)
    {
        float y = part == DomovoiPart.Hat ? 145 : 176;
        double angle = part == DomovoiPart.Hat ? pose.Hat : pose.Beard;
        var q = Rotate(new PointF(p.X - 180, p.Y - y), inverse ? -angle : angle);
        return new(q.X + 180, q.Y + y);
    }

    internal static PointF ModelPoint(DomovoiGrip grip, DomovoiPose pose)
    {
        if (grip.Part is DomovoiPart.Hat or DomovoiPart.Beard)
            return JointPoint(SecondaryPoint(grip.Point, grip.Part, pose, false), DomovoiPart.Head, pose, false);
        return JointPoint(grip.Point, grip.Part, pose, false);
    }

    // Convert an actual painted pixel to the matching local bone coordinates.
    // Both hit testing and rendering use the same transform and draw order.
    internal static DomovoiGrip Grab(PointF pixel, int width, int height, double angle, int facing, DomovoiPose pose)
    {
        float scale = ScaleFor(width, height);
        var q = Rotate(new PointF(pixel.X - width / 2f, pixel.Y - height / 2f), -angle);
        var model = new PointF(q.X / (scale * facing) + 180, q.Y / scale + 215 - pose.Bob);
        bool E(PointF p, float x, float y, float w, float h)
        {
            float dx = (p.X - x - w / 2) / (w / 2), dy = (p.Y - y - h / 2) / (h / 2);
            return dx * dx + dy * dy <= 1.12f;
        }
        bool V(string d, PointF p) => Cached(d).IsVisible(p);
        var h = JointPoint(model, DomovoiPart.Head, pose, true);
        var hat = SecondaryPoint(h, DomovoiPart.Hat, pose, true);
        if (V(Hat,hat) || V(Brim,hat)) return new(DomovoiPart.Hat,hat);
        if (V(Face,h)) return new(DomovoiPart.Head,h);
        var beard = SecondaryPoint(h, DomovoiPart.Beard, pose, true);
        if (V(Hair,beard)) return new(DomovoiPart.Beard,beard);
        var right = JointPoint(model, DomovoiPart.RightHand, pose, true);
        if (V(RightArm,right) || E(right,277,359,60,40)) return new(DomovoiPart.RightHand,right);
        var left = JointPoint(model, DomovoiPart.LeftHand, pose, true);
        if (V(LeftArm,left) || E(left,23,359,60,40)) return new(DomovoiPart.LeftHand,left);
        if (V(Coat,model)) return new(DomovoiPart.Body,model);
        var rf = JointPoint(model, DomovoiPart.RightFoot, pose, true);
        if (E(rf,194,405,39,19) || V("M 200 381 L 226 381 225 408 199 408 Z",rf)) return new(DomovoiPart.RightFoot,rf);
        var lf = JointPoint(model, DomovoiPart.LeftFoot, pose, true);
        if (E(lf,127,405,39,19) || V("M 134 381 L 160 381 161 408 135 408 Z",lf)) return new(DomovoiPart.LeftFoot,lf);
        return new(DomovoiPart.Body,model);
    }

    internal static PointF Offset(DomovoiGrip grip, DomovoiPose pose, int width, int height, double angle, int facing)
    {
        var p = ModelPoint(grip,pose);
        float scale = ScaleFor(width,height);
        return Rotate(new PointF((p.X - 180) * scale * facing, (p.Y - 215 + pose.Bob) * scale), angle);
    }

    internal static double HangingAngle(DomovoiGrip grip, DomovoiPose pose, int facing, double ax, double ay)
    {
        var p = ModelPoint(grip,pose);
        // Approximate center of mass in torso coordinates. The vector from
        // the grabbed point to the center aligns with effective gravity.
        double dx = (180 - p.X) * facing, dy = 240 - p.Y;
        if (dx * dx + dy * dy < 100) return 0;
        return (Math.Atan2(ay, ax) - Math.Atan2(dy, dx)) * 180 / Math.PI;
    }

    internal static void Paint(Graphics g, int width, int height, double time, double angle, DomovoiPose pose, DomovoiMood mood, int facing, float lookX = 0, float lookY = 0, double signAmount = 0, string batteryText = "", double actionTime = 0)
    {
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = ScaleFor(width,height);
            bool held = mood == DomovoiMood.Held, sleepy = mood == DomovoiMood.Sleepy, happy = mood is DomovoiMood.Happy or DomovoiMood.Petting or DomovoiMood.Dance or DomovoiMood.Wave || (DomovoiSign.IsSign(mood) && !DomovoiSign.IsBatterySign(mood)) || mood is DomovoiMood.ChargeFull or DomovoiMood.ChargeRising;
            g.TranslateTransform(width / 2f, height / 2f);
            g.RotateTransform((float)angle);
            g.ScaleTransform(scale * facing, scale);
            g.TranslateTransform(-180, -215 + pose.Bob);
            void Draw(string d, int fill = -1, int stroke = 0x12121b, float sw = 5)
            {
                var path = Cached(d);
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
            // Legs are separate articulated pieces, not just sliding shoes.
            Joint(145, 388, pose.LeftLeg, () => {
                Draw("M 134 381 L 160 381 161 408 135 408 Z", 0x403c50, sw: 3);
                Ellipse(127, 405, 39, 19, 0x373043, sw: 4);
            });
            Joint(215, 388, pose.RightLeg, () => {
                Draw("M 200 381 L 226 381 225 408 199 408 Z", 0x403c50, sw: 3);
                Ellipse(194, 405, 39, 19, 0x373043, sw: 4);
            });
            Draw(Coat, 0x555368);
            Draw("M 87 278 Q 106 304 102 379 L 71 398 60 398 Z", 0x484559, sw: 0);
            Draw("M 272 275 Q 246 300 254 367 L 285 396 303 396 Z", 0x666379, sw: 0);
            Draw("M 110 298 L 121 348 M 248 298 L 240 348", -1, 0x777287, 3);
            Draw("M 145 253 L 180 298 215 253 M 180 298 L 180 383", -1, 0x918ca8, 4);
            Joint(92, 283, pose.LeftArm, () => {
                Draw(LeftArm, 0x66647c);
                Ellipse(23, 359, 60, 40, 0xc3bac8, sw: 5);
            });
            Joint(268, 283, pose.RightArm, () => {
                Draw(RightArm, 0x66647c);
                Ellipse(277, 359, 60, 40, 0xc3bac8, sw: 5);
                if (mood == DomovoiMood.Tea)
                {
                    Draw("M 281 333 L 326 333 322 368 Q 303 380 285 368 Z", 0xa98bc8, sw: 4);
                    Draw("M 326 340 Q 347 333 345 352 Q 343 364 324 359", -1, 0xd8bce9, 5);
                    Ellipse(281, 326, 45, 14, 0x674a42, 0xdbc1e9, 3);
                }
            });
            if (mood == DomovoiMood.Coding)
            {
                Draw("M 103 320 L 257 320 246 382 114 382 Z", 0x242033, sw: 5);
                Draw("M 111 328 L 249 328 240 374 121 374 Z", 0x574877, sw: 0);
                Draw("M 114 382 L 246 382 271 393 90 393 Z", 0x77718c, sw: 3);
                Draw("M 159 340 L 146 351 159 362 M 201 340 L 214 351 201 362 M 188 337 L 173 365", -1, 0xb8f0e4, 4);
            }

            DomovoiScenes.PaintProps(g, pose, mood, actionTime);
            // A separate head joint follows the body with a little lag.
            var head = g.Save();
            g.TranslateTransform(180, 255);
            g.RotateTransform(pose.Head);
            g.TranslateTransform(-180, -255);
            Joint(180, 176, pose.Beard, () => {
                Draw(Hair, 0xa6a9bd);
                Draw("M 91 222 Q 120 264 158 257 M 220 260 Q 255 247 270 218", -1, 0xc9c9d7, 4);
            });
            Draw(Face, 0x23232e);
            var hatState = g.Save();
            g.TranslateTransform(180, 145);
            g.RotateTransform(pose.Hat);
            g.TranslateTransform(-180, -145);
            Draw(Hat, 0x464456);
            Draw("M 83 110 Q 100 49 174 29 Q 202 24 219 35 Q 165 49 137 106 Z", 0x5b596e, sw: 0);
            Draw("M 228 38 L 258 30 273 59 242 67 Z", 0x716d82, sw: 3);
            Draw("M 238 43 L 245 64 M 251 38 L 258 60", -1, 0xb6b1c1, 2);
            Draw(Brim, 0x646278);
            Draw("M 73 148 Q 121 123 173 126 Q 241 122 287 144", -1, 0x858099, 3);
            g.Restore(hatState);
            Draw("M 85 165 Q 112 156 141 162 Q 126 183 92 191 Z M 219 162 Q 254 153 278 165 L 273 192 Q 241 180 219 162 Z", 0xc3c4d4, sw: 0);
            Draw("M 179 205 Q 151 181 119 201 Q 104 215 91 211 Q 119 243 165 220 Q 175 216 179 205 Q 193 225 215 228 Q 245 232 266 211 Q 251 217 238 204 Q 209 182 179 205 Z", 0xd4d4df, sw: 4);
            Joint(180, 176, pose.Beard, () => Draw("M 113 233 Q 128 252 144 250 M 213 251 Q 230 249 242 237 M 169 245 Q 176 259 185 263", -1, 0x77788d, 3));
            var eyeState = g.Save();
            g.TranslateTransform(lookX, lookY);

            bool blink = time % 4.4 > 4.22;
            if (sleepy || blink)
            {
                Draw("M 122 183 L 151 183 M 209 183 L 238 183", -1, 0xf4eaff, 6);
            }
            else if (mood == DomovoiMood.Dizzy)
            {
                Draw("M 124 169 L 150 194 M 150 169 L 124 194 M 210 169 L 236 194 M 236 169 L 210 194", -1, 0xf4eaff, 6);
            }
            else if (held || mood == DomovoiMood.Worried || DomovoiSign.IsBatterySign(mood))
            {
                Ellipse(130, 166, 15, 25, 0xf3eaff, sw: 0);
                Ellipse(216, 166, 15, 25, 0xf3eaff, sw: 0);
                Draw("M 119 161 L 151 156 M 209 156 L 241 161", -1,
                    mood == DomovoiMood.Worried || DomovoiSign.IsBatterySign(mood) ? 0xf2b7b7 : 0xf4eaff, 5);
            }
            else if (mood == DomovoiMood.DeviceFound)
            {
                Ellipse(127, 167, 19, 24, 0xf4eaff, sw: 0);
                Ellipse(213, 167, 19, 24, 0xf4eaff, sw: 0);
            }
            else if (mood == DomovoiMood.SignalLost)
            {
                Draw("M 119 164 L 151 170 M 209 170 L 241 164", -1, 0xf1c0ce, 6);
                Ellipse(131, 175, 11, 17, 0xf4eaff, sw: 0);
                Ellipse(217, 175, 11, 17, 0xf4eaff, sw: 0);
            }
            else if (mood == DomovoiMood.CompatibilityInspect || mood == DomovoiMood.WidgetTune)
            {
                Draw("M 122 180 L 151 177 M 209 177 L 238 180", -1, 0xf4eaff, 6);
            }
            else if (mood == DomovoiMood.Annoyed)
            {
                Draw("M 120 176 L 151 185 M 209 185 L 240 176", -1, 0xf4eaff, 7);
            }
            else if (happy)
            {
                Draw("M 118 187 Q 137 160 156 187 M 204 187 Q 224 160 243 187", -1, 0xfff4ff, 8);
            }
            else if (Math.Abs(lookX) + Math.Abs(lookY) > 1.0f)
            {
                Ellipse(128, 172, 17, 17, 0xf4eaff, sw: 0);
                Ellipse(215, 172, 17, 17, 0xf4eaff, sw: 0);
            }
            else
                Draw("M 120 185 Q 137 167 153 185 M 207 185 Q 224 167 240 185", -1, 0xf4eaff, 6);
            g.Restore(eyeState);
            g.Restore(head);
            if (DomovoiSign.IsSign(mood) && signAmount > .02)
                DomovoiSign.Paint(g, pose, mood, facing, signAmount, batteryText);
        }
        finally { g.Restore(state); }
    }
}
