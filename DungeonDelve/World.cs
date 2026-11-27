using System.Numerics;

namespace DungeonDelve;

public enum Phase { Title, Playing, LevelUp, Paused, Dead }

/// <summary>Input for the current frame. Fields marked one-shot are consumed by the game.</summary>
public sealed class InputState
{
    public Vector2 Move;
    public Vector2 AimScreen;
    public bool Melee;
    public bool Fire;
    public bool Dash;          // one-shot
    public bool UseHealth;     // one-shot
    public bool UseMana;       // one-shot
    public bool Interact;      // one-shot
}

public sealed class World
{
    public const float Zoom = 1.5f;
    private const int VisionRadius = 9;
    private const float ExplosionRadius = 58f;

    public Phase Phase = Phase.Title;
    public readonly InputState Input = new();
    public Player Player = new();
    public DungeonMap Map = null!;

    public readonly List<Monster> Monsters = new();
    public readonly List<Projectile> Projectiles = new();
    public readonly List<Pickup> Pickups = new();
    public readonly List<FloatText> Texts = new();
    public readonly List<LogLine> Log = new();
    public readonly List<Perk> PerkChoices = new();
    public readonly ParticleSystem Fx = new();

    public Monster? Boss;
    public int Floor;
    public bool BossFloor;
    public float Time;
    public float PlayTime;
    public float Shake;
    public Vector2 Cam;
    public float ViewW = 1280f;
    public float ViewH = 720f;
    public int PendingLevelUps;

    private readonly Random rng = new();
    private int lastTx = -1;
    private int lastTy = -1;

    public bool StairsOpen => !BossFloor || Boss == null || Boss.Dead;
    public int Score => Player.Gold + Floor * 150 + Player.Kills * 5;

    // ----------------------------------------------------------- game flow

    public void NewGame()
    {
        Player = new Player();
        Floor = 0;
        PlayTime = 0f;
        PendingLevelUps = 0;
        Log.Clear();
        StartFloor(1);
        Player.Hp = Player.MaxHp;
        Player.Mana = Player.MaxMana;
        Phase = Phase.Playing;
    }

    public void ToTitle() => Phase = Phase.Title;

    public void TogglePause()
    {
        if (Phase == Phase.Playing) Phase = Phase.Paused;
        else if (Phase == Phase.Paused) Phase = Phase.Playing;
    }

    public void PauseIfPlaying()
    {
        if (Phase == Phase.Playing) Phase = Phase.Paused;
    }

    private void StartFloor(int floor)
    {
        Floor = floor;
        BossFloor = floor % 4 == 0;
        Boss = null;
        Map = DungeonMap.Generate(floor, rng);
        Monsters.Clear();
        Projectiles.Clear();
        Pickups.Clear();
        Texts.Clear();

        Room start = Map.Rooms[Map.StartRoom];
        Player.Pos = DungeonMap.TileCenter(start.CX, start.CY);
        Player.DashTimer = 0f;

        SpawnMonsters();
        SpawnLoot();

        lastTx = lastTy = -1;
        Point t = DungeonMap.TileOf(Player.Pos);
        Map.ComputeFlow(t.X, t.Y);
        Map.UpdateVisibility(Player.Pos, VisionRadius);
        UpdateCamera(0f, snap: true);

        AddLog(BossFloor ? $"Floor {Floor}: something ancient waits below..." : $"Floor {Floor}", Color.White);
    }

    private MonsterType PickType()
    {
        var pool = new List<MonsterType> { MonsterType.Slime, MonsterType.Bat };
        if (Floor >= 2) { pool.Add(MonsterType.Skeleton); pool.Add(MonsterType.Skeleton); }
        if (Floor >= 3) { pool.Add(MonsterType.Archer); pool.Add(MonsterType.Archer); }
        if (Floor >= 4) pool.Add(MonsterType.Ogre);
        if (Floor >= 6) pool.Add(MonsterType.Ogre);
        return pool[rng.Next(pool.Count)];
    }

    private void SpawnMonsters()
    {
        for (int i = 0; i < Map.Rooms.Count; i++)
        {
            if (i == Map.StartRoom) continue;
            Room room = Map.Rooms[i];

            if (BossFloor && i == Map.StairsRoom)
            {
                Vector2 pos = DungeonMap.TileCenter(room.CX + 2, room.CY);
                Boss = MonsterFactory.Create(MonsterType.BoneKing, pos, Floor / 4 * 3 + 1, rng);
                Monsters.Add(Boss);
                continue;
            }

            int area = room.W * room.H;
            int count = Math.Clamp(rng.Next(2, 4) + Floor / 3 + area / 45, 2, 8);
            for (int k = 0; k < count; k++)
            {
                Vector2 pos = Map.RandomPointInRoom(room, rng);
                if (Vector2.Distance(pos, Player.Pos) < 200f) continue;
                Monsters.Add(MonsterFactory.Create(PickType(), pos, Floor, rng));
            }
        }
    }

    private void SpawnLoot()
    {
        int chests = Math.Min(3, 1 + Floor / 3);
        for (int i = 0; i < chests; i++)
        {
            Room room = RandomNonStartRoom();
            Pickups.Add(new Pickup { Kind = PickupKind.Chest, Pos = Map.RandomPointInRoom(room, rng) });
        }

        int potions = 1 + rng.Next(2);
        for (int i = 0; i < potions; i++)
        {
            Room room = RandomNonStartRoom();
            var kind = rng.Next(3) == 0 ? PickupKind.ManaPotion : PickupKind.HealthPotion;
            Pickups.Add(new Pickup { Kind = kind, Pos = Map.RandomPointInRoom(room, rng) });
        }
    }

    private Room RandomNonStartRoom()
    {
        int i = rng.Next(1, Map.Rooms.Count);
        if (i == Map.StartRoom) i = (i + 1) % Map.Rooms.Count;
        return Map.Rooms[i];
    }

    // ---------------------------------------------------------------- update

    public void Update(float dt)
    {
        Time += dt;

        if (Phase == Phase.Playing)
        {
            PlayTime += dt;
            UpdatePlayer(dt);
            if (Phase == Phase.Playing) UpdateMonsters(dt);
            if (Phase == Phase.Playing) UpdateProjectiles(dt);
            if (Phase == Phase.Playing) UpdatePickups(dt);
        }

        if (Phase != Phase.Paused && Phase != Phase.Title) UpdateEffects(dt);
        if (Phase != Phase.Title) UpdateCamera(dt, snap: false);
    }

    public Vector2 ScreenToWorld(Vector2 s) =>
        Cam + (s - new Vector2(ViewW * 0.5f, ViewH * 0.5f)) / Zoom;

    private void UpdateCamera(float dt, bool snap)
    {
        float halfW = ViewW / (2f * Zoom);
        float halfH = ViewH / (2f * Zoom);
        Vector2 target = Player.Pos;
        Cam = snap ? target : Vector2.Lerp(Cam, target, 1f - MathF.Exp(-9f * dt));

        float mapW = DungeonMap.Width * DungeonMap.TileSize;
        float mapH = DungeonMap.Height * DungeonMap.TileSize;
        Cam.X = mapW <= halfW * 2f ? mapW / 2f : Math.Clamp(Cam.X, halfW, mapW - halfW);
        Cam.Y = mapH <= halfH * 2f ? mapH / 2f : Math.Clamp(Cam.Y, halfH, mapH - halfH);
    }

    private void UpdateEffects(float dt)
    {
        Fx.Update(dt);
        Shake = MathF.Max(0f, Shake - dt * 2.5f);

        foreach (var t in Texts)
        {
            t.Life -= dt;
            t.Pos.Y -= 24f * dt;
        }
        Texts.RemoveAll(t => t.Life <= 0f);

        foreach (var l in Log) l.Age += dt;
        Log.RemoveAll(l => l.Age > 7f);
    }

    // ---------------------------------------------------------------- player

    private void UpdatePlayer(float dt)
    {
        var p = Player;
        var inp = Input;

        Vector2 toAim = ScreenToWorld(inp.AimScreen) - p.Pos;
        if (toAim.LengthSquared() > 4f) p.Facing = MathUtil.AngleOf(toAim);

        p.MeleeCd = MathF.Max(0f, p.MeleeCd - dt);
        p.FireCd = MathF.Max(0f, p.FireCd - dt);
        p.DashCd = MathF.Max(0f, p.DashCd - dt);
        p.Invuln = MathF.Max(0f, p.Invuln - dt);
        p.HitFlash = MathF.Max(0f, p.HitFlash - dt);
        p.SwingTimer = MathF.Max(0f, p.SwingTimer - dt);
        p.Mana = MathF.Min(p.MaxMana, p.Mana + p.ManaRegen * dt);

        Vector2 move = MathUtil.SafeNormalize(inp.Move);

        if (inp.Dash)
        {
            inp.Dash = false;
            if (p.DashCd <= 0f && p.DashTimer <= 0f)
            {
                p.DashDir = move != Vector2.Zero ? move : MathUtil.FromAngle(p.Facing);
                p.DashTimer = 0.16f;
                p.DashCd = 1.3f * p.DashCdMul;
                p.Invuln = MathF.Max(p.Invuln, 0.22f);
                Burst(p.Pos, 8, Color.FromArgb(170, 200, 220, 255), 90f, 0.3f, 5f);
            }
        }

        Vector2 vel;
        if (p.DashTimer > 0f)
        {
            p.DashTimer -= dt;
            vel = p.DashDir * 640f;
            Fx.Emit(p.Pos, Vector2.Zero, 0.25f, 9f, 1f, Color.FromArgb(120, 190, 210, 255), 1f);
        }
        else
        {
            vel = move * p.MoveSpeed;
            if (move != Vector2.Zero) p.WalkAnim += dt * 10f;
        }
        Map.Move(ref p.Pos, vel * dt, p.Radius);

        if (inp.Melee && p.MeleeCd <= 0f && p.DashTimer <= 0f) Melee();
        if (inp.Fire && p.FireCd <= 0f && p.DashTimer <= 0f) CastFireball();

        if (inp.UseHealth)
        {
            inp.UseHealth = false;
            if (p.HealthPotions > 0 && p.Hp < p.MaxHp)
            {
                p.HealthPotions--;
                float heal = p.MaxHp * 0.45f;
                p.Hp = MathF.Min(p.MaxHp, p.Hp + heal);
                AddText(p.Pos + new Vector2(0f, -16f), $"+{(int)heal}", Color.FromArgb(255, 110, 110), 16f);
                Burst(p.Pos, 10, Color.FromArgb(220, 255, 90, 90), 80f, 0.5f, 4f);
            }
        }
        if (inp.UseMana)
        {
            inp.UseMana = false;
            if (p.ManaPotions > 0 && p.Mana < p.MaxMana)
            {
                p.ManaPotions--;
                p.Mana = MathF.Min(p.MaxMana, p.Mana + p.MaxMana * 0.6f);
                AddText(p.Pos + new Vector2(0f, -16f), "+mana", Color.FromArgb(120, 170, 255), 16f);
                Burst(p.Pos, 10, Color.FromArgb(220, 90, 140, 255), 80f, 0.5f, 4f);
            }
        }
        if (inp.Interact)
        {
            inp.Interact = false;
            TryInteract();
        }

        // Keep the flow field and fog of war current.
        Point tile = DungeonMap.TileOf(p.Pos);
        if (tile.X != lastTx || tile.Y != lastTy)
        {
            lastTx = tile.X;
            lastTy = tile.Y;
            Map.ComputeFlow(tile.X, tile.Y);
        }
        Map.UpdateVisibility(p.Pos, VisionRadius);
    }

    private float RollDamage(float baseDmg, bool canCrit, out bool crit)
    {
        crit = canCrit && rng.NextSingle() < Player.CritChance;
        float d = baseDmg * (0.9f + rng.NextSingle() * 0.2f);
        if (crit) d *= 2f;
        return MathF.Max(1f, MathF.Round(d));
    }

    private void Melee()
    {
        var p = Player;
        p.MeleeCd = 0.38f * p.MeleeCdMul;
        p.SwingTimer = 0.16f;

        const float range = 58f;
        const float halfArc = 0.98f;
        bool hitAny = false;

        foreach (var m in Monsters)
        {
            if (m.Dead) continue;
            Vector2 d = m.Pos - p.Pos;
            float dist = d.Length();
            if (dist > range + m.Radius) continue;

            float ang = MathF.Abs(MathUtil.NormalizeAngle(MathUtil.AngleOf(d) - p.Facing));
            if (ang > halfArc && dist > m.Radius + 14f) continue;
            if (!Map.HasLineOfSight(p.Pos, m.Pos)) continue;

            float dmg = RollDamage(p.MeleeDamage, true, out bool crit);
            DamageMonster(m, dmg, crit, MathUtil.SafeNormalize(d), 300f);
            hitAny = true;
        }

        Map.Move(ref p.Pos, MathUtil.FromAngle(p.Facing) * 7f, p.Radius);   // little lunge
        if (hitAny) Shake = MathF.Min(1f, Shake + 0.08f);
    }

    private void CastFireball()
    {
        var p = Player;
        const float cost = 18f;
        if (p.Mana < cost) return;

        p.Mana -= cost;
        p.FireCd = 0.45f;
        Vector2 dir = MathUtil.FromAngle(p.Facing);
        Projectiles.Add(new Projectile
        {
            Kind = ProjKind.Fireball,
            Pos = p.Pos + dir * 14f,
            Vel = dir * 520f,
            Radius = 7f,
            Damage = p.FireDamage,
            FromPlayer = true,
            Life = 1.6f,
        });
    }

    private void TryInteract()
    {
        Point t = DungeonMap.TileOf(Player.Pos);
        if (Map.Tiles[t.X, t.Y] != Tile.Stairs) return;

        if (StairsOpen)
        {
            StartFloor(Floor + 1);
            Player.Hp = MathF.Min(Player.MaxHp, Player.Hp + Player.MaxHp * 0.25f);
            Player.Mana = Player.MaxMana;
        }
        else
        {
            AddLog("The stairs are sealed. Defeat the Bone King!", Color.OrangeRed);
        }
    }

    public void HurtPlayer(float dmg)
    {
        var p = Player;
        if (p.Invuln > 0f || Phase != Phase.Playing) return;

        float reduce = p.ArmorValue / (p.ArmorValue + 25f);
        int actual = (int)MathF.Max(1f, MathF.Round(dmg * (1f - reduce)));
        p.Hp -= actual;
        p.Invuln = 0.5f;
        p.HitFlash = 0.15f;
        Shake = MathF.Min(1f, Shake + 0.3f);
        AddText(p.Pos + new Vector2(0f, -18f), $"-{actual}", Color.FromArgb(255, 80, 80), 18f);
        Burst(p.Pos, 8, Color.FromArgb(230, 200, 40, 40), 140f, 0.4f, 4f);

        if (p.Hp <= 0f)
        {
            p.Hp = 0f;
            Burst(p.Pos, 40, Color.FromArgb(230, 200, 40, 40), 240f, 0.8f, 6f);
            Phase = Phase.Dead;
        }
    }

    // --------------------------------------------------------------- monsters

    private void DamageMonster(Monster m, float dmg, bool crit, Vector2 dir, float force)
    {
        if (m.Dead) return;
        m.Hp -= dmg;
        m.HitFlash = 0.1f;
        m.Alerted = true;

        float resist = m.Type == MonsterType.BoneKing ? 0.1f : m.Type == MonsterType.Ogre ? 0.45f : 1f;
        m.Knock += dir * force * resist;

        AddText(m.Pos + new Vector2(0f, -m.Radius), ((int)dmg).ToString(),
            crit ? Color.Gold : Color.White, crit ? 20f : 14f);
        Burst(m.Pos, crit ? 8 : 4, Color.FromArgb(220, 190, 40, 40), 140f, 0.35f, 4f);

        if (Player.LifeSteal > 0f)
            Player.Hp = MathF.Min(Player.MaxHp, Player.Hp + dmg * Player.LifeSteal);

        if (m.Hp <= 0f) KillMonster(m);
    }

    private void KillMonster(Monster m)
    {
        m.Dead = true;
        Player.Kills++;
        Burst(m.Pos, m.IsBoss ? 60 : 14, Color.FromArgb(230, 200, 60, 60), m.IsBoss ? 320f : 180f, 0.6f, 5f);
        Shake = MathF.Min(1f, Shake + (m.IsBoss ? 0.8f : 0.1f));

        int gold = rng.Next(1, m.GoldMax + 1) + Floor * 2;
        if (m.IsBoss)
        {
            for (int i = 0; i < 8; i++) SpawnGold(m.Pos, gold / 2 + 5);
        }
        else if (rng.NextSingle() < 0.75f)
        {
            SpawnGold(m.Pos, gold);
        }

        if (rng.NextSingle() < (m.IsBoss ? 1f : 0.08f))
            Pickups.Add(new Pickup { Kind = PickupKind.HealthPotion, Pos = m.Pos, Vel = ScatterVel() });
        if (rng.NextSingle() < (m.IsBoss ? 1f : 0.05f))
            Pickups.Add(new Pickup { Kind = PickupKind.ManaPotion, Pos = m.Pos, Vel = ScatterVel() });

        if (m.IsBoss || rng.NextSingle() < 0.06f)
        {
            Item it = ItemFactory.Create(rng, Floor, minRarity: m.IsBoss ? Rarity.Rare : null);
            Pickups.Add(new Pickup { Kind = PickupKind.Loot, Pos = m.Pos, Item = it, Vel = ScatterVel() });
        }

        if (m.IsBoss) AddLog("The Bone King falls! The stairs unseal.", Color.Gold);
        GainXp(m.Xp);
    }

    private Vector2 ScatterVel() => MathUtil.FromAngle(rng.NextSingle() * MathF.Tau) * rng.Between(40f, 120f);

    private void SpawnGold(Vector2 pos, int amount)
    {
        Pickups.Add(new Pickup { Kind = PickupKind.Gold, Pos = pos, Amount = Math.Max(1, amount), Vel = ScatterVel() });
    }

    private void UpdateMonsters(float dt)
    {
        var p = Player;

        for (int i = 0; i < Monsters.Count; i++)
        {
            Monster m = Monsters[i];
            if (m.Dead) continue;

            m.HitFlash = MathF.Max(0f, m.HitFlash - dt);
            m.AttackCd = MathF.Max(0f, m.AttackCd - dt);
            m.Anim += dt;

            if (m.Knock.LengthSquared() > 4f)
            {
                Map.Move(ref m.Pos, m.Knock * dt, m.Radius);
                m.Knock *= MathF.Max(0f, 1f - 9f * dt);
            }
            else
            {
                m.Knock = Vector2.Zero;
            }

            Vector2 to = p.Pos - m.Pos;
            float dist = to.Length();
            bool los = dist < 360f && Map.HasLineOfSight(m.Pos, p.Pos);

            if (!m.Alerted)
            {
                if (los && dist < 240f) m.Alerted = true;
                else continue;
            }
            if (dist > 0.01f) m.Facing = MathUtil.AngleOf(to);

            switch (m.Type)
            {
                case MonsterType.Bat: BatAi(m, dt, to, dist, los); break;
                case MonsterType.Archer: ArcherAi(m, dt, to, dist, los); break;
                case MonsterType.BoneKing: BossAi(m, dt, to, dist, los); break;
                default: MeleeAi(m, dt, to, dist, los); break;
            }

            if (Phase != Phase.Playing) break;
        }

        SeparateMonsters();
        Monsters.RemoveAll(x => x.Dead);
    }

    private void SeparateMonsters()
    {
        for (int i = 0; i < Monsters.Count; i++)
        {
            Monster a = Monsters[i];
            if (a.Dead) continue;
            for (int j = i + 1; j < Monsters.Count; j++)
            {
                Monster b = Monsters[j];
                if (b.Dead) continue;
                Vector2 d = b.Pos - a.Pos;
                float dist = d.Length();
                float min = a.Radius + b.Radius;
                if (dist >= min || dist < 0.01f) continue;
                Vector2 n = d / dist;
                float push = (min - dist) * 0.5f;
                Map.Move(ref a.Pos, -n * push, a.Radius);
                Map.Move(ref b.Pos, n * push, b.Radius);
            }
        }
    }

    /// <summary>Walk toward the player: straight when close and visible, otherwise downhill on the flow field.</summary>
    private void ChaseMove(Monster m, float dt, Vector2 to, float dist, bool los, float speedMul = 1f)
    {
        Vector2 dir;
        if (los && dist < 110f)
        {
            dir = MathUtil.SafeNormalize(to);
        }
        else
        {
            Vector2 target = Map.FlowTarget(m.Pos, out bool found);
            dir = found ? MathUtil.SafeNormalize(target - m.Pos) : MathUtil.SafeNormalize(to);
        }
        Map.Move(ref m.Pos, dir * m.Speed * speedMul * dt, m.Radius);
    }

    private void MeleeAi(Monster m, float dt, Vector2 to, float dist, bool los)
    {
        var p = Player;
        float reach = m.Radius + p.Radius + 10f;

        if (m.State == AiState.Windup)
        {
            m.StateTimer -= dt;
            if (m.StateTimer <= 0f)
            {
                if (dist < reach + 14f) HurtPlayer(m.Damage);
                if (m.Type == MonsterType.Ogre)
                {
                    Burst(m.Pos + MathUtil.FromAngle(m.Facing) * 20f, 14, Color.FromArgb(200, 150, 130, 100), 160f, 0.4f, 5f);
                    Shake = MathF.Min(1f, Shake + 0.25f);
                }
                m.AttackCd = m.Type == MonsterType.Ogre ? 1.3f : 0.9f;
                m.State = AiState.Chase;
            }
            return;
        }

        if (dist < reach && m.AttackCd <= 0f)
        {
            m.State = AiState.Windup;
            m.StateTimer = m.Type == MonsterType.Ogre ? 0.55f : 0.35f;
            return;
        }
        ChaseMove(m, dt, to, dist, los);
    }

    private void BatAi(Monster m, float dt, Vector2 to, float dist, bool los)
    {
        var p = Player;
        if (!los || dist > 200f)
        {
            ChaseMove(m, dt, to, dist, los);
        }
        else
        {
            Vector2 dir = MathUtil.SafeNormalize(to);
            Vector2 perp = new(-dir.Y, dir.X);
            float wobble = MathF.Sin(m.Anim * 7f);
            Vector2 mv = MathUtil.SafeNormalize(dir * 0.8f + perp * wobble * 0.9f);
            Map.Move(ref m.Pos, mv * m.Speed * dt, m.Radius);
        }

        if (dist < m.Radius + p.Radius + 4f && m.AttackCd <= 0f)
        {
            HurtPlayer(m.Damage);
            m.AttackCd = 0.9f;
            m.Knock = -MathUtil.SafeNormalize(to) * 220f;
        }
    }

    private void ArcherAi(Monster m, float dt, Vector2 to, float dist, bool los)
    {
        if (m.State == AiState.Windup)
        {
            m.StateTimer -= dt;
            if (m.StateTimer <= 0f)
            {
                Vector2 dir = MathUtil.SafeNormalize(to);
                Projectiles.Add(new Projectile
                {
                    Kind = ProjKind.Arrow,
                    Pos = m.Pos + dir * 12f,
                    Vel = dir * 300f,
                    Radius = 4f,
                    Damage = m.Damage,
                    FromPlayer = false,
                    Life = 2.5f,
                });
                m.AttackCd = 1.7f;
                m.State = AiState.Chase;
            }
            return;
        }

        if (los && dist < 400f && m.AttackCd <= 0f)
        {
            m.State = AiState.Windup;
            m.StateTimer = 0.4f;
            return;
        }

        if (los && dist < 130f)
        {
            Vector2 away = -MathUtil.SafeNormalize(to);
            Map.Move(ref m.Pos, away * m.Speed * dt, m.Radius);        // back off while kiting
        }
        else if (!los || dist > 260f)
        {
            ChaseMove(m, dt, to, dist, los);
        }
    }

    private void BossAi(Monster m, float dt, Vector2 to, float dist, bool los)
    {
        var p = Player;
        bool enraged = m.Hp < m.MaxHp * 0.5f;

        switch (m.State)
        {
            case AiState.Chase:
                ChaseMove(m, dt, to, dist, los, enraged ? 1.3f : 1f);
                if (dist < m.Radius + p.Radius + 14f && m.AttackCd <= 0f)
                {
                    HurtPlayer(m.Damage);
                    m.AttackCd = 0.9f;
                }
                m.StateTimer -= dt;
                if (m.StateTimer <= 0f)
                {
                    m.Cycle++;
                    int a = m.Cycle % 3;
                    if (a == 0)
                    {
                        m.State = AiState.Windup;
                        m.StateTimer = 0.7f;
                        m.ChargeDir = MathUtil.SafeNormalize(to);
                    }
                    else if (a == 1)
                    {
                        FireRing(m, enraged ? 20 : 14);
                        m.State = AiState.Recover;
                        m.StateTimer = 0.8f;
                    }
                    else
                    {
                        Summon(m);
                        m.State = AiState.Recover;
                        m.StateTimer = 1.0f;
                    }
                }
                break;

            case AiState.Windup:
                m.ChargeDir = MathUtil.SafeNormalize(to);
                m.StateTimer -= dt;
                if (m.StateTimer <= 0f)
                {
                    m.State = AiState.Charge;
                    m.StateTimer = 0.7f;
                }
                break;

            case AiState.Charge:
                Map.Move(ref m.Pos, m.ChargeDir * 430f * dt, m.Radius);
                if (dist < m.Radius + p.Radius + 6f && m.AttackCd <= 0f)
                {
                    HurtPlayer(m.Damage * 1.4f);
                    m.AttackCd = 0.8f;
                }
                m.StateTimer -= dt;
                if (m.StateTimer <= 0f)
                {
                    m.State = AiState.Recover;
                    m.StateTimer = 0.6f;
                    Shake = MathF.Min(1f, Shake + 0.3f);
                }
                break;

            default:    // Recover
                m.StateTimer -= dt;
                if (m.StateTimer <= 0f)
                {
                    m.State = AiState.Chase;
                    m.StateTimer = enraged ? 2.0f : 3.0f;
                }
                break;
        }
    }

    private void FireRing(Monster m, int count)
    {
        float offset = rng.NextSingle() * MathF.Tau;
        for (int i = 0; i < count; i++)
        {
            Vector2 dir = MathUtil.FromAngle(offset + MathF.Tau * i / count);
            Projectiles.Add(new Projectile
            {
                Kind = ProjKind.Orb,
                Pos = m.Pos + dir * (m.Radius + 4f),
                Vel = dir * 190f,
                Radius = 8f,
                Damage = m.Damage * 0.6f,
                FromPlayer = false,
                Life = 4f,
            });
        }
        Burst(m.Pos, 20, Color.FromArgb(220, 190, 90, 240), 200f, 0.5f, 6f);
    }

    private void Summon(Monster m)
    {
        if (Monsters.Count > 14) return;
        for (int i = 0; i < 2; i++)
        {
            Vector2 pos = m.Pos + MathUtil.FromAngle(rng.NextSingle() * MathF.Tau) * 50f;
            if (Map.CircleBlocked(pos, 12f)) pos = m.Pos;
            var type = rng.Next(2) == 0 ? MonsterType.Skeleton : MonsterType.Bat;
            var minion = MonsterFactory.Create(type, pos, Floor, rng);
            minion.Alerted = true;
            Monsters.Add(minion);
            Burst(pos, 10, Color.FromArgb(220, 150, 90, 220), 120f, 0.5f, 5f);
        }
        AddLog("The Bone King summons minions!", Color.FromArgb(200, 140, 255));
    }

    // ------------------------------------------------------------ projectiles

    private void UpdateProjectiles(float dt)
    {
        for (int i = 0; i < Projectiles.Count; i++)
        {
            Projectile pr = Projectiles[i];
            if (pr.Dead) continue;

            float travel = pr.Vel.Length() * dt;
            int steps = Math.Max(1, (int)MathF.Ceiling(travel / 8f));
            Vector2 step = pr.Vel * dt / steps;

            for (int s = 0; s < steps && !pr.Dead; s++)
            {
                pr.Pos += step;

                if (Map.IsSolidAt(pr.Pos))
                {
                    Impact(pr);
                    break;
                }

                if (pr.FromPlayer)
                {
                    foreach (var m in Monsters)
                    {
                        if (m.Dead) continue;
                        float rr = m.Radius + pr.Radius;
                        if (Vector2.DistanceSquared(m.Pos, pr.Pos) < rr * rr)
                        {
                            Impact(pr);
                            break;
                        }
                    }
                }
                else
                {
                    float rr = Player.Radius + pr.Radius;
                    if (Vector2.DistanceSquared(Player.Pos, pr.Pos) < rr * rr)
                    {
                        HurtPlayer(pr.Damage);
                        pr.Dead = true;
                    }
                }
            }

            if (pr.Dead) continue;

            pr.Life -= dt;
            if (pr.Life <= 0f)
            {
                Impact(pr);
                continue;
            }

            if (pr.Kind == ProjKind.Fireball)
                Fx.Emit(pr.Pos, MathUtil.FromAngle(rng.NextSingle() * MathF.Tau) * 30f, 0.3f, 9f, 1f, Color.FromArgb(200, 255, 150, 40), 2f);
            else if (pr.Kind == ProjKind.Orb)
                Fx.Emit(pr.Pos, Vector2.Zero, 0.25f, 9f, 1f, Color.FromArgb(150, 190, 90, 240), 2f);
        }
        Projectiles.RemoveAll(x => x.Dead);
    }

    private void Impact(Projectile pr)
    {
        pr.Dead = true;
        if (pr.Kind == ProjKind.Fireball) Explode(pr.Pos, pr.Damage);
        else Burst(pr.Pos, 5, pr.Kind == ProjKind.Orb ? Color.FromArgb(200, 190, 90, 240) : Color.FromArgb(200, 200, 170, 110), 90f, 0.25f, 3f);
    }

    private void Explode(Vector2 pos, float dmg)
    {
        Burst(pos, 26, Color.FromArgb(230, 255, 140, 30), 260f, 0.5f, 7f);
        Burst(pos, 10, Color.FromArgb(230, 255, 230, 120), 160f, 0.35f, 5f);
        Shake = MathF.Min(1f, Shake + 0.2f);

        foreach (var m in Monsters)
        {
            if (m.Dead) continue;
            float d = Vector2.Distance(m.Pos, pos);
            if (d > ExplosionRadius + m.Radius) continue;
            float falloff = 1f - 0.5f * d / (ExplosionRadius + m.Radius);
            float amount = RollDamage(dmg * falloff, true, out bool crit);
            DamageMonster(m, amount, crit, MathUtil.SafeNormalize(m.Pos - pos), 260f);
        }
    }

    // ---------------------------------------------------------------- pickups

    private void UpdatePickups(float dt)
    {
        var p = Player;
        for (int i = 0; i < Pickups.Count; i++)   // index loop: opening a chest appends new pickups
        {
            Pickup pk = Pickups[i];
            if (pk.Taken) continue;
            pk.Bob += dt;

            if (pk.Vel.LengthSquared() > 1f)
            {
                Vector2 next = pk.Pos + pk.Vel * dt;
                if (Map.IsSolidAt(next)) pk.Vel = Vector2.Zero;
                else pk.Pos = next;
                pk.Vel *= MathF.Max(0f, 1f - 6f * dt);
            }

            Vector2 d = p.Pos - pk.Pos;
            float dist = d.Length();

            switch (pk.Kind)
            {
                case PickupKind.Gold:
                    if (dist < 110f && dist > 1f) pk.Pos += d / dist * 320f * dt;
                    if (dist < p.Radius + 8f)
                    {
                        p.Gold += pk.Amount;
                        pk.Taken = true;
                        AddText(p.Pos + new Vector2(0f, -20f), $"+{pk.Amount}g", Color.Gold, 13f);
                    }
                    break;

                case PickupKind.HealthPotion:
                    if (dist < p.Radius + 14f)
                    {
                        p.HealthPotions++;
                        pk.Taken = true;
                        AddLog("Picked up a health potion (Q)", Color.FromArgb(255, 120, 120));
                    }
                    break;

                case PickupKind.ManaPotion:
                    if (dist < p.Radius + 14f)
                    {
                        p.ManaPotions++;
                        pk.Taken = true;
                        AddLog("Picked up a mana potion (F)", Color.FromArgb(130, 170, 255));
                    }
                    break;

                case PickupKind.Loot:
                    if (dist < p.Radius + 14f && pk.Item != null)
                    {
                        pk.Taken = true;
                        ProcessItem(pk.Item);
                    }
                    break;

                case PickupKind.Chest:
                    if (dist < p.Radius + 18f)
                    {
                        pk.Taken = true;
                        OpenChest(pk);
                    }
                    break;
            }
        }
        Pickups.RemoveAll(x => x.Taken);
    }

    private void OpenChest(Pickup chest)
    {
        Burst(chest.Pos, 18, Color.FromArgb(230, 240, 200, 70), 160f, 0.6f, 5f);
        int piles = rng.Next(3, 7);
        for (int i = 0; i < piles; i++) SpawnGold(chest.Pos, rng.Next(4, 10) + Floor * 3);

        if (rng.NextSingle() < 0.35f)
            Pickups.Add(new Pickup { Kind = PickupKind.HealthPotion, Pos = chest.Pos, Vel = ScatterVel() });
        if (rng.NextSingle() < 0.20f)
            Pickups.Add(new Pickup { Kind = PickupKind.ManaPotion, Pos = chest.Pos, Vel = ScatterVel() });
        if (rng.NextSingle() < 0.75f)
        {
            Item it = ItemFactory.Create(rng, Floor, minRarity: Rarity.Uncommon);
            Pickups.Add(new Pickup { Kind = PickupKind.Loot, Pos = chest.Pos, Item = it, Vel = ScatterVel() });
        }
        AddLog("Chest opened!", Color.Gold);
    }

    /// <summary>Equips an item if it beats the current one, otherwise sells it for gold.</summary>
    private void ProcessItem(Item it)
    {
        var p = Player;
        Item? current = it.Slot switch
        {
            Slot.Weapon => p.Weapon,
            Slot.Armor => p.Armor,
            _ => p.Charm,
        };

        if (current == null || it.Rating > current.Rating)
        {
            if (current != null)
            {
                int value = (int)(current.Rating * 2f) + 4;
                p.Gold += value;
            }
            switch (it.Slot)
            {
                case Slot.Weapon: p.Weapon = it; break;
                case Slot.Armor: p.Armor = it; break;
                default: p.Charm = it; break;
            }
            p.Hp = MathF.Min(p.Hp, p.MaxHp);
            AddLog($"Equipped {it.Name} ({it.Describe()})", it.Color);
            Burst(p.Pos, 14, it.Color, 120f, 0.6f, 5f);
        }
        else
        {
            int value = (int)(it.Rating * 2f) + 4;
            p.Gold += value;
            AddLog($"Sold {it.Name} for {value} gold", Color.FromArgb(180, 180, 190));
        }
    }

    // ------------------------------------------------------------- levelling

    private void GainXp(int amount)
    {
        var p = Player;
        p.Xp += amount;
        while (p.Xp >= p.XpToNext)
        {
            p.Xp -= p.XpToNext;
            p.Level++;
            p.Hp = MathF.Min(p.MaxHp, p.Hp + p.MaxHp * 0.3f);
            p.Mana = p.MaxMana;
            PendingLevelUps++;
            Burst(p.Pos, 30, Color.FromArgb(230, 255, 230, 100), 220f, 0.8f, 6f);
            AddText(p.Pos + new Vector2(0f, -24f), "LEVEL UP!", Color.Gold, 22f);
        }

        if (PendingLevelUps > 0 && Phase == Phase.Playing)
        {
            RollPerks();
            Phase = Phase.LevelUp;
        }
    }

    private void RollPerks()
    {
        PerkChoices.Clear();
        var pool = new List<Perk>(Perks.All);
        for (int i = 0; i < 3 && pool.Count > 0; i++)
        {
            int k = rng.Next(pool.Count);
            PerkChoices.Add(pool[k]);
            pool.RemoveAt(k);
        }
    }

    public void ChoosePerk(int index)
    {
        if (Phase != Phase.LevelUp || index < 0 || index >= PerkChoices.Count) return;

        PerkChoices[index].Apply(Player);
        Player.Hp = MathF.Min(Player.Hp, Player.MaxHp);
        PendingLevelUps--;

        if (PendingLevelUps > 0) RollPerks();
        else Phase = Phase.Playing;
    }

    // ---------------------------------------------------------------- helpers

    private void Burst(Vector2 pos, int count, Color color, float speed, float life, float size)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 dir = MathUtil.FromAngle(rng.NextSingle() * MathF.Tau);
            float spd = speed * (0.3f + rng.NextSingle() * 0.7f);
            Fx.Emit(pos, dir * spd, life * (0.6f + rng.NextSingle() * 0.8f), size, 0.5f, color, 3f);
        }
    }

    private void AddText(Vector2 pos, string text, Color color, float size)
    {
        Texts.Add(new FloatText { Pos = pos, Text = text, Color = color, Life = 0.9f, MaxLife = 0.9f, Size = size });
    }

    private void AddLog(string text, Color color)
    {
        Log.Add(new LogLine { Text = text, Color = color });
        while (Log.Count > 6) Log.RemoveAt(0);
    }
}
