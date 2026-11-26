using System.Numerics;

namespace TankBattle;

public enum GameState { Playing, Paused, GameOver }

/// <summary>Snapshot of player input for one frame.</summary>
public sealed class InputState
{
    public Vector2 Move;
    public Vector2 Aim;
    public bool Fire;
}

public sealed class Game
{
    public const int Width = 1024;
    public const int Height = 768;

    private const float BulletSpeed = 520f;
    private const float PlayerBulletDamage = 30f;
    private const float EnemyBulletDamage = 15f;

    private readonly Random rng = new();

    public PlayerTank Player { get; private set; } = null!;
    public List<EnemyTank> Enemies { get; } = new();
    public List<Bullet> Bullets { get; } = new();
    public List<Obstacle> Obstacles { get; } = new();
    public List<Particle> Particles { get; } = new();

    public int Score { get; private set; }
    public int Wave { get; private set; }
    public int Kills { get; private set; }
    public GameState State { get; private set; }
    public float WaveBanner { get; private set; }

    private int toSpawn;
    private float spawnTimer;

    public Game() => Reset();

    // ------------------------------------------------------------------ setup

    public void Reset()
    {
        Enemies.Clear();
        Bullets.Clear();
        Particles.Clear();
        Score = 0;
        Kills = 0;
        Wave = 0;
        Player = new PlayerTank(new Vector2(Width / 2f, Height / 2f));
        BuildObstacles();
        State = GameState.Playing;
        StartNextWave();
    }

    public void TogglePause()
    {
        if (State == GameState.Playing) State = GameState.Paused;
        else if (State == GameState.Paused) State = GameState.Playing;
    }

    public void PauseIfPlaying()
    {
        if (State == GameState.Playing) State = GameState.Paused;
    }

    private void BuildObstacles()
    {
        Obstacles.Clear();
        var spawnZone = new RectangleF(Width / 2f - 130, Height / 2f - 130, 260, 260);

        for (int attempt = 0; attempt < 300 && Obstacles.Count < 12; attempt++)
        {
            bool horizontal = rng.Next(2) == 0;
            float w = horizontal ? rng.Next(90, 170) : rng.Next(30, 60);
            float h = horizontal ? rng.Next(30, 60) : rng.Next(90, 170);
            float x = rng.Next(70, (int)(Width - 70 - w));
            float y = rng.Next(70, (int)(Height - 70 - h));
            var rect = new RectangleF(x, y, w, h);

            if (rect.IntersectsWith(spawnZone)) continue;

            // Keep gaps wide enough for a tank (diameter 40) to squeeze between blocks.
            var padded = RectangleF.Inflate(rect, 55, 55);
            if (Obstacles.Any(o => o.Bounds.IntersectsWith(padded))) continue;

            Obstacles.Add(new Obstacle(rect, destructible: rng.Next(2) == 0));
        }
    }

    private void StartNextWave()
    {
        Wave++;
        toSpawn = 3 + Wave * 2;
        spawnTimer = 1f;
        WaveBanner = 2.5f;
        if (Wave > 1)
            Player.Health = MathF.Min(Player.MaxHealth, Player.Health + 25f);
    }

    // ----------------------------------------------------------------- update

    public void Update(float dt, InputState input)
    {
        if (State == GameState.Paused) return;

        UpdateParticles(dt);
        if (State == GameState.GameOver) return;

        WaveBanner = MathF.Max(0f, WaveBanner - dt);

        UpdatePlayer(dt, input);
        UpdateEnemies(dt);
        UpdateBullets(dt);
        UpdateWaves(dt);

        if (!Player.IsAlive)
        {
            Explode(Player.Position, 60, Color.OrangeRed);
            State = GameState.GameOver;
        }
    }

    private void UpdatePlayer(float dt, InputState input)
    {
        var p = Player;
        p.ReloadTimer = MathF.Max(0f, p.ReloadTimer - dt);
        p.HitFlash = MathF.Max(0f, p.HitFlash - dt);

        if (input.Move != Vector2.Zero)
        {
            var dir = Vector2.Normalize(input.Move);
            MoveTank(p, dir * p.Speed * dt);
            p.BodyAngle = TurnToward(p.BodyAngle, MathF.Atan2(dir.Y, dir.X), 9f * dt);
        }

        var aim = input.Aim - p.Position;
        if (aim.LengthSquared() > 1f)
            p.TurretAngle = MathF.Atan2(aim.Y, aim.X);

        if (input.Fire && p.ReloadTimer <= 0f)
        {
            Shoot(p, fromPlayer: true, spread: 0f);
            p.ReloadTimer = p.ReloadTime;
        }
    }

    private void UpdateEnemies(float dt)
    {
        foreach (var e in Enemies)
        {
            e.ReloadTimer = MathF.Max(0f, e.ReloadTimer - dt);
            e.HitFlash = MathF.Max(0f, e.HitFlash - dt);
            e.StrafeTimer -= dt;
            e.AvoidTimer -= dt;

            var toPlayer = Player.Position - e.Position;
            float dist = toPlayer.Length();
            var dir = dist > 0.001f ? toPlayer / dist : Vector2.UnitX;

            if (e.StrafeTimer <= 0f)
            {
                e.StrafeDir = rng.Next(2) == 0 ? -1f : 1f;
                e.StrafeTimer = 1.5f + rng.NextSingle() * 2f;
            }
            var perp = new Vector2(-dir.Y, dir.X) * e.StrafeDir;

            // Behaviour: close the gap, then circle at mid range, back off if too near.
            Vector2 move;
            if (e.AvoidTimer > 0f) move = perp;
            else if (dist > 280f) move = dir;
            else if (dist < 170f) move = -dir * 0.7f + perp * 0.3f;
            else move = perp * 0.6f;

            bool blocked = MoveTank(e, move * e.Speed * dt);
            if (blocked && e.AvoidTimer <= 0f)
            {
                // Hit a wall or another tank: sidestep for a moment.
                e.AvoidTimer = 0.7f;
                e.StrafeDir = -e.StrafeDir;
            }
            if (move.LengthSquared() > 0.01f)
                e.BodyAngle = TurnToward(e.BodyAngle, MathF.Atan2(move.Y, move.X), 4f * dt);

            // Aim and fire.
            float desired = MathF.Atan2(toPlayer.Y, toPlayer.X);
            e.TurretAngle = TurnToward(e.TurretAngle, desired, 2.4f * dt);
            float error = MathF.Abs(NormalizeAngle(desired - e.TurretAngle));

            if (e.ReloadTimer <= 0f && dist < 560f && error < 0.12f && HasLineOfSight(e.Position, Player.Position))
            {
                Shoot(e, fromPlayer: false, spread: (rng.NextSingle() - 0.5f) * 0.12f);
                e.ReloadTimer = e.ReloadTime * (0.8f + rng.NextSingle() * 0.6f);
            }
        }
    }

    private void UpdateBullets(float dt)
    {
        for (int i = Bullets.Count - 1; i >= 0; i--)
        {
            var b = Bullets[i];
            b.Position += b.Velocity * dt;
            b.Life -= dt;

            bool dead = b.Life <= 0f
                || b.Position.X < 0 || b.Position.X > Width
                || b.Position.Y < 0 || b.Position.Y > Height;

            // Obstacles
            if (!dead)
            {
                for (int j = Obstacles.Count - 1; j >= 0; j--)
                {
                    var o = Obstacles[j];
                    if (!CircleHitsRect(b.Position, Bullet.Radius, o.Bounds)) continue;

                    Explode(b.Position, 6, Color.Gray);
                    if (o.Destructible && --o.Hp <= 0)
                    {
                        Explode(new Vector2(o.Bounds.X + o.Bounds.Width / 2, o.Bounds.Y + o.Bounds.Height / 2), 25, Color.SaddleBrown);
                        Obstacles.RemoveAt(j);
                    }
                    dead = true;
                    break;
                }
            }

            // Tanks
            if (!dead)
            {
                if (b.FromPlayer)
                {
                    for (int j = Enemies.Count - 1; j >= 0; j--)
                    {
                        var e = Enemies[j];
                        if (Vector2.Distance(b.Position, e.Position) > e.Radius + Bullet.Radius) continue;

                        e.Health -= PlayerBulletDamage;
                        e.HitFlash = 0.1f;
                        Explode(b.Position, 8, Color.Orange);
                        if (!e.IsAlive)
                        {
                            Score += 100;
                            Kills++;
                            Explode(e.Position, 45, Color.OrangeRed);
                            Enemies.RemoveAt(j);
                        }
                        dead = true;
                        break;
                    }
                }
                else if (Vector2.Distance(b.Position, Player.Position) <= Player.Radius + Bullet.Radius)
                {
                    Player.Health -= EnemyBulletDamage;
                    Player.HitFlash = 0.1f;
                    Explode(b.Position, 8, Color.Orange);
                    dead = true;
                }
            }

            if (dead) Bullets.RemoveAt(i);
        }
    }

    private void UpdateWaves(float dt)
    {
        if (toSpawn > 0)
        {
            spawnTimer -= dt;
            if (spawnTimer <= 0f && Enemies.Count < Math.Min(3 + Wave, 8))
            {
                SpawnEnemy();
                toSpawn--;
                spawnTimer = 1.5f;
            }
        }
        else if (Enemies.Count == 0)
        {
            Score += 50 * Wave;   // wave-clear bonus
            StartNextWave();
        }
    }

    private void SpawnEnemy()
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            Vector2 pos = rng.Next(4) switch
            {
                0 => new Vector2(rng.Next(30, Width - 30), 30),
                1 => new Vector2(rng.Next(30, Width - 30), Height - 30),
                2 => new Vector2(30, rng.Next(30, Height - 30)),
                _ => new Vector2(Width - 30, rng.Next(30, Height - 30)),
            };

            if (Vector2.Distance(pos, Player.Position) < 350f) continue;
            if (Obstacles.Any(o => CircleHitsRect(pos, 26f, o.Bounds))) continue;
            if (Enemies.Any(e => Vector2.Distance(pos, e.Position) < 60f)) continue;

            var enemy = new EnemyTank(pos, Wave, rng);
            enemy.TurretAngle = enemy.BodyAngle = MathF.Atan2(Player.Position.Y - pos.Y, Player.Position.X - pos.X);
            Enemies.Add(enemy);
            return;
        }
    }

    private void UpdateParticles(float dt)
    {
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Life -= dt;
            if (p.Life <= 0f) { Particles.RemoveAt(i); continue; }
            p.Position += p.Velocity * dt;
            p.Velocity *= MathF.Max(0f, 1f - 3f * dt);
        }
    }

    // ---------------------------------------------------------------- actions

    private void Shoot(Tank t, bool fromPlayer, float spread)
    {
        float a = t.TurretAngle + spread;
        var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
        var muzzle = t.Position + dir * (t.Radius + 16f);
        Bullets.Add(new Bullet(muzzle, dir * BulletSpeed, fromPlayer));
        Explode(muzzle, 4, Color.Khaki);
    }

    private void Explode(Vector2 pos, int count, Color baseColor)
    {
        Color[] palette = { baseColor, Color.Orange, Color.Gold, Color.DimGray };
        for (int i = 0; i < count; i++)
        {
            float angle = rng.NextSingle() * MathF.Tau;
            float speed = 40f + rng.NextSingle() * 220f;
            float life = 0.3f + rng.NextSingle() * 0.6f;
            Particles.Add(new Particle
            {
                Position = pos,
                Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
                Life = life,
                MaxLife = life,
                Size = 2f + rng.NextSingle() * 4f,
                Color = palette[rng.Next(palette.Length)],
            });
        }
    }

    // -------------------------------------------------------------- collision

    /// <summary>Moves a tank, resolving collisions with walls, obstacles and other tanks. Returns true if blocked.</summary>
    private bool MoveTank(Tank t, Vector2 delta)
    {
        var pos = t.Position + delta;
        bool blocked = false;

        foreach (var o in Obstacles)
            if (PushOutOfRect(ref pos, t.Radius, o.Bounds)) blocked = true;

        if (t != Player && Player.IsAlive && PushOutOfCircle(ref pos, t.Radius, Player)) blocked = true;
        foreach (var other in Enemies)
            if (other != t && PushOutOfCircle(ref pos, t.Radius, other)) blocked = true;

        pos.X = Math.Clamp(pos.X, t.Radius, Width - t.Radius);
        pos.Y = Math.Clamp(pos.Y, t.Radius, Height - t.Radius);
        t.Position = pos;
        return blocked;
    }

    private static bool PushOutOfCircle(ref Vector2 pos, float radius, Tank other)
    {
        var d = pos - other.Position;
        float min = radius + other.Radius;
        float dist = d.Length();
        if (dist >= min) return false;
        pos += (dist > 0.001f ? d / dist : Vector2.UnitX) * (min - dist);
        return true;
    }

    private static bool PushOutOfRect(ref Vector2 p, float r, RectangleF rc)
    {
        float cx = Math.Clamp(p.X, rc.Left, rc.Right);
        float cy = Math.Clamp(p.Y, rc.Top, rc.Bottom);
        var d = new Vector2(p.X - cx, p.Y - cy);
        float dist = d.Length();
        if (dist >= r) return false;

        if (dist > 0.0001f)
        {
            p += d / dist * (r - dist);
        }
        else
        {
            // Centre is inside the rectangle: exit through the nearest edge.
            float l = p.X - rc.Left, rt = rc.Right - p.X, t = p.Y - rc.Top, b = rc.Bottom - p.Y;
            float m = MathF.Min(MathF.Min(l, rt), MathF.Min(t, b));
            if (m == l) p.X = rc.Left - r;
            else if (m == rt) p.X = rc.Right + r;
            else if (m == t) p.Y = rc.Top - r;
            else p.Y = rc.Bottom + r;
        }
        return true;
    }

    private static bool CircleHitsRect(Vector2 c, float r, RectangleF rc)
    {
        float cx = Math.Clamp(c.X, rc.Left, rc.Right);
        float cy = Math.Clamp(c.Y, rc.Top, rc.Bottom);
        float dx = c.X - cx, dy = c.Y - cy;
        return dx * dx + dy * dy < r * r;
    }

    private bool HasLineOfSight(Vector2 from, Vector2 to)
    {
        var d = to - from;
        float len = d.Length();
        int steps = Math.Max(1, (int)(len / 16f));
        for (int i = 1; i < steps; i++)
        {
            var p = from + d * (i / (float)steps);
            foreach (var o in Obstacles)
                if (o.Bounds.Contains(p.X, p.Y)) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ maths

    private static float NormalizeAngle(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    private static float TurnToward(float current, float target, float maxStep)
    {
        float diff = NormalizeAngle(target - current);
        if (MathF.Abs(diff) <= maxStep) return target;
        return current + MathF.Sign(diff) * maxStep;
    }
}
