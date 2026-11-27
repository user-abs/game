using System.Numerics;

namespace DungeonDelve;

public sealed class Player
{
    public Vector2 Pos;
    public float Facing;
    public float Radius = 11f;

    public float Hp = 100f;
    public float Mana = 60f;
    public int Level = 1;
    public int Xp;
    public int Gold;
    public int Kills;
    public int HealthPotions = 2;
    public int ManaPotions = 1;

    public Item? Weapon;
    public Item? Armor;
    public Item? Charm;

    // Modifiers granted by perks.
    public float DmgMul = 1f;
    public float SpeedBonus;
    public float MeleeCdMul = 1f;
    public float DashCdMul = 1f;
    public float LifeSteal;
    public float PerkCrit;
    public float FireMul = 1f;
    public float PerkManaRegen;
    public int PerkHp;
    public int PerkMana;
    public int PerkArmor;

    // Timers.
    public float MeleeCd;
    public float FireCd;
    public float DashCd;
    public float DashTimer;
    public float Invuln;
    public float HitFlash;
    public float SwingTimer;
    public float WalkAnim;
    public Vector2 DashDir;

    // Derived stats.
    public int MaxHp => 100 + (Level - 1) * 12 + PerkHp + (Charm?.Hp ?? 0);
    public float MaxMana => 60f + (Level - 1) * 4f + PerkMana;
    public int XpToNext => 40 + Level * 30;
    public float MeleeDamage => (10f + Level * 2f + (Weapon?.Power ?? 0)) * DmgMul;
    public float FireDamage => (18f + Level * 3f + (Weapon?.Power ?? 0) * 0.5f) * DmgMul * FireMul;
    public int ArmorValue => (Armor?.Power ?? 0) + PerkArmor;
    public float MoveSpeed => 175f * (1f + SpeedBonus + (Charm?.Speed ?? 0f));
    public float CritChance => 0.05f + PerkCrit + (Charm?.Crit ?? 0f);
    public float ManaRegen => 4f + PerkManaRegen + (Charm?.ManaRegen ?? 0f);
}

public enum MonsterType { Slime, Bat, Skeleton, Archer, Ogre, BoneKing }

public enum AiState { Chase, Windup, Charge, Recover }

public sealed class Monster
{
    public MonsterType Type;
    public Vector2 Pos;
    public float Hp;
    public float MaxHp;
    public float Radius;
    public float Speed;
    public float Damage;
    public int Xp;
    public int GoldMax;

    public bool Alerted;
    public bool Dead;
    public AiState State = AiState.Chase;
    public float StateTimer;
    public float AttackCd;
    public float HitFlash;
    public float Facing;
    public float Anim;
    public Vector2 Knock;
    public Vector2 ChargeDir;
    public int Cycle;

    public bool IsBoss => Type == MonsterType.BoneKing;
}

public static class MonsterFactory
{
    public static Monster Create(MonsterType type, Vector2 pos, int floor, Random rng)
    {
        var m = new Monster { Type = type, Pos = pos, Anim = rng.NextSingle() * 10f };

        switch (type)
        {
            case MonsterType.Slime:
                m.MaxHp = 32f; m.Speed = 62f; m.Damage = 9f; m.Radius = 12f; m.Xp = 8; m.GoldMax = 6;
                break;
            case MonsterType.Bat:
                m.MaxHp = 14f; m.Speed = 140f; m.Damage = 6f; m.Radius = 8f; m.Xp = 6; m.GoldMax = 4;
                break;
            case MonsterType.Skeleton:
                m.MaxHp = 44f; m.Speed = 85f; m.Damage = 13f; m.Radius = 11f; m.Xp = 14; m.GoldMax = 9;
                break;
            case MonsterType.Archer:
                m.MaxHp = 26f; m.Speed = 80f; m.Damage = 10f; m.Radius = 10f; m.Xp = 14; m.GoldMax = 9;
                break;
            case MonsterType.Ogre:
                m.MaxHp = 130f; m.Speed = 62f; m.Damage = 26f; m.Radius = 17f; m.Xp = 40; m.GoldMax = 20;
                break;
            default:
                m.MaxHp = 650f; m.Speed = 80f; m.Damage = 20f; m.Radius = 26f; m.Xp = 220; m.GoldMax = 60;
                m.StateTimer = 2.5f;
                break;
        }

        float hpScale = 1f + 0.2f * (floor - 1);
        float dmgScale = 1f + 0.12f * (floor - 1);
        m.MaxHp = MathF.Round(m.MaxHp * hpScale);
        m.Hp = m.MaxHp;
        m.Damage = MathF.Round(m.Damage * dmgScale);
        m.Xp = (int)(m.Xp * (1f + 0.1f * (floor - 1)));
        return m;
    }
}

public enum ProjKind { Fireball, Arrow, Orb }

public sealed class Projectile
{
    public ProjKind Kind;
    public Vector2 Pos;
    public Vector2 Vel;
    public float Radius;
    public float Damage;
    public bool FromPlayer;
    public float Life;
    public bool Dead;
}

public enum PickupKind { Gold, HealthPotion, ManaPotion, Loot, Chest }

public sealed class Pickup
{
    public PickupKind Kind;
    public Vector2 Pos;
    public Vector2 Vel;
    public int Amount;
    public Item? Item;
    public float Bob;
    public bool Taken;
}

public sealed class FloatText
{
    public Vector2 Pos;
    public string Text = "";
    public Color Color;
    public float Life;
    public float MaxLife;
    public float Size;
}

public sealed class LogLine
{
    public string Text = "";
    public Color Color;
    public float Age;
}

public struct Particle
{
    public bool Active;
    public Vector2 Pos;
    public Vector2 Vel;
    public float Life;
    public float MaxLife;
    public float Size0;
    public float Size1;
    public float Drag;
    public Color Color;
}

public sealed class ParticleSystem
{
    public readonly Particle[] Items = new Particle[1800];
    private int next;

    public void Emit(Vector2 pos, Vector2 vel, float life, float size0, float size1, Color color, float drag = 3f)
    {
        ref Particle p = ref Items[next];
        next = (next + 1) % Items.Length;
        p.Active = true;
        p.Pos = pos;
        p.Vel = vel;
        p.Life = life;
        p.MaxLife = life;
        p.Size0 = size0;
        p.Size1 = size1;
        p.Drag = drag;
        p.Color = color;
    }

    public void Update(float dt)
    {
        for (int i = 0; i < Items.Length; i++)
        {
            ref Particle p = ref Items[i];
            if (!p.Active) continue;
            p.Life -= dt;
            if (p.Life <= 0f) { p.Active = false; continue; }
            p.Pos += p.Vel * dt;
            p.Vel *= MathF.Max(0f, 1f - p.Drag * dt);
        }
    }
}
