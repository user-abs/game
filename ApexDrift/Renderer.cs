using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Numerics;

namespace ApexDrift;

public static class Renderer
{
    // ------------------------------------------------------------- resources

    private static readonly Font TitleFont = new("Segoe UI Black", 72f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font BigFont = new("Segoe UI Black", 56f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font H1Font = new("Segoe UI Black", 34f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font H2Font = new("Segoe UI Semibold", 22f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font BodyFont = new("Segoe UI", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font SmallFont = new("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font MonoFont = new("Consolas", 16f, FontStyle.Bold, GraphicsUnit.Pixel);

    private static readonly StringFormat Near = new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
    private static readonly StringFormat Mid = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    private static readonly StringFormat Far = new() { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };

    private static readonly SolidBrush EdgeBrush = new(Color.FromArgb(225, 235, 235, 235));
    private static readonly SolidBrush KerbRedBrush = new(Color.FromArgb(214, 40, 40));
    private static readonly SolidBrush KerbWhiteBrush = new(Color.FromArgb(240, 240, 240));
    private static readonly SolidBrush DashBrush = new(Color.FromArgb(150, 235, 235, 235));
    private static readonly SolidBrush BarrierRedBrush = new(Color.FromArgb(200, 40, 40));
    private static readonly SolidBrush BarrierWhiteBrush = new(Color.FromArgb(235, 235, 235));
    private static readonly SolidBrush StartBlackBrush = new(Color.FromArgb(20, 20, 20));
    private static readonly SolidBrush StartWhiteBrush = new(Color.FromArgb(245, 245, 245));
    private static readonly SolidBrush ShadowBrush = new(Color.FromArgb(70, 0, 0, 0));
    private static readonly SolidBrush GlassBrush = new(Color.FromArgb(30, 44, 64));
    private static readonly SolidBrush DarkBrush = new(Color.FromArgb(26, 26, 30));
    private static readonly SolidBrush WheelBrush = new(Color.FromArgb(15, 15, 17));
    private static readonly SolidBrush BrakeOnBrush = new(Color.FromArgb(255, 60, 40));
    private static readonly SolidBrush BrakeOffBrush = new(Color.FromArgb(110, 20, 20));
    private static readonly SolidBrush PartBrush = new(Color.White);
    private static readonly SolidBrush PanelBrush = new(Color.FromArgb(140, 8, 10, 16));

    private static readonly Pen[] SkidPens =
    {
        MakeSkidPen(130), MakeSkidPen(95), MakeSkidPen(60), MakeSkidPen(30),
    };

    private static readonly GraphicsPath CarBody = RoundedRect(-21f, -10f, 42f, 20f, 7f);
    private static readonly GraphicsPath Cockpit = RoundedRect(-4f, -6.5f, 14f, 13f, 4f);
    private static readonly GraphicsPath ShadowPath = RoundedRect(-27f, -13f, 54f, 26f, 8f);

    private static PointF[] mapBuf = Array.Empty<PointF>();

    private static Pen MakeSkidPen(int alpha) => new(Color.FromArgb(alpha, 12, 12, 12), 4.5f)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
    };

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2f;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ----------------------------------------------------------------- entry

    public static void Render(Graphics g, int w, int h, Game game)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        if (game.Screen == AppScreen.Menu || game.Race == null)
            DrawMenu(g, w, h, game);
        else
            DrawRace(g, w, h, game);
    }

    // ------------------------------------------------------------- text util

    private static void Text(Graphics g, string s, Font f, Color c, float x, float y, StringFormat fmt, bool shadow = true)
    {
        if (shadow)
        {
            using var sh = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.DrawString(s, f, sh, x + 2f, y + 2f, fmt);
        }
        using var br = new SolidBrush(c);
        g.DrawString(s, f, br, x, y, fmt);
    }

    private static string FormatTime(float t)
    {
        if (t < 0f || t >= float.MaxValue / 2f) return "--:--.---";
        int ms = (int)(t * 1000f);
        return $"{ms / 60000}:{ms / 1000 % 60:00}.{ms % 1000:000}";
    }

    private static Color LerpColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    private static void Panel(Graphics g, float x, float y, float w, float h, float r = 10f)
    {
        using var path = RoundedRect(x, y, w, h, r);
        g.FillPath(PanelBrush, path);
    }

    // ------------------------------------------------------------------ menu

    private static void DrawMenu(Graphics g, int w, int h, Game game)
    {
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(14, 18, 30), Color.FromArgb(46, 24, 56), 90f))
            g.FillRectangle(bg, 0, 0, w, h);

        Text(g, "APEX DRIFT", TitleFont, Color.White, w / 2f, h * 0.11f, Mid);
        Text(g, "Top-down arcade racing", H2Font, Color.FromArgb(190, 200, 220), w / 2f, h * 0.11f + 54f, Mid);

        var track = game.CurrentTrack;
        var def = TrackLibrary.All[game.TrackIndex];

        float pw = Math.Min(620f, w - 80f);
        float ph = h * 0.40f;
        float px = (w - pw) / 2f;
        float py = h * 0.22f;
        Panel(g, px, py, pw, ph, 16f);
        DrawMapLine(g, track, new RectangleF(px, py, pw, ph), 40f, 9f, 4f, Color.FromArgb(20, 22, 30), Color.FromArgb(235, 235, 240));
        Text(g, "◀", H1Font, Color.FromArgb(200, 255, 255, 255), px - 30f, py + ph / 2f, Mid);
        Text(g, "▶", H1Font, Color.FromArgb(200, 255, 255, 255), px + pw + 30f, py + ph / 2f, Mid);

        float iy = py + ph + 22f;
        Text(g, def.Name.ToUpperInvariant(), H1Font, Color.White, w / 2f, iy + 16f, Mid);
        Text(g, def.Description, BodyFont, Color.FromArgb(190, 200, 220), w / 2f, iy + 52f, Mid);

        float lengthKm = track.Length * Units.MetresPerPx / 1000f;
        string info = $"{lengthKm:0.0} km      {game.Laps} lap{(game.Laps == 1 ? "" : "s")}      AI: {game.Difficulty}";
        Text(g, info, H2Font, Color.FromArgb(255, 210, 90), w / 2f, iy + 86f, Mid);

        Text(g, "←  →  track      ↑  ↓  laps      TAB  difficulty      ENTER  start      ESC  quit",
            BodyFont, Color.FromArgb(210, 215, 230), w / 2f, h - 80f, Mid);
        Text(g, "In race:  W/S throttle & brake    A/D steer    SPACE handbrake (drift)    SHIFT nitro    R respawn    C camera    P pause    F11 fullscreen",
            SmallFont, Color.FromArgb(150, 160, 180), w / 2f, h - 48f, Mid);
    }

    // ------------------------------------------------------------- map helper

    /// <summary>Draws the track outline fitted into a box. Returns the transform so callers can place dots.</summary>
    private static (float scale, float ox, float oy) DrawMapLine(
        Graphics g, Track track, RectangleF box, float pad, float outerW, float innerW, Color outer, Color inner)
    {
        var (scale, ox, oy) = FitMap(track, box, pad);
        if (mapBuf.Length != track.N) mapBuf = new PointF[track.N];
        for (int i = 0; i < track.N; i++)
            mapBuf[i] = new PointF(track.P[i].X * scale + ox, track.P[i].Y * scale + oy);

        using var po = new Pen(outer, outerW) { LineJoin = LineJoin.Round };
        using var pi = new Pen(inner, innerW) { LineJoin = LineJoin.Round };
        g.DrawPolygon(po, mapBuf);
        g.DrawPolygon(pi, mapBuf);
        return (scale, ox, oy);
    }

    private static (float scale, float ox, float oy) FitMap(Track track, RectangleF box, float pad)
    {
        float tw = Math.Max(1f, track.Max.X - track.Min.X);
        float th = Math.Max(1f, track.Max.Y - track.Min.Y);
        float scale = MathF.Min((box.Width - pad * 2f) / tw, (box.Height - pad * 2f) / th);
        float ox = box.X + box.Width / 2f - (track.Min.X + track.Max.X) / 2f * scale;
        float oy = box.Y + box.Height / 2f - (track.Min.Y + track.Max.Y) / 2f * scale;
        return (scale, ox, oy);
    }

    // ------------------------------------------------------------------ race

    private static void DrawRace(Graphics g, int w, int h, Game game)
    {
        var race = game.Race!;
        var track = race.Track;
        g.Clear(track.Grass);

        float sx = 0f, sy = 0f;
        if (race.Shake > 0.001f)
        {
            sx = (Random.Shared.NextSingle() - 0.5f) * race.Shake * 20f;
            sy = (Random.Shared.NextSingle() - 0.5f) * race.Shake * 20f;
        }

        var worldState = g.Save();
        g.TranslateTransform(w * 0.5f + sx, h * 0.62f + sy);
        g.RotateTransform(game.CamAngle * 180f / MathF.PI);
        g.ScaleTransform(game.CamZoom, game.CamZoom);
        g.TranslateTransform(-game.CamPos.X, -game.CamPos.Y);

        Vector2 cam = game.CamPos;
        float viewR = MathF.Sqrt((float)w * w + (float)h * h) / game.CamZoom * 0.75f + 200f;
        float viewR2 = viewR * viewR;

        // Grass texture
        using (var blot = new SolidBrush(Color.Black))
        {
            foreach (var b in track.Blotches)
            {
                float r = viewR + b.Radius;
                if (Vector2.DistanceSquared(b.Pos, cam) > r * r) continue;
                blot.Color = b.Color;
                g.FillEllipse(blot, b.Pos.X - b.Radius, b.Pos.Y - b.Radius, b.Radius * 2f, b.Radius * 2f);
            }
        }

        // Asphalt
        using (var asphalt = new SolidBrush(track.Asphalt))
            g.FillPath(asphalt, track.AsphaltPath);

        // Road markings, kerbs
        foreach (var ch in track.Chunks)
        {
            float r = viewR + ch.Radius;
            if (Vector2.DistanceSquared(ch.Mid, cam) > r * r) continue;
            g.FillPath(DashBrush, ch.Dashes);
            g.FillPath(EdgeBrush, ch.Edge);
            g.FillPath(KerbRedBrush, ch.KerbRed);
            g.FillPath(KerbWhiteBrush, ch.KerbWhite);
        }
        g.FillPath(StartWhiteBrush, track.StartWhite);
        g.FillPath(StartBlackBrush, track.StartBlack);

        // Tyre marks
        var skids = race.Skids.Items;
        for (int i = 0; i < skids.Length; i++)
        {
            var s = skids[i];
            if (!s.Active) continue;
            float age = race.Time - s.Time;
            if (age > 22f) continue;
            if (Vector2.DistanceSquared(s.A, cam) > viewR2) continue;
            var pen = age < 6f ? SkidPens[0] : age < 12f ? SkidPens[1] : age < 18f ? SkidPens[2] : SkidPens[3];
            g.DrawLine(pen, s.A.X, s.A.Y, s.B.X, s.B.Y);
        }

        // Barriers
        foreach (var ch in track.Chunks)
        {
            float r = viewR + ch.Radius;
            if (Vector2.DistanceSquared(ch.Mid, cam) > r * r) continue;
            g.FillPath(BarrierRedBrush, ch.BarrierRed);
            g.FillPath(BarrierWhiteBrush, ch.BarrierWhite);
        }

        // Cars: shadows first, then bodies (player on top)
        foreach (var c in race.Cars) DrawShadow(g, c);
        foreach (var c in race.Cars) if (!c.IsPlayer) DrawCar(g, c);
        DrawCar(g, race.Player);

        // Particles
        var parts = race.Fx.Items;
        for (int i = 0; i < parts.Length; i++)
        {
            ref Particle p = ref parts[i];
            if (!p.Active) continue;
            if (Vector2.DistanceSquared(p.Pos, cam) > viewR2) continue;
            float k = p.Life / p.MaxLife;
            float size = MathUtil.Lerp(p.Size1, p.Size0, k);
            int a = (int)(p.Color.A * k);
            PartBrush.Color = Color.FromArgb(a, p.Color.R, p.Color.G, p.Color.B);
            g.FillEllipse(PartBrush, p.Pos.X - size / 2f, p.Pos.Y - size / 2f, size, size);
        }

        // Trees (drawn last so they overhang the world)
        using (var tb = new SolidBrush(track.TreeColor))
        using (var hl = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
        {
            foreach (var t in track.Trees)
            {
                float r = viewR + t.Radius;
                if (Vector2.DistanceSquared(t.Pos, cam) > r * r) continue;
                g.FillEllipse(ShadowBrush, t.Pos.X - t.Radius + 9f, t.Pos.Y - t.Radius + 11f, t.Radius * 2f, t.Radius * 2f);
                tb.Color = Color.FromArgb(
                    Math.Clamp((int)(track.TreeColor.R * t.Shade), 0, 255),
                    Math.Clamp((int)(track.TreeColor.G * t.Shade), 0, 255),
                    Math.Clamp((int)(track.TreeColor.B * t.Shade), 0, 255));
                g.FillEllipse(tb, t.Pos.X - t.Radius, t.Pos.Y - t.Radius, t.Radius * 2f, t.Radius * 2f);
                g.FillEllipse(hl, t.Pos.X - t.Radius * 0.7f - 5f, t.Pos.Y - t.Radius * 0.7f - 5f, t.Radius * 1.1f, t.Radius * 1.1f);
            }
        }

        g.Restore(worldState);

        DrawHud(g, w, h, game);
    }

    private static void DrawShadow(Graphics g, Car c)
    {
        var st = g.Save();
        g.TranslateTransform(c.Pos.X + 4f, c.Pos.Y + 6f);
        g.RotateTransform(c.Heading * 180f / MathF.PI);
        g.FillPath(ShadowBrush, ShadowPath);
        g.Restore(st);
    }

    private static void DrawWheel(Graphics g, float x, float y, float angleDeg)
    {
        var st = g.Save();
        g.TranslateTransform(x, y);
        g.RotateTransform(angleDeg);
        g.FillRectangle(WheelBrush, -6f, -3.5f, 12f, 7f);
        g.Restore(st);
    }

    private static void DrawCar(Graphics g, Car c)
    {
        var st = g.Save();
        g.TranslateTransform(c.Pos.X, c.Pos.Y);
        g.RotateTransform(c.Heading * 180f / MathF.PI);

        // Wheels (front pair turns with the steering)
        float steerDeg = c.Steer * 28f;
        DrawWheel(g, -14f, -11.5f, 0f);
        DrawWheel(g, -14f, 11.5f, 0f);
        DrawWheel(g, 14f, -11.5f, steerDeg);
        DrawWheel(g, 14f, 11.5f, steerDeg);

        // Wings
        g.FillRectangle(DarkBrush, -27f, -12f, 6f, 24f);
        g.FillRectangle(DarkBrush, 19f, -12.5f, 7f, 25f);

        // Body, stripe, cockpit
        using (var body = new SolidBrush(c.Body)) g.FillPath(body, CarBody);
        using (var stripe = new SolidBrush(c.Accent)) g.FillRectangle(stripe, -20f, -2.2f, 40f, 4.4f);
        g.FillPath(GlassBrush, Cockpit);

        // Brake lights
        var lamp = c.Braking ? BrakeOnBrush : BrakeOffBrush;
        g.FillRectangle(lamp, -22f, -9f, 3f, 5f);
        g.FillRectangle(lamp, -22f, 4f, 3f, 5f);

        g.Restore(st);
    }

    // ------------------------------------------------------------------- HUD

    private static void DrawHud(Graphics g, int w, int h, Game game)
    {
        var race = game.Race!;
        var p = race.Player;

        // Position + lap + times
        int place = race.PlaceOf(p);
        Text(g, $"P{place}", BigFont, Color.White, 24f, 8f, Near);
        Text(g, $"/ {race.Cars.Count}", H2Font, Color.FromArgb(200, 210, 230), 118f + (place >= 10 ? 28f : 0f), 36f, Near);
        int lapShown = Math.Min(p.LapsDone + 1, race.TotalLaps);
        Text(g, $"LAP {lapShown}/{race.TotalLaps}", H2Font, Color.White, 24f, 76f, Near);

        float lapTime = race.State == RaceState.Countdown ? 0f : race.RaceTime - p.LapStart;
        Text(g, $"TIME  {FormatTime(lapTime)}", MonoFont, Color.White, 24f, 108f, Near);
        Text(g, $"LAST  {FormatTime(p.LastLap > 0f ? p.LastLap : -1f)}", MonoFont, Color.FromArgb(200, 210, 230), 24f, 130f, Near);
        Text(g, $"BEST  {FormatTime(p.BestLap)}", MonoFont, Color.FromArgb(255, 210, 90), 24f, 152f, Near);

        // Standings
        float sy = 190f;
        Panel(g, 16f, sy - 6f, 190f, race.Ranking.Count * 24f + 12f, 8f);
        for (int i = 0; i < race.Ranking.Count; i++)
        {
            var c = race.Ranking[i];
            float y = sy + i * 24f;
            using (var sw = new SolidBrush(c.Body)) g.FillRectangle(sw, 26f, y + 4f, 10f, 14f);
            Color tc = c.IsPlayer ? Color.FromArgb(255, 210, 90) : Color.White;
            Text(g, $"{i + 1}", SmallFont, tc, 44f, y + 2f, Near, false);
            Text(g, c.Name, BodyFont, tc, 66f, y, Near, false);
            if (c.Finished) Text(g, "✔", SmallFont, Color.LimeGreen, 190f, y + 2f, Far, false);
        }

        DrawMinimap(g, w, race);
        DrawSpeedo(g, w, h, p);
        DrawNitro(g, w, h, p);

        // Wrong way
        int ti = p.Index;
        if (race.State == RaceState.Racing && p.Speed > 60f &&
            Vector2.Dot(p.Forward, race.Track.Tan[ti]) < -0.25f &&
            (int)(race.Time * 3f) % 2 == 0)
        {
            Text(g, "WRONG WAY", H1Font, Color.FromArgb(255, 70, 60), w / 2f, 90f, Mid);
        }

        // Lap flash
        if (p.LapFlash > 0f && race.State == RaceState.Racing)
        {
            Text(g, $"LAP TIME  {FormatTime(p.LastLap)}", H2Font, Color.White, w / 2f, 46f, Mid);
            if (p.LastLap <= p.BestLap + 0.0005f && p.LapsDone > 1)
                Text(g, "BEST LAP!", H2Font, Color.FromArgb(255, 210, 90), w / 2f, 74f, Mid);
        }

        // Controls hint at the start
        if (race.State == RaceState.Countdown || race.RaceTime < 8f)
        {
            Text(g, "W/S throttle & brake   A/D steer   SPACE handbrake   SHIFT nitro   R respawn   C camera   P pause",
                SmallFont, Color.FromArgb(230, 235, 245), w / 2f, h - 20f, Mid);
        }

        DrawCountdown(g, w, h, race);

        if (race.State == RaceState.Finished) DrawFinished(g, w, h, race);
        if (game.Paused) DrawPause(g, w, h);
    }

    private static void DrawMinimap(Graphics g, int w, Race race)
    {
        var box = new RectangleF(w - 236f, 16f, 220f, 168f);
        Panel(g, box.X, box.Y, box.Width, box.Height, 10f);
        var (scale, ox, oy) = DrawMapLine(g, race.Track, box, 16f, 7f, 3f, Color.FromArgb(220, 15, 15, 20), Color.FromArgb(220, 210, 215, 225));

        foreach (var c in race.Cars)
        {
            float x = c.Pos.X * scale + ox;
            float y = c.Pos.Y * scale + oy;
            float r = c.IsPlayer ? 5.5f : 4f;
            if (c.IsPlayer)
            {
                using var ring = new SolidBrush(Color.White);
                g.FillEllipse(ring, x - r - 1.5f, y - r - 1.5f, (r + 1.5f) * 2f, (r + 1.5f) * 2f);
            }
            using var dot = new SolidBrush(c.Body);
            g.FillEllipse(dot, x - r, y - r, r * 2f, r * 2f);
        }
    }

    private static void DrawSpeedo(Graphics g, int w, int h, Car p)
    {
        float cx = w - 150f;
        float cy = h - 135f;
        float R = 98f;

        Panel(g, cx - R - 28f, cy - R - 24f, (R + 28f) * 2f, (R + 24f) * 2f - 28f, 20f);

        float kmh = MathF.Max(0f, p.ForwardSpeed) * Units.KmhPerPxSec;
        float frac = Math.Clamp(kmh / 300f, 0f, 1f);

        using (var bg = new Pen(Color.FromArgb(120, 255, 255, 255), 14f))
            g.DrawArc(bg, cx - R, cy - R, R * 2f, R * 2f, 135f, 270f);

        Color col = frac < 0.6f
            ? LerpColor(Color.FromArgb(70, 220, 110), Color.FromArgb(250, 210, 60), frac / 0.6f)
            : LerpColor(Color.FromArgb(250, 210, 60), Color.FromArgb(240, 60, 50), (frac - 0.6f) / 0.4f);
        if (frac > 0.004f)
        {
            using var fp = new Pen(col, 14f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(fp, cx - R, cy - R, R * 2f, R * 2f, 135f, 270f * frac);
        }

        Text(g, ((int)kmh).ToString(), BigFont, Color.White, cx, cy - 6f, Mid);
        Text(g, "KM/H", SmallFont, Color.FromArgb(200, 210, 230), cx, cy + 34f, Mid);

        string gear = p.ForwardSpeed < -5f ? "R" : Math.Clamp(1 + (int)(kmh / 45f), 1, 7).ToString();
        Text(g, $"GEAR  {gear}", H2Font, Color.FromArgb(255, 210, 90), cx, cy + 66f, Mid);
    }

    private static void DrawNitro(Graphics g, int w, int h, Car p)
    {
        float bw = 240f, bh = 12f;
        float x = (w - bw) / 2f, y = h - 52f;
        using var back = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        g.FillRectangle(back, x - 2f, y - 2f, bw + 4f, bh + 4f);
        Color c = p.NitroActive ? Color.FromArgb(120, 200, 255) : Color.FromArgb(50, 140, 240);
        using var fill = new SolidBrush(c);
        g.FillRectangle(fill, x, y, bw * p.Nitro, bh);
        Text(g, "NITRO", SmallFont, Color.FromArgb(200, 220, 255), w / 2f, y - 12f, Mid);
    }

    private static void DrawCountdown(Graphics g, int w, int h, Race race)
    {
        if (race.State == RaceState.Countdown)
        {
            if (race.Countdown > 3f)
            {
                Text(g, "GET READY", H1Font, Color.White, w / 2f, h * 0.3f, Mid);
            }
            else
            {
                int n = (int)MathF.Ceiling(race.Countdown);
                Color c = n == 1 ? Color.FromArgb(80, 230, 110) : n == 2 ? Color.FromArgb(250, 210, 60) : Color.FromArgb(240, 70, 60);
                Text(g, n.ToString(), TitleFont, c, w / 2f, h * 0.3f, Mid);
            }
        }
        else if (race.State == RaceState.Racing && race.RaceTime < 1.2f)
        {
            Text(g, "GO!", TitleFont, Color.FromArgb(80, 230, 110), w / 2f, h * 0.3f, Mid);
        }
    }

    private static void DrawFinished(Graphics g, int w, int h, Race race)
    {
        float pw = 460f, ph = 120f + race.Ranking.Count * 28f + 46f;
        float x = (w - pw) / 2f, y = (h - ph) / 2f - 20f;
        using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            g.FillRectangle(dim, 0, 0, w, h);
        Panel(g, x, y, pw, ph, 16f);

        int place = race.PlaceOf(race.Player);
        string suffix = place == 1 ? "st" : place == 2 ? "nd" : place == 3 ? "rd" : "th";
        Text(g, "RACE FINISHED", H1Font, Color.White, w / 2f, y + 30f, Mid);
        Text(g, $"You finished {place}{suffix}", H2Font, Color.FromArgb(255, 210, 90), w / 2f, y + 70f, Mid);

        float ry = y + 100f;
        for (int i = 0; i < race.Ranking.Count; i++)
        {
            var c = race.Ranking[i];
            float rowY = ry + i * 28f;
            Color tc = c.IsPlayer ? Color.FromArgb(255, 210, 90) : Color.White;
            using (var sw = new SolidBrush(c.Body)) g.FillRectangle(sw, x + 28f, rowY + 5f, 10f, 16f);
            Text(g, $"{i + 1}", BodyFont, tc, x + 50f, rowY, Near, false);
            Text(g, c.Name, BodyFont, tc, x + 80f, rowY, Near, false);
            string t = c.Finished ? FormatTime(c.FinishTime) : "racing…";
            Text(g, t, MonoFont, c.Finished ? tc : Color.FromArgb(150, 160, 180), x + pw - 28f, rowY + 2f, Far, false);
        }

        Text(g, "ENTER  race again        ESC  menu", BodyFont, Color.FromArgb(210, 215, 230), w / 2f, y + ph - 26f, Mid);
    }

    private static void DrawPause(Graphics g, int w, int h)
    {
        using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            g.FillRectangle(dim, 0, 0, w, h);
        Text(g, "PAUSED", TitleFont, Color.White, w / 2f, h / 2f - 20f, Mid);
        Text(g, "P  resume        ESC  menu", H2Font, Color.FromArgb(210, 215, 230), w / 2f, h / 2f + 40f, Mid);
    }
}
