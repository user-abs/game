using System.Numerics;

namespace ApexDrift;

public static class Units
{
    /// <summary>One world pixel in metres.</summary>
    public const float MetresPerPx = 0.125f;

    /// <summary>Converts pixels/second to km/h (0.125 m * 3.6).</summary>
    public const float KmhPerPxSec = 0.45f;
}

public static class MathUtil
{
    public static float NormalizeAngle(float a)
    {
        while (a > MathF.PI) a -= MathF.Tau;
        while (a < -MathF.PI) a += MathF.Tau;
        return a;
    }

    public static float MoveToward(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta) return target;
        return current + MathF.Sign(target - current) * maxDelta;
    }

    public static int Mod(int a, int n)
    {
        int r = a % n;
        return r < 0 ? r + n : r;
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static PointF ToPoint(Vector2 v) => new(v.X, v.Y);
}
