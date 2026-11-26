using System.Drawing.Drawing2D;

namespace TankBattle;

/// <summary>All drawing lives here so the game logic stays free of GDI+.</summary>
public static class Renderer
{
    private static readonly Font HudFont = new("Segoe UI Semibold", 14f);
    private static readonly Font SmallFont = new("Segoe UI", 9f, FontStyle.Bold);
    private static readonly Font MidFont = new("Segoe UI", 18f);
    private static readonly Font BigFont = new("Segoe UI Black", 44f);
    private static readonly StringFormat Centered = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
    };

    public static void Draw(Graphics g, Game game)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        DrawGround(g);
        foreach (var o in game.Obstacles) DrawObstacle(g, o);

        foreach (var e in game.Enemies)
        {
            DrawTank(g, e, isPlayer: false);
            DrawHealthBar(g, e.Position.X - 22, e.Position.Y - 36, 44, 5, e.Health / e.MaxHealth);
        }
        if (game.Player.IsAlive) DrawTank(g, game.Player, isPlayer: true);

        foreach (var b in game.Bullets) DrawBullet(g, b);
        foreach (var p in game.Particles) DrawParticle(g, p);

        DrawHud(g, game);

        if (game.WaveBanner > 0f && game.State == GameState.Playing)
        {
            int alpha = (int)(255 * Math.Clamp(game.WaveBanner / 1f, 0f, 1f));
            using var br = new SolidBrush(Color.FromArgb(alpha, Color.White));
            g.DrawString($"WAVE {game.Wave}", BigFont, br, new RectangleF(0, 90, Game.Width, 80), Centered);
        }

        if (game.State == GameState.Paused)
            DrawOverlay(g, "PAUSED", "Press P to resume");
        else if (game.State == GameState.GameOver)
            DrawOverlay(g, "GAME OVER", $"Score {game.Score}  •  Wave {game.Wave}  •  Kills {game.Kills}\nPress Enter to play again");
    }

    // ------------------------------------------------------------- battlefield

    private static void DrawGround(Graphics g)
    {
        using var ground = new SolidBrush(Color.FromArgb(78, 88, 54));
        g.FillRectangle(ground, 0, 0, Game.Width, Game.Height);

        using var grid = new Pen(Color.FromArgb(40, 0, 0, 0), 1f);
        for (int x = 0; x <= Game.Width; x += 64) g.DrawLine(grid, x, 0, x, Game.Height);
        for (int y = 0; y <= Game.Height; y += 64) g.DrawLine(grid, 0, y, Game.Width, y);
    }

    private static void DrawObstacle(Graphics g, Obstacle o)
    {
        var r = o.Bounds;
        if (o.Destructible)
        {
            // Bricks darken as they take damage.
            float health = o.Hp / (float)o.MaxHp;
            int shade = (int)(70 + 80 * health);
            using var fill = new SolidBrush(Color.FromArgb(shade + 20, shade - 15, shade - 45));
            g.FillRectangle(fill, r);

            using var mortar = new Pen(Color.FromArgb(90, 40, 25, 20), 1.5f);
            for (float y = r.Top + 15; y < r.Bottom; y += 15)
                g.DrawLine(mortar, r.Left, y, r.Right, y);
            int row = 0;
            for (float y = r.Top; y < r.Bottom; y += 15, row++)
                for (float x = r.Left + (row % 2 == 0 ? 0 : 15); x < r.Right; x += 30)
                    g.DrawLine(mortar, x, y, x, Math.Min(y + 15, r.Bottom));
            using var border = new Pen(Color.FromArgb(60, 30, 20), 2f);
            g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(112, 118, 128));
            using var border = new Pen(Color.FromArgb(55, 60, 70), 3f);
            using var rivet = new SolidBrush(Color.FromArgb(70, 75, 85));
            g.FillRectangle(fill, r);
            g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
            foreach (var (rx, ry) in new[] { (r.Left + 7, r.Top + 7), (r.Right - 7, r.Top + 7), (r.Left + 7, r.Bottom - 7), (r.Right - 7, r.Bottom - 7) })
                g.FillEllipse(rivet, rx - 3, ry - 3, 6, 6);
        }
    }

    // ------------------------------------------------------------------- tanks

    private static void DrawTank(Graphics g, Tank t, bool isPlayer)
    {
        Color hull = isPlayer ? Color.FromArgb(62, 140, 62) : Color.FromArgb(170, 62, 52);
        Color track = isPlayer ? Color.FromArgb(28, 60, 28) : Color.FromArgb(72, 30, 26);
        Color turret = isPlayer ? Color.FromArgb(86, 172, 86) : Color.FromArgb(205, 84, 72);
        if (t.HitFlash > 0f) hull = turret = Color.White;

        using var hullBr = new SolidBrush(hull);
        using var trackBr = new SolidBrush(track);
        using var turretBr = new SolidBrush(turret);
        using var outline = new Pen(Color.FromArgb(160, 0, 0, 0), 1.5f);

        // Hull and tracks (rotated with the body).
        var state = g.Save();
        g.TranslateTransform(t.Position.X, t.Position.Y);
        g.RotateTransform(t.BodyAngle * 180f / MathF.PI);
        g.FillRectangle(trackBr, -22, -20, 44, 9);
        g.FillRectangle(trackBr, -22, 11, 44, 9);
        g.FillRectangle(hullBr, -20, -14, 40, 28);
        g.DrawRectangle(outline, -20, -14, 40, 28);
        g.DrawRectangle(outline, -22, -20, 44, 9);
        g.DrawRectangle(outline, -22, 11, 44, 9);
        g.Restore(state);

        // Turret and barrel (rotated independently, aimed by mouse / AI).
        state = g.Save();
        g.TranslateTransform(t.Position.X, t.Position.Y);
        g.RotateTransform(t.TurretAngle * 180f / MathF.PI);
        g.FillRectangle(turretBr, 0, -3.5f, 34, 7);
        g.DrawRectangle(outline, 0, -3.5f, 34, 7);
        g.FillEllipse(turretBr, -11, -11, 22, 22);
        g.DrawEllipse(outline, -11, -11, 22, 22);
        g.Restore(state);
    }

    private static void DrawHealthBar(Graphics g, float x, float y, float w, float h, float fraction)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        using var back = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
        using var fill = new SolidBrush(fraction > 0.5f ? Color.LimeGreen : fraction > 0.25f ? Color.Gold : Color.OrangeRed);
        g.FillRectangle(back, x - 1, y - 1, w + 2, h + 2);
        g.FillRectangle(fill, x, y, w * fraction, h);
    }

    // -------------------------------------------------------- bullets & effects

    private static void DrawBullet(Graphics g, Bullet b)
    {
        Color core = b.FromPlayer ? Color.Yellow : Color.OrangeRed;
        using var glow = new SolidBrush(Color.FromArgb(70, core));
        using var fill = new SolidBrush(core);
        const float r = Bullet.Radius;
        g.FillEllipse(glow, b.Position.X - r * 2, b.Position.Y - r * 2, r * 4, r * 4);
        g.FillEllipse(fill, b.Position.X - r, b.Position.Y - r, r * 2, r * 2);
    }

    private static void DrawParticle(Graphics g, Particle p)
    {
        int alpha = (int)(255 * Math.Clamp(p.Life / p.MaxLife, 0f, 1f));
        using var br = new SolidBrush(Color.FromArgb(alpha, p.Color));
        float s = p.Size * (0.5f + 0.5f * p.Life / p.MaxLife);
        g.FillEllipse(br, p.Position.X - s / 2, p.Position.Y - s / 2, s, s);
    }

    // --------------------------------------------------------------------- HUD

    private static void DrawHud(Graphics g, Game game)
    {
        using var text = new SolidBrush(Color.White);
        using var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0));

        DrawShadowed(g, $"SCORE  {game.Score}", 16, 12);
        DrawShadowed(g, $"WAVE  {game.Wave}", Game.Width - 110, 12);

        DrawHealthBar(g, 16, 44, 200, 16, game.Player.Health / game.Player.MaxHealth);
        g.DrawString("HEALTH", SmallFont, text, 18, 62);

        void DrawShadowed(Graphics gr, string s, float x, float y)
        {
            gr.DrawString(s, HudFont, shadow, x + 1.5f, y + 1.5f);
            gr.DrawString(s, HudFont, text, x, y);
        }
    }

    private static void DrawOverlay(Graphics g, string title, string subtitle)
    {
        using var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        using var white = new SolidBrush(Color.White);
        g.FillRectangle(dim, 0, 0, Game.Width, Game.Height);
        g.DrawString(title, BigFont, white, new RectangleF(0, 250, Game.Width, 100), Centered);
        g.DrawString(subtitle, MidFont, white, new RectangleF(0, 360, Game.Width, 100), Centered);
    }
}
