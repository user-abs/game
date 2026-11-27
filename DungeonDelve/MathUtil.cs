using System.Numerics;

namespace DungeonDelve;

public static class MathUtil
{
    public static float NormalizeAngle(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    public static Vector2 FromAngle(float a) => new(MathF.Cos(a), MathF.Sin(a));

    public static float AngleOf(Vector2 v) => MathF.Atan2(v.Y, v.X);

    public static Vector2 SafeNormalize(Vector2 v) =>
        v.LengthSquared() > 1e-8f ? Vector2.Normalize(v) : Vector2.Zero;

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Random float in [a, b).</summary>
    public static float Between(this Random rng, float a, float b) => a + rng.NextSingle() * (b - a);
}
