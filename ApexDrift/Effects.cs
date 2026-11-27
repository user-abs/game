using System.Numerics;

namespace ApexDrift;

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

/// <summary>Fixed-size particle pool: smoke, dust, sparks and nitro flames.</summary>
public sealed class ParticleSystem
{
    public readonly Particle[] Items = new Particle[1600];
    private int next;

    public void Emit(Vector2 pos, Vector2 vel, float life, float size0, float size1, Color color, float drag = 2f)
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

public struct Skid
{
    public bool Active;
    public Vector2 A;
    public Vector2 B;
    public float Time;
}

/// <summary>Ring buffer of tyre mark segments; old ones are overwritten and fade with age.</summary>
public sealed class SkidMarks
{
    public readonly Skid[] Items = new Skid[6000];
    private int next;

    public void Add(Vector2 a, Vector2 b, float time)
    {
        ref Skid s = ref Items[next];
        next = (next + 1) % Items.Length;
        s.Active = true;
        s.A = a;
        s.B = b;
        s.Time = time;
    }
}
