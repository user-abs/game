using System.Numerics;

namespace TankBattle;

public abstract class Tank
{
    public Vector2 Position;
    public float BodyAngle;
    public float TurretAngle;
    public float Health;
    public float MaxHealth;
    public float Speed;
    public float Radius = 20f;
    public float ReloadTime;
    public float ReloadTimer;
    public float HitFlash;

    public bool IsAlive => Health > 0;

    protected Tank(Vector2 position)
    {
        Position = position;
    }
}

public sealed class PlayerTank : Tank
{
    public PlayerTank(Vector2 position) : base(position)
    {
        MaxHealth = Health = 100f;
        Speed = 190f;
        ReloadTime = 0.45f;
    }
}

public sealed class EnemyTank : Tank
{
    public float StrafeDir = 1f;
    public float StrafeTimer;
    public float AvoidTimer;

    public EnemyTank(Vector2 position, int wave, Random rng) : base(position)
    {
        MaxHealth = Health = 60f;
        Speed = Math.Min(80f + wave * 6f, 150f);
        ReloadTime = Math.Max(2.2f - wave * 0.08f, 1.2f);
        ReloadTimer = 1f + rng.NextSingle();           // don't fire the instant we spawn
        StrafeDir = rng.Next(2) == 0 ? -1f : 1f;
        StrafeTimer = 1f + rng.NextSingle() * 2f;
    }
}

public sealed class Bullet
{
    public Vector2 Position;
    public Vector2 Velocity;
    public bool FromPlayer;
    public float Life = 2f;
    public const float Radius = 4f;

    public Bullet(Vector2 position, Vector2 velocity, bool fromPlayer)
    {
        Position = position;
        Velocity = velocity;
        FromPlayer = fromPlayer;
    }
}

public sealed class Obstacle
{
    public RectangleF Bounds;
    public bool Destructible;
    public int Hp;
    public int MaxHp;

    public Obstacle(RectangleF bounds, bool destructible)
    {
        Bounds = bounds;
        Destructible = destructible;
        Hp = MaxHp = destructible ? 3 : int.MaxValue;
    }
}

public sealed class Particle
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Life;
    public float MaxLife;
    public float Size;
    public Color Color;
}
