using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Numerics;

namespace DungeonDelve;

public static class Renderer
{
    // ------------------------------------------------------------- resources

    private static readonly Font TitleFont = new("Segoe UI Black", 76f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font H1Font = new("Segoe UI Black", 34f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font H2Font = new("Segoe UI Semibold", 22f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font BodyFont = new("Segoe UI", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font SmallFont = new("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font BoldSmall = new("Segoe UI Semibold", 14f, FontStyle.Regular, GraphicsUnit.Pixel);

    private static readonly StringFormat Near = new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
    private static readonly StringFormat Mid = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    private static readonly StringFormat Far = new() { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };
    private static readonly StringFormat TopCenter = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

    private static readonly SolidBrush Scratch = new(Color.White);
    private static readonly SolidBrush TileBrush = new(Color.Black);
    private static readonly SolidBrush ShadowBrush = new(Color.FromArgb(70, 0, 0, 0));
    private static readonly SolidBrush PartBrush = new(Color.White);
    private static readonly Pen OutlinePen = new(Color.FromArgb(150, 0, 0, 0), 1.5f);

    private static readonly List<Vector2> Torches = new();

    private static Bitmap? miniBmp;
    private static DungeonMap? miniFor;
    private static int miniVersion = -1;

    private static Brush Solid(Color c)
    {
        Scratch.Color = c;
        return Scratch;
    }

    private static Color Shade(Color c, float k) => Color.FromArgb(
        c.A,
        Math.Clamp((int)(c.R * k), 0, 255),
        Math.Clamp((int)(c.G * k), 0, 255),
        Math.Clamp((int)(c.B * k), 0, 255));

    private static void Circle(Graphics g, Color c, float x, float y, float r) =>
        g.FillEllipse(Solid(c), x - r, y - r, r * 2f, r * 2f);

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

    private static void Panel(Graphics g, float x, float y, float w, float h, float r = 10f, int alpha = 150)
    {
        using var path = RoundedRect(x, y, w, h, r);
        g.FillPath(Solid(Color.FromArgb(alpha, 8, 8, 14)), path);
    }

    private static void Text(Graphics g, string s, Font f, Color c, float x, float y, StringFormat fmt, bool shadow = true)
    {
        if (shadow) g.DrawString(s, f, Solid(Color.FromArgb(160, 0, 0, 0)), x + 1.5f, y + 1.5f, fmt);
        g.DrawString(s, f, Solid(c), x, y, fmt);
    }

    private static void Bar(Graphics g, float x, float y, float w, float h, float frac, Color fill)
    {
        frac = Math.Clamp(frac, 0f, 1f);
        g.FillRectangle(Solid(Color.FromArgb(200, 10, 10, 14)), x - 1.5f, y - 1.5f, w + 3f, h + 3f);
        g.FillRectangle(Solid(Shade(fill, 0.35f)), x, y, w, h);
        g.FillRectangle(Solid(fill), x, y, w * frac, h);
        g.FillRectangle(Solid(Color.FromArgb(50, 255, 255, 255)), x, y, w * frac, h * 0.4f);
    }

    // ----------------------------------------------------------------- entry

    public static void Render(Graphics g, int w, int h, World world)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        if (world.Phase == Phase.Title)
        {
            DrawTitle(g, w, h);
            return;
        }

        g.Clear(Color.Black);
        DrawWorld(g, w, h, world);
        DrawHud(g, w, h, world);

        switch (world.Phase)
        {
            case Phase.Paused: DrawPause(g, w, h); break;
            case Phase.LevelUp: DrawLevelUp(g, w, h, world); break;
            case Phase.Dead: DrawDead(g, w, h, world); break;
        }
    }

    // ----------------------------------------------------------------- world

    private static void DrawWorld(Graphics g, int w, int h, World world)
    {
        var map = world.Map;
        var state = g.Save();

        float sx = 0f, sy = 0f;
        if (world.Shake > 0.001f)
        {
            sx = (Random.Shared.NextSingle() - 0.5f) * world.Shake * 14f;
            sy = (Random.Shared.NextSingle() - 0.5f) * world.Shake * 14f;
        }

        g.TranslateTransform(w * 0.5f + sx, h * 0.5f + sy);
        g.ScaleTransform(World.Zoom, World.Zoom);
        g.TranslateTransform(-world.Cam.X, -world.Cam.Y);

        DrawTiles(g, w, h, world);
        DrawPickups(g, world);

        foreach (var m in world.Monsters)
        {
            Point t = DungeonMap.TileOf(m.Pos);
            if (!map.Visible[t.X, t.Y]) continue;
            DrawTelegraph(g, m);
        }
        foreach (var m in world.Monsters)
        {
            Point t = DungeonMap.TileOf(m.Pos);
            if (!map.Visible[t.X, t.Y]) continue;
            DrawMonster(g, m);
        }

        DrawPlayer(g, world);
        DrawProjectiles(g, world);
        DrawParticles(g, world);
        DrawFloatTexts(g, world);

        g.Restore(state);
    }

    private static void DrawTiles(Graphics g, int w, int h, World world)
    {
        var map = world.Map;
        const int ts = DungeonMap.TileSize;
        float halfW = w / (2f * World.Zoom) + ts;
        float halfH = h / (2f * World.Zoom) + ts;
        int x0 = Math.Max(0, (int)((world.Cam.X - halfW) / ts));
        int x1 = Math.Min(DungeonMap.Width - 1, (int)((world.Cam.X + halfW) / ts));
        int y0 = Math.Max(0, (int)((world.Cam.Y - halfH) / ts));
        int y1 = Math.Min(DungeonMap.Height - 1, (int)((world.Cam.Y + halfH) / ts));

        float flicker = 0.94f + 0.06f * MathF.Sin(world.Time * 9f) + 0.03f * MathF.Sin(world.Time * 23f);
        Torches.Clear();

        for (int ty = y0; ty <= y1; ty++)
        {
            for (int tx = x0; tx <= x1; tx++)
            {
                if (!map.Explored[tx, ty]) continue;

                bool vis = map.Visible[tx, ty];
                float b = vis ? map.Light[tx, ty] * flicker : 0.24f;
                int v = map.Variant[tx, ty];
                float px = tx * ts, py = ty * ts;
                Tile tile = map.Tiles[tx, ty];

                if (tile == Tile.Wall)
                {
                    bool faceBelow = ty + 1 < DungeonMap.Height && map.Tiles[tx, ty + 1] != Tile.Wall;
                    TileBrush.Color = Shade(Color.FromArgb(46, 42, 58), b);
                    g.FillRectangle(TileBrush, px, py, ts, ts);

                    if (faceBelow)
                    {
                        TileBrush.Color = Shade(Color.FromArgb(104, 84, 92), b);
                        g.FillRectangle(TileBrush, px, py + ts - 14, ts, 14);
                        TileBrush.Color = Shade(Color.FromArgb(62, 48, 58), b);
                        g.FillRectangle(TileBrush, px, py + ts - 8, ts, 1);
                        g.FillRectangle(TileBrush, px + (v % 2 == 0 ? 10 : 20), py + ts - 14, 1, 6);
                        g.FillRectangle(TileBrush, px + (v % 2 == 0 ? 22 : 8), py + ts - 7, 1, 7);
                        if (v % 29 == 0 && vis) Torches.Add(new Vector2(px + ts / 2f, py + ts - 9f));
                    }
                    else
                    {
                        TileBrush.Color = Shade(Color.FromArgb(58, 54, 72), b);
                        g.FillRectangle(TileBrush, px + 2, py + 2, ts - 4, ts - 4);
                    }
                }
                else
                {
                    int n = v & 7;
                    int checker = ((tx + ty) & 1) == 0 ? 3 : 0;
                    TileBrush.Color = Shade(Color.FromArgb(60 + n + checker, 55 + n + checker, 72 + n + checker), b);
                    g.FillRectangle(TileBrush, px, py, ts, ts);

                    if (v % 41 == 0)
                    {
                        TileBrush.Color = Shade(Color.FromArgb(40, 38, 50), b);
                        g.FillRectangle(TileBrush, px + 6, py + 10, 14, 1);
                        g.FillRectangle(TileBrush, px + 14, py + 10, 1, 9);
                    }
                    else if (v % 67 == 1)
                    {
                        TileBrush.Color = Shade(Color.FromArgb(110, 30, 34), b);
                        g.FillEllipse(TileBrush, px + 6, py + 8, 16, 11);
                    }
                    else if (v % 53 == 2)
                    {
                        TileBrush.Color = Shade(Color.FromArgb(190, 185, 170), b);
                        g.FillEllipse(TileBrush, px + 8, py + 12, 6, 4);
                        g.FillEllipse(TileBrush, px + 15, py + 15, 5, 3);
                    }

                    if (tile == Tile.Stairs) DrawStairs(g, px, py, b, world.StairsOpen, world.Time);
                }
            }
        }

        // Torch glow, drawn after the tiles so neighbours don't paint over it.
        foreach (var tp in Torches)
        {
            float fl = 0.8f + 0.2f * MathF.Sin(world.Time * 12f + tp.X);
            g.FillEllipse(Solid(Color.FromArgb((int)(34 * fl), 255, 170, 60)), tp.X - 46f, tp.Y - 46f, 92f, 92f);
            g.FillEllipse(Solid(Color.FromArgb((int)(40 * fl), 255, 190, 80)), tp.X - 22f, tp.Y - 22f, 44f, 44f);
            g.FillEllipse(Solid(Color.FromArgb(255, 255, 140, 30)), tp.X - 3f, tp.Y - 6f * fl, 6f, 9f * fl);
            g.FillEllipse(Solid(Color.FromArgb(255, 255, 230, 120)), tp.X - 1.5f, tp.Y - 3f, 3f, 5f);
        }
    }

    private static void DrawStairs(Graphics g, float px, float py, float b, bool open, float time)
    {
        for (int i = 0; i < 4; i++)
        {
            int inset = 2 + i * 4;
            Color c = open
                ? Color.FromArgb(215 - i * 30, 180 - i * 25, 70)
                : Color.FromArgb(150 - i * 20, 50, 50);
            TileBrush.Color = Shade(c, b);
            g.FillRectangle(TileBrush, px + inset, py + inset, 32 - inset * 2, 32 - inset * 2);
        }
        if (open)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin(time * 4f);
            g.FillEllipse(Solid(Color.FromArgb((int)(60 * pulse * b), 255, 230, 120)), px - 6, py - 6, 44, 44);
        }
    }

    // --------------------------------------------------------------- pickups

    private static void DrawPickups(Graphics g, World world)
    {
        var map = world.Map;
        foreach (var pk in world.Pickups)
        {
            if (pk.Taken) continue;
            Point t = DungeonMap.TileOf(pk.Pos);
            if (!map.Visible[t.X, t.Y]) continue;

            float x = pk.Pos.X;
            float y = pk.Pos.Y + MathF.Sin(pk.Bob * 4f) * 2f;

            switch (pk.Kind)
            {
                case PickupKind.Gold:
                    Circle(g, Color.FromArgb(120, 240, 200, 50), x, y, 8f);
                    Circle(g, Color.FromArgb(240, 200, 50), x, y, 5f);
                    Circle(g, Color.FromArgb(255, 240, 130), x - 1.2f, y - 1.2f, 2.2f);
                    break;

                case PickupKind.HealthPotion:
                case PickupKind.ManaPotion:
                {
                    Color liquid = pk.Kind == PickupKind.HealthPotion ? Color.FromArgb(220, 50, 60) : Color.FromArgb(60, 110, 235);
                    Circle(g, Color.FromArgb(70, liquid.R, liquid.G, liquid.B), x, y, 12f);
                    g.FillEllipse(Solid(liquid), x - 5f, y - 4f, 10f, 11f);
                    g.FillRectangle(Solid(Color.FromArgb(210, 210, 220)), x - 2f, y - 9f, 4f, 6f);
                    g.FillRectangle(Solid(Color.FromArgb(160, 110, 70)), x - 3f, y - 10f, 6f, 2f);
                    Circle(g, Color.FromArgb(150, 255, 255, 255), x - 2f, y - 1f, 1.6f);
                    break;
                }

                case PickupKind.Loot:
                {
                    Color rc = pk.Item?.Color ?? Color.White;
                    Circle(g, Color.FromArgb(60 + (int)(30 * MathF.Sin(world.Time * 5f)), rc.R, rc.G, rc.B), x, y, 15f);
                    var diamond = new[]
                    {
                        new PointF(x, y - 9f), new PointF(x + 7f, y), new PointF(x, y + 9f), new PointF(x - 7f, y),
                    };
                    g.FillPolygon(Solid(rc), diamond);
                    g.DrawPolygon(OutlinePen, diamond);
                    break;
                }

                case PickupKind.Chest:
                    g.FillEllipse(ShadowBrush, x - 12f, y + 3f, 24f, 9f);
                    g.FillRectangle(Solid(Color.FromArgb(130, 84, 40)), x - 11f, y - 8f, 22f, 16f);
                    g.FillRectangle(Solid(Color.FromArgb(160, 108, 52)), x - 11f, y - 8f, 22f, 6f);
                    g.FillRectangle(Solid(Color.FromArgb(70, 70, 80)), x - 11f, y - 3f, 22f, 2f);
                    g.FillRectangle(Solid(Color.FromArgb(240, 200, 60)), x - 2.5f, y - 4f, 5f, 6f);
                    g.DrawRectangle(OutlinePen, x - 11f, y - 8f, 22f, 16f);
                    break;
            }
        }
    }

    // -------------------------------------------------------------- monsters

    private static void DrawTelegraph(Graphics g, Monster m)
    {
        if (m.State != AiState.Windup) return;
        float pulse = 0.6f + 0.4f * MathF.Sin(m.Anim * 30f);

        switch (m.Type)
        {
            case MonsterType.Archer:
            {
                using var pen = new Pen(Color.FromArgb((int)(120 * pulse), 255, 60, 40), 2f);
                Vector2 end = m.Pos + MathUtil.FromAngle(m.Facing) * 380f;
                g.DrawLine(pen, m.Pos.X, m.Pos.Y, end.X, end.Y);
                break;
            }
            case MonsterType.BoneKing:
            {
                using var pen = new Pen(Color.FromArgb((int)(70 * pulse), 255, 60, 40), m.Radius * 2f);
                Vector2 end = m.Pos + m.ChargeDir * 380f;
                g.DrawLine(pen, m.Pos.X, m.Pos.Y, end.X, end.Y);
                break;
            }
            default:
                Circle(g, Color.FromArgb((int)(80 * pulse), 255, 60, 40), m.Pos.X, m.Pos.Y, m.Radius + 24f);
                break;
        }
    }

    private static void DrawMonster(Graphics g, Monster m)
    {
        bool flash = m.HitFlash > 0f;
        float r = m.Radius;
        var st = g.Save();
        g.TranslateTransform(m.Pos.X, m.Pos.Y);

        g.FillEllipse(ShadowBrush, -r, r * 0.35f, r * 2f, r * 0.9f);

        switch (m.Type)
        {
            case MonsterType.Slime:
            {
                float squash = 1f + 0.1f * MathF.Sin(m.Anim * 6f);
                Color c = flash ? Color.White : Color.FromArgb(90, 205, 95);
                g.FillEllipse(Solid(c), -r * squash, -r / squash * 0.9f, r * 2f * squash, r * 2f / squash * 0.9f);
                g.FillEllipse(Solid(Color.FromArgb(110, 255, 255, 255)), -r * 0.55f, -r * 0.65f, r * 0.7f, r * 0.45f);
                Circle(g, Color.FromArgb(20, 40, 20), -r * 0.35f, -r * 0.05f, 2f);
                Circle(g, Color.FromArgb(20, 40, 20), r * 0.35f, -r * 0.05f, 2f);
                break;
            }

            case MonsterType.Bat:
            {
                float flap = MathF.Sin(m.Anim * 22f);
                Color wing = flash ? Color.White : Color.FromArgb(70, 40, 90);
                var left = new[] { new PointF(-3, 0), new PointF(-21, -6 - 8 * flap), new PointF(-14, 4), new PointF(-6, 5) };
                var right = new[] { new PointF(3, 0), new PointF(21, -6 - 8 * flap), new PointF(14, 4), new PointF(6, 5) };
                g.FillPolygon(Solid(wing), left);
                g.FillPolygon(Solid(wing), right);
                g.FillEllipse(Solid(flash ? Color.White : Color.FromArgb(95, 60, 120)), -6, -6, 12, 12);
                Circle(g, Color.FromArgb(255, 80, 80), -2.5f, -1.5f, 1.4f);
                Circle(g, Color.FromArgb(255, 80, 80), 2.5f, -1.5f, 1.4f);
                break;
            }

            case MonsterType.Skeleton:
            {
                Color bone = flash ? Color.White : Color.FromArgb(220, 218, 200);
                g.FillEllipse(Solid(flash ? Color.White : Color.FromArgb(180, 178, 160)), -r, -r * 0.55f, r * 2f, r * 1.1f);
                g.FillEllipse(Solid(bone), -r * 0.75f, -r * 0.8f, r * 1.5f, r * 1.5f);
                Circle(g, Color.FromArgb(25, 20, 30), -r * 0.3f, -r * 0.2f, 2.4f);
                Circle(g, Color.FromArgb(25, 20, 30), r * 0.3f, -r * 0.2f, 2.4f);
                g.FillRectangle(Solid(Color.FromArgb(60, 55, 60)), -r * 0.3f, r * 0.2f, r * 0.6f, 1.5f);
                var s2 = g.Save();
                g.RotateTransform(m.Facing * 180f / MathF.PI);
                float lift = m.State == AiState.Windup ? -0.7f : 0.2f;
                g.RotateTransform(lift * 57.3f);
                g.FillRectangle(Solid(Color.FromArgb(200, 200, 205)), r * 0.6f, -1.5f, 17f, 3f);
                g.FillRectangle(Solid(Color.FromArgb(120, 90, 60)), r * 0.45f, -3f, 3f, 6f);
                g.Restore(s2);
                break;
            }

            case MonsterType.Archer:
            {
                Color skin = flash ? Color.White : Color.FromArgb(70, 160, 80);
                g.FillEllipse(Solid(flash ? Color.White : Color.FromArgb(55, 80, 60)), -r, -r, r * 2f, r * 2f);
                g.FillEllipse(Solid(skin), -r * 0.7f, -r * 0.7f, r * 1.4f, r * 1.4f);
                Circle(g, Color.FromArgb(255, 220, 80), -r * 0.28f, -r * 0.1f, 1.8f);
                Circle(g, Color.FromArgb(255, 220, 80), r * 0.28f, -r * 0.1f, 1.8f);
                var s2 = g.Save();
                g.RotateTransform(m.Facing * 180f / MathF.PI);
                using (var bow = new Pen(Color.FromArgb(150, 100, 50), 2f))
                {
                    g.DrawArc(bow, 5f, -11f, 14f, 22f, -80f, 160f);
                    g.DrawLine(OutlinePen, 12f, -10.8f, 12f, 10.8f);
                    if (m.State == AiState.Windup)
                        g.DrawLine(bow, 2f, 0f, 24f, 0f);
                }
                g.Restore(s2);
                break;
            }

            case MonsterType.Ogre:
            {
                Color skin = flash ? Color.White : Color.FromArgb(150, 122, 78);
                g.FillEllipse(Solid(skin), -r, -r, r * 2f, r * 2f);
                g.FillEllipse(Solid(Shade(skin, 0.8f)), -r * 0.7f, -r * 0.2f, r * 1.4f, r * 1.1f);
                g.FillEllipse(Solid(flash ? Color.White : Color.FromArgb(180, 150, 108)), -r * 0.45f, -r * 0.75f, r * 0.9f, r * 0.9f);
                Circle(g, Color.FromArgb(255, 90, 60), -r * 0.2f, -r * 0.35f, 1.8f);
                Circle(g, Color.FromArgb(255, 90, 60), r * 0.2f, -r * 0.35f, 1.8f);
                var s2 = g.Save();
                g.RotateTransform(m.Facing * 180f / MathF.PI);
                float lift = m.State == AiState.Windup ? -1.1f : 0.35f;
                g.RotateTransform(lift * 57.3f);
                g.FillRectangle(Solid(Color.FromArgb(110, 76, 44)), r * 0.5f, -4f, 26f, 8f);
                g.FillEllipse(Solid(Color.FromArgb(90, 62, 38)), r * 0.5f + 18f, -7f, 14f, 14f);
                g.Restore(s2);
                break;
            }

            default:   // Bone King
            {
                bool enraged = m.Hp < m.MaxHp * 0.5f;
                if (enraged)
                {
                    float pulse = 0.5f + 0.5f * MathF.Sin(m.Anim * 8f);
                    Circle(g, Color.FromArgb((int)(60 * pulse), 200, 60, 255), 0f, 0f, r + 14f);
                }
                g.FillEllipse(Solid(Color.FromArgb(70, 30, 100)), -r * 1.25f, -r * 0.9f, r * 2.5f, r * 1.8f);
                Color bone = flash ? Color.White : Color.FromArgb(225, 222, 205);
                g.FillEllipse(Solid(flash ? Color.White : Color.FromArgb(190, 186, 170)), -r, -r * 0.6f, r * 2f, r * 1.3f);
                g.FillEllipse(Solid(bone), -r * 0.7f, -r * 0.85f, r * 1.4f, r * 1.4f);
                Circle(g, Color.FromArgb(200, 100, 255), -r * 0.25f, -r * 0.3f, 3f);
                Circle(g, Color.FromArgb(200, 100, 255), r * 0.25f, -r * 0.3f, 3f);
                var crown = new[]
                {
                    new PointF(-r * 0.6f, -r * 0.7f), new PointF(-r * 0.6f, -r * 1.25f), new PointF(-r * 0.3f, -r * 0.95f),
                    new PointF(0f, -r * 1.35f), new PointF(r * 0.3f, -r * 0.95f), new PointF(r * 0.6f, -r * 1.25f),
                    new PointF(r * 0.6f, -r * 0.7f),
                };
                g.FillPolygon(Solid(Color.FromArgb(245, 200, 60)), crown);
                g.DrawPolygon(OutlinePen, crown);
                break;
            }
        }

        // Health bar for damaged non-boss monsters.
        if (!m.IsBoss && m.Hp < m.MaxHp)
        {
            float bw = r * 2f + 8f;
            g.FillRectangle(Solid(Color.FromArgb(190, 10, 10, 12)), -bw / 2f - 1f, -r - 11f, bw + 2f, 5f);
            g.FillRectangle(Solid(Color.FromArgb(220, 60, 60)), -bw / 2f, -r - 10f, bw * (m.Hp / m.MaxHp), 3f);
        }

        g.Restore(st);
    }

    // ---------------------------------------------------------------- player

    private static void DrawPlayer(Graphics g, World world)
    {
        var p = world.Player;
        if (p.Invuln > 0f && p.DashTimer <= 0f && ((int)(world.Time * 25f)) % 2 == 0) return;   // damage blink

        // Sword swing sweep (world space).
        if (p.SwingTimer > 0f)
        {
            float k = p.SwingTimer / 0.16f;
            float startDeg = p.Facing * 180f / MathF.PI - 56f;
            g.FillPie(Solid(Color.FromArgb((int)(120 * k), 235, 240, 255)), p.Pos.X - 58f, p.Pos.Y - 58f, 116f, 116f, startDeg, 112f);
        }

        var st = g.Save();
        g.TranslateTransform(p.Pos.X, p.Pos.Y);
        g.FillEllipse(ShadowBrush, -10f, 4f, 20f, 9f);
        g.RotateTransform(p.Facing * 180f / MathF.PI);

        bool flash = p.HitFlash > 0f;
        float bob = MathF.Sin(p.WalkAnim) * 0.8f;
        Color body = flash ? Color.White : Color.FromArgb(70, 120, 215);

        g.FillEllipse(Solid(Color.FromArgb(150, 105, 50)), 0f, 4f, 11f, 11f);         // shield
        g.FillEllipse(Solid(body), -11f, -11f + bob, 22f, 22f);                      // body
        g.DrawEllipse(OutlinePen, -11f, -11f + bob, 22f, 22f);
        g.FillEllipse(Solid(flash ? Color.White : Color.FromArgb(175, 185, 205)), -6.5f, -6.5f + bob, 13f, 13f);   // helmet
        g.FillRectangle(Solid(Color.FromArgb(30, 30, 40)), 1f, -2f + bob, 6f, 4f);   // visor

        // Sword.
        var s2 = g.Save();
        float swing = 0f;
        if (p.SwingTimer > 0f) swing = (-1f + 2f * (1f - p.SwingTimer / 0.16f)) * 57.3f;
        else swing = 30f;
        g.RotateTransform(swing);
        g.FillRectangle(Solid(Color.FromArgb(215, 220, 230)), 8f, -2f, p.SwingTimer > 0f ? 40f : 24f, 4f);
        g.FillRectangle(Solid(Color.FromArgb(150, 110, 60)), 6f, -3f, 3f, 6f);
        g.Restore(s2);

        g.Restore(st);
    }

    private static void DrawProjectiles(Graphics g, World world)
    {
        var map = world.Map;
        foreach (var pr in world.Projectiles)
        {
            Point t = DungeonMap.TileOf(pr.Pos);
            if (t.X < 0 || t.Y < 0 || t.X >= DungeonMap.Width || t.Y >= DungeonMap.Height) continue;
            if (!map.Visible[t.X, t.Y] && !pr.FromPlayer) continue;

            switch (pr.Kind)
            {
                case ProjKind.Fireball:
                    Circle(g, Color.FromArgb(70, 255, 140, 30), pr.Pos.X, pr.Pos.Y, 15f);
                    Circle(g, Color.FromArgb(255, 150, 40), pr.Pos.X, pr.Pos.Y, 8f);
                    Circle(g, Color.FromArgb(255, 235, 140), pr.Pos.X, pr.Pos.Y, 4f);
                    break;

                case ProjKind.Orb:
                    Circle(g, Color.FromArgb(80, 190, 90, 240), pr.Pos.X, pr.Pos.Y, 14f);
                    Circle(g, Color.FromArgb(190, 100, 245), pr.Pos.X, pr.Pos.Y, 8f);
                    Circle(g, Color.FromArgb(240, 210, 255), pr.Pos.X, pr.Pos.Y, 3.5f);
                    break;

                default:
                {
                    Vector2 dir = MathUtil.SafeNormalize(pr.Vel);
                    Vector2 tail = pr.Pos - dir * 16f;
                    using var pen = new Pen(Color.FromArgb(180, 140, 90), 2f);
                    g.DrawLine(pen, tail.X, tail.Y, pr.Pos.X, pr.Pos.Y);
                    Circle(g, Color.FromArgb(220, 220, 230), pr.Pos.X, pr.Pos.Y, 2.5f);
                    break;
                }
            }
        }
    }

    private static void DrawParticles(Graphics g, World world)
    {
        var parts = world.Fx.Items;
        for (int i = 0; i < parts.Length; i++)
        {
            ref Particle p = ref parts[i];
            if (!p.Active) continue;
            float k = p.Life / p.MaxLife;
            float size = MathUtil.Lerp(p.Size1, p.Size0, k);
            PartBrush.Color = Color.FromArgb((int)(p.Color.A * k), p.Color.R, p.Color.G, p.Color.B);
            g.FillEllipse(PartBrush, p.Pos.X - size / 2f, p.Pos.Y - size / 2f, size, size);
        }
    }

    private static void DrawFloatTexts(Graphics g, World world)
    {
        foreach (var t in world.Texts)
        {
            float k = Math.Clamp(t.Life / t.MaxLife, 0f, 1f);
            int a = (int)(255 * MathF.Min(1f, k * 2f));
            using var font = new Font("Segoe UI Black", t.Size * 0.75f, FontStyle.Regular, GraphicsUnit.Pixel);
            g.DrawString(t.Text, font, Solid(Color.FromArgb(a / 2, 0, 0, 0)), t.Pos.X + 1f, t.Pos.Y + 1f, Mid);
            g.DrawString(t.Text, font, Solid(Color.FromArgb(a, t.Color.R, t.Color.G, t.Color.B)), t.Pos.X, t.Pos.Y, Mid);
        }
    }

    // ------------------------------------------------------------------- HUD

    private static void DrawHud(Graphics g, int w, int h, World world)
    {
        var p = world.Player;

        // Top-left: floor / gold / kills.
        Panel(g, 14f, 14f, 210f, 74f);
        Text(g, $"FLOOR {world.Floor}", H2Font, Color.White, 26f, 20f, Near);
        Text(g, $"Gold {p.Gold}", BodyFont, Color.Gold, 26f, 50f, Near);
        Text(g, $"Kills {p.Kills}", SmallFont, Color.FromArgb(200, 205, 220), 130f, 54f, Near);

        // Bottom-left: bars and potions.
        float bx = 20f, by = h - 120f;
        Panel(g, bx - 8f, by - 34f, 300f, 140f, 12f);
        Text(g, $"LV {p.Level}", H2Font, Color.FromArgb(255, 220, 110), bx, by - 30f, Near);
        Bar(g, bx, by, 280f, 22f, p.Hp / p.MaxHp, Color.FromArgb(215, 55, 60));
        Text(g, $"{(int)p.Hp} / {p.MaxHp}", BoldSmall, Color.White, bx + 140f, by + 3f, TopCenter);
        Bar(g, bx, by + 30f, 280f, 14f, p.Mana / p.MaxMana, Color.FromArgb(60, 120, 235));
        Bar(g, bx, by + 52f, 280f, 8f, p.Xp / (float)p.XpToNext, Color.FromArgb(150, 210, 90));
        Text(g, $"Q  Health x{p.HealthPotions}", SmallFont, Color.FromArgb(255, 140, 140), bx, by + 68f, Near);
        Text(g, $"F  Mana x{p.ManaPotions}", SmallFont, Color.FromArgb(140, 180, 255), bx + 150f, by + 68f, Near);

        DrawAbilityIcons(g, w, h, world);
        DrawEquipment(g, w, h, p);
        DrawMinimap(g, w, world);

        // Log.
        float ly = h - 160f;
        for (int i = world.Log.Count - 1; i >= 0; i--)
        {
            var l = world.Log[i];
            float fade = Math.Clamp((7f - l.Age) / 1.5f, 0f, 1f);
            Text(g, l.Text, BodyFont, Color.FromArgb((int)(255 * fade), l.Color.R, l.Color.G, l.Color.B), 20f, ly - 28f, Near);
            ly -= 22f;
        }

        // Stairs prompt.
        Point tile = DungeonMap.TileOf(p.Pos);
        if (world.Map.Tiles[tile.X, tile.Y] == Tile.Stairs)
        {
            string msg = world.StairsOpen ? "Press E to descend" : "The stairs are sealed...";
            Text(g, msg, H2Font, world.StairsOpen ? Color.Gold : Color.OrangeRed, w / 2f, h * 0.62f, Mid);
        }

        // Boss bar.
        if (world.Boss != null && !world.Boss.Dead && world.Boss.Alerted)
        {
            float bw = 520f;
            float x = (w - bw) / 2f;
            Text(g, "THE BONE KING", H2Font, Color.FromArgb(230, 190, 255), w / 2f, 18f, TopCenter);
            Bar(g, x, 50f, bw, 18f, world.Boss.Hp / world.Boss.MaxHp, Color.FromArgb(160, 60, 210));
        }
    }

    private static void DrawAbilityIcons(Graphics g, int w, int h, World world)
    {
        var p = world.Player;
        float size = 46f, gap = 12f;
        float totalW = size * 3f + gap * 2f;
        float x = (w - totalW) / 2f, y = h - size - 18f;

        DrawIcon(g, x, y, size, "LMB", "Sword", p.MeleeCd / (0.38f * p.MeleeCdMul), true, Color.FromArgb(220, 225, 240));
        DrawIcon(g, x + size + gap, y, size, "RMB", "Fire", p.FireCd / 0.45f, p.Mana >= 18f, Color.FromArgb(255, 150, 50));
        DrawIcon(g, x + (size + gap) * 2f, y, size, "SPACE", "Dash", p.DashCd / (1.3f * p.DashCdMul), true, Color.FromArgb(150, 200, 255));
    }

    private static void DrawIcon(Graphics g, float x, float y, float s, string key, string label, float cooldownFrac, bool usable, Color accent)
    {
        g.FillRectangle(Solid(Color.FromArgb(180, 10, 10, 16)), x, y, s, s);
        Color c = usable ? accent : Color.FromArgb(90, 90, 110);
        using var pen = new Pen(c, 2f);
        g.DrawRectangle(pen, x, y, s, s);
        Text(g, label, SmallFont, c, x + s / 2f, y + s / 2f - 4f, Mid, false);
        Text(g, key, SmallFont, Color.FromArgb(170, 175, 195), x + s / 2f, y + s + 8f, Mid, false);
        float cd = Math.Clamp(cooldownFrac, 0f, 1f);
        if (cd > 0f)
            g.FillRectangle(Solid(Color.FromArgb(170, 0, 0, 0)), x, y, s, s * cd);
    }

    private static void DrawEquipment(Graphics g, int w, int h, Player p)
    {
        float pw = 330f, ph = 124f;
        float x = w - pw - 14f, y = h - ph - 14f;
        Panel(g, x, y, pw, ph, 12f);

        DrawSlotLine(g, x + 12f, y + 8f, "WPN", p.Weapon);
        DrawSlotLine(g, x + 12f, y + 32f, "ARM", p.Armor);
        DrawSlotLine(g, x + 12f, y + 56f, "CHM", p.Charm);

        string stats = $"DMG {(int)p.MeleeDamage}   ARM {p.ArmorValue}   CRIT {(int)(p.CritChance * 100f)}%   SPD {(int)p.MoveSpeed}";
        Text(g, stats, SmallFont, Color.FromArgb(200, 205, 225), x + 12f, y + 90f, Near, false);
    }

    private static void DrawSlotLine(Graphics g, float x, float y, string label, Item? item)
    {
        Text(g, label, BoldSmall, Color.FromArgb(150, 155, 175), x, y + 2f, Near, false);
        if (item == null)
        {
            Text(g, "- empty -", SmallFont, Color.FromArgb(110, 112, 130), x + 44f, y + 2f, Near, false);
            return;
        }
        Text(g, item.Name, BoldSmall, item.Color, x + 44f, y + 2f, Near, false);
        Text(g, item.Describe(), SmallFont, Color.FromArgb(185, 190, 210), x + 44f, y + 16f, Near, false);
    }

    private static void DrawMinimap(Graphics g, int w, World world)
    {
        var map = world.Map;
        const int px = 3;
        int bw = DungeonMap.Width * px, bh = DungeonMap.Height * px;

        // Rebuild the cached bitmap only when new tiles have been explored.
        if (miniBmp == null || !ReferenceEquals(miniFor, map) || miniVersion != map.ExploredVersion)
        {
            miniBmp?.Dispose();
            miniBmp = new Bitmap(bw, bh);
            using (var mg = Graphics.FromImage(miniBmp))
            {
                mg.Clear(Color.Transparent);
                using var floor = new SolidBrush(Color.FromArgb(205, 100, 100, 125));
                using var stairs = new SolidBrush(Color.FromArgb(255, 235, 200, 70));
                for (int y = 0; y < DungeonMap.Height; y++)
                {
                    for (int x = 0; x < DungeonMap.Width; x++)
                    {
                        if (!map.Explored[x, y] || map.Tiles[x, y] == Tile.Wall) continue;
                        mg.FillRectangle(map.Tiles[x, y] == Tile.Stairs ? stairs : floor, x * px, y * px, px, px);
                    }
                }
            }
            miniFor = map;
            miniVersion = map.ExploredVersion;
        }

        float ox = w - bw - 22f, oy = 18f;
        Panel(g, ox - 8f, oy - 8f, bw + 16f, bh + 16f, 10f);
        g.DrawImage(miniBmp, ox, oy, bw, bh);

        // Player and visible monsters.
        Point pt = DungeonMap.TileOf(world.Player.Pos);
        float ts = DungeonMap.TileSize;
        foreach (var m in world.Monsters)
        {
            Point mt = DungeonMap.TileOf(m.Pos);
            if (!map.Visible[mt.X, mt.Y]) continue;
            Circle(g, m.IsBoss ? Color.FromArgb(200, 100, 255) : Color.FromArgb(230, 70, 70),
                ox + m.Pos.X / ts * px, oy + m.Pos.Y / ts * px, m.IsBoss ? 3.5f : 2f);
        }
        Circle(g, Color.White, ox + world.Player.Pos.X / ts * px, oy + world.Player.Pos.Y / ts * px, 3.2f);
        _ = pt;
    }

    // --------------------------------------------------------------- screens

    private static void DrawTitle(Graphics g, int w, int h)
    {
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(12, 10, 20), Color.FromArgb(44, 22, 40), 90f))
            g.FillRectangle(bg, 0, 0, w, h);

        float t = (float)(Environment.TickCount64 % 100000) / 1000f;
        for (int i = 0; i < 4; i++)
        {
            float tx = w * (0.15f + 0.23f * i);
            float fl = 0.8f + 0.2f * MathF.Sin(t * 10f + i * 2f);
            g.FillEllipse(Solid(Color.FromArgb((int)(36 * fl), 255, 170, 60)), tx - 90f, h * 0.2f - 90f, 180f, 180f);
            g.FillEllipse(Solid(Color.FromArgb(255, 255, 150, 40)), tx - 6f, h * 0.2f - 14f * fl, 12f, 22f * fl);
        }

        Text(g, "DUNGEON DELVE", TitleFont, Color.FromArgb(240, 225, 190), w / 2f, h * 0.22f, Mid);
        Text(g, "Descend. Fight. Loot. Survive.", H2Font, Color.FromArgb(190, 170, 200), w / 2f, h * 0.22f + 60f, Mid);

        float y = h * 0.42f;
        string[] lines =
        {
            "WASD / Arrows   move",
            "Mouse           aim",
            "Left click      sword swing",
            "Right click     fireball (costs mana)",
            "Space           dash (brief invulnerability)",
            "Q / F           health / mana potion",
            "E               take the stairs down",
            "Esc / P         pause",
        };
        using (var mono = new Font("Consolas", 19f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            foreach (var line in lines)
            {
                Text(g, line, mono, Color.FromArgb(215, 215, 230), w / 2f - 190f, y, Near, false);
                y += 28f;
            }
        }

        float pulse = 0.65f + 0.35f * MathF.Sin(t * 4f);
        Text(g, "Press ENTER to descend", H1Font, Color.FromArgb((int)(255 * pulse), 255, 210, 100), w / 2f, h - 80f, Mid);
    }

    private static void DrawPause(Graphics g, int w, int h)
    {
        g.FillRectangle(Solid(Color.FromArgb(150, 0, 0, 0)), 0, 0, w, h);
        Text(g, "PAUSED", TitleFont, Color.White, w / 2f, h / 2f - 20f, Mid);
        Text(g, "Esc / P  resume        ENTER  quit to title", H2Font, Color.FromArgb(210, 215, 230), w / 2f, h / 2f + 50f, Mid);
    }

    private static void DrawLevelUp(Graphics g, int w, int h, World world)
    {
        g.FillRectangle(Solid(Color.FromArgb(170, 0, 0, 0)), 0, 0, w, h);
        Text(g, "LEVEL UP!", TitleFont, Color.Gold, w / 2f, h * 0.2f, Mid);
        Text(g, $"Reached level {world.Player.Level}  -  choose a perk (press 1, 2 or 3)", H2Font, Color.FromArgb(220, 225, 240), w / 2f, h * 0.2f + 56f, Mid);

        float cw = 280f, ch = 190f, gap = 24f;
        int n = world.PerkChoices.Count;
        float total = cw * n + gap * (n - 1);
        float x0 = (w - total) / 2f;
        float y0 = h * 0.4f;

        for (int i = 0; i < n; i++)
        {
            var perk = world.PerkChoices[i];
            float x = x0 + i * (cw + gap);
            Panel(g, x, y0, cw, ch, 14f, 215);
            using (var pen = new Pen(Color.FromArgb(220, 190, 80), 2f))
            using (var path = RoundedRect(x, y0, cw, ch, 14f))
                g.DrawPath(pen, path);
            Text(g, $"[{i + 1}]", H1Font, Color.Gold, x + cw / 2f, y0 + 18f, TopCenter);
            Text(g, perk.Name, H2Font, Color.White, x + cw / 2f, y0 + 66f, TopCenter);
            g.DrawString(perk.Description, BodyFont, Solid(Color.FromArgb(205, 210, 228)),
                new RectangleF(x + 18f, y0 + 106f, cw - 36f, ch - 112f), TopCenter);
        }
    }

    private static void DrawDead(Graphics g, int w, int h, World world)
    {
        var p = world.Player;
        g.FillRectangle(Solid(Color.FromArgb(185, 20, 0, 0)), 0, 0, w, h);
        Text(g, "YOU DIED", TitleFont, Color.FromArgb(230, 70, 70), w / 2f, h * 0.24f, Mid);

        int mins = (int)(world.PlayTime / 60f);
        int secs = (int)(world.PlayTime % 60f);
        string[] lines =
        {
            $"Reached floor {world.Floor}   -   level {p.Level}",
            $"Monsters slain: {p.Kills}",
            $"Gold: {p.Gold}",
            $"Time: {mins}:{secs:00}",
        };
        float y = h * 0.4f;
        foreach (var line in lines)
        {
            Text(g, line, H2Font, Color.FromArgb(235, 225, 225), w / 2f, y, Mid);
            y += 36f;
        }
        Text(g, $"SCORE  {world.Score}", H1Font, Color.Gold, w / 2f, y + 24f, Mid);
        Text(g, "ENTER  try again        ESC  title", BodyFont, Color.FromArgb(220, 210, 215), w / 2f, h - 80f, Mid);
    }
}
