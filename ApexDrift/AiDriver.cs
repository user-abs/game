using System.Numerics;

namespace ApexDrift;

/// <summary>
/// Writes a car's input fields each tick. Pure pursuit steering toward a look-ahead point on a lane,
/// with braking planned from the corner speeds coming up on the track.
/// </summary>
public static class AiDriver
{
    public static void Drive(Race race, Car c, float h)
    {
        Track tr = race.Track;
        int n = tr.N;
        float speed = MathF.Max(0f, c.ForwardSpeed);

        // --- Recover if wedged against a barrier ---------------------------------
        if (race.RaceTime > 2.5f && speed < 30f) c.StuckTimer += h;
        else c.StuckTimer = 0f;
        if (c.StuckTimer > 2.2f)
        {
            race.Respawn(c);
            c.StuckTimer = 0f;
        }

        // --- Where to aim ----------------------------------------------------------
        int look = Math.Clamp((int)(speed * 0.32f / tr.Ds), 8, 34);
        int ti = MathUtil.Mod(c.Index + look, n);

        // Lean toward the inside of the upcoming corner (positive curvature = right turn = +normal).
        float curv = tr.Curv[MathUtil.Mod(ti + 8, n)];
        float lineLane = Math.Clamp(curv * 1400f, -1f, 1f) * (Track.HalfWidth * 0.55f);

        float wantLane = lineLane + c.BaseLane * 0.4f;
        float blockSpeed = float.MaxValue;

        foreach (var o in race.Cars)
        {
            if (o == c) continue;
            float dp = o.Progress - c.Progress;          // samples ahead of us
            if (dp <= 0f || dp >= 18f) continue;
            float dl = o.Lateral - c.Lateral;
            if (MathF.Abs(dl) >= 36f) continue;

            // Move over to the side with more room.
            float side = dl >= 0f ? -1f : 1f;
            wantLane = o.Lateral + side * 44f;
            if (dp < 9f && MathF.Abs(dl) < 26f)
                blockSpeed = MathF.Min(blockSpeed, MathF.Max(60f, o.ForwardSpeed));
        }

        float maxLane = Track.HalfWidth - 24f;
        wantLane = Math.Clamp(wantLane, -maxLane, maxLane);
        c.Lane += (wantLane - c.Lane) * MathF.Min(1f, 3.2f * h);

        Vector2 target = tr.P[ti] + tr.Nrm[ti] * c.Lane;
        Vector2 to = target - c.Pos;
        float desired = MathF.Atan2(to.Y, to.X);
        float err = MathUtil.NormalizeAngle(desired - c.Heading);
        c.InSteer = Math.Clamp(err * 2.2f, -1f, 1f);

        // --- Target speed: brake early enough for every corner in the next ~450px -----
        float vmax = Car.MaxSpeed * 0.93f * c.Skill;
        float best = vmax;
        const float brakeDecel = 900f;
        for (int j = 0; j <= 45; j += 3)
        {
            float k = MathF.Abs(tr.Curv[MathUtil.Mod(c.Index + j, n)]);
            if (k < 0.0003f) continue;
            float vc = Car.MaxCornerSpeed(1f / k) * 0.88f * c.Skill;
            float dist = j * tr.Ds;
            float allowed = MathF.Sqrt(vc * vc + 2f * brakeDecel * dist);
            if (allowed < best) best = allowed;
        }

        // Rubber band: slow down when well ahead of the player, push when far behind.
        float gap = race.Player.Progress - c.Progress;
        float rb = 1f + Math.Clamp(gap / (n * 0.12f), -1f, 1f) * race.RubberBand;
        best *= rb;
        best = MathF.Min(best, blockSpeed);

        float diff = best - speed;
        c.InThrottle = Math.Clamp(diff / 40f, 0f, 1f);
        c.InBrake = Math.Clamp(-diff / 50f, 0f, 1f);
        if (c.InBrake < 0.15f || speed < 20f) c.InBrake = 0f;   // no dithering, never reverse
        c.InHandbrake = false;
        c.InNitro = false;
    }
}
