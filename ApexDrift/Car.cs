using System.Numerics;

namespace ApexDrift;

public sealed class Car
{
    // Tuning (pixels, seconds).
    public const float MaxSpeed = 700f;
    public const float Accel = 950f;
    public const float BrakePower = 1500f;
    public const float ReverseAccel = 520f;
    public const float MaxReverse = 190f;
    public const float Radius = 13f;

    public string Name;
    public Color Body;
    public Color Accent;
    public bool IsPlayer;
    public bool IsAi;

    // Physics state.
    public Vector2 Pos;
    public Vector2 Vel;
    public float Heading;
    public float YawRate;
    public float Steer;
    public float ForwardSpeed;
    public float LateralSpeed;
    public float Slip;
    public bool OnGrass;
    public bool Braking;
    public float Nitro = 1f;
    public bool NitroActive;

    // Driver inputs (player or AI writes these every tick).
    public float InThrottle;
    public float InBrake;
    public float InSteer;
    public bool InHandbrake;
    public bool InNitro;

    // Track / race state.
    public int Index;
    public float SegT;
    public float Lateral;
    public float PrevFrac;
    public float Progress;
    public int LapsDone;
    public float LapStart;
    public float LastLap;
    public float BestLap = float.MaxValue;
    public float LapFlash;
    public bool Finished;
    public float FinishTime;

    // AI.
    public float Skill = 1f;
    public float BaseLane;
    public float Lane;
    public float StuckTimer;

    // Skid mark chain.
    public Vector2 LastRearL;
    public Vector2 LastRearR;
    public bool Skidding;

    public Car(string name, Color body, Color accent, bool isPlayer)
    {
        Name = name;
        Body = body;
        Accent = accent;
        IsPlayer = isPlayer;
        IsAi = !isPlayer;
    }

    public Vector2 Forward => new(MathF.Cos(Heading), MathF.Sin(Heading));
    public Vector2 RightVec => new(-MathF.Sin(Heading), MathF.Cos(Heading));
    public float Speed => Vel.Length();

    /// <summary>One fixed physics step.</summary>
    public void Step(float h)
    {
        Vector2 f = Forward;
        Vector2 r = RightVec;
        float vF = Vector2.Dot(Vel, f);
        float vR = Vector2.Dot(Vel, r);

        // --- Nitro --------------------------------------------------------
        bool nitroOn = InNitro && Nitro > 0.02f && InThrottle > 0.1f;
        NitroActive = nitroOn;
        if (nitroOn) Nitro = MathF.Max(0f, Nitro - h * 0.30f);
        else Nitro = MathF.Min(1f, Nitro + h * 0.05f);

        float maxSp = MaxSpeed * (nitroOn ? 1.18f : 1f);
        float accel = Accel * (nitroOn ? 1.65f : 1f);

        // --- Longitudinal: throttle, brake, reverse ----------------------
        Braking = false;
        if (InThrottle > 0f)
        {
            if (vF < -5f)
            {
                vF += BrakePower * InThrottle * h;   // throttle while rolling backwards acts as a brake
                Braking = true;
            }
            else
            {
                vF += accel * InThrottle * MathF.Max(0f, 1f - vF / maxSp) * h;
            }
        }
        if (InBrake > 0f)
        {
            if (vF > 8f)
            {
                vF = MathF.Max(0f, vF - BrakePower * InBrake * h);
                Braking = true;
            }
            else
            {
                vF = MathF.Max(-MaxReverse, vF - ReverseAccel * InBrake * h);
            }
        }

        // Engine braking / rolling resistance, much worse on grass.
        float drag = 0.28f + (OnGrass ? 1.2f : 0f) + (InHandbrake ? 0.6f : 0f);
        vF *= MathF.Exp(-drag * h);

        // --- Lateral grip --------------------------------------------------
        float grip = OnGrass ? 4.8f : 9.0f;
        if (InHandbrake) grip = OnGrass ? 1.5f : 1.8f;
        else if (MathF.Abs(vR) > 170f) grip *= 0.55f;          // tyres saturate -> slide
        vR *= MathF.Exp(-grip * h);
        Slip = Math.Clamp(MathF.Abs(vR) / 220f, 0f, 1f);

        // --- Steering ------------------------------------------------------
        float rate = MathF.Abs(InSteer) < 0.01f ? 10f : 5.5f;
        Steer = MathUtil.MoveToward(Steer, InSteer, rate * h);

        float spd = MathF.Abs(vF);
        float authority = Math.Clamp(spd / 140f, 0f, 1f);       // no steering when stationary
        float s = spd / 520f;
        float yawTarget = Steer * authority * 2.7f / (1f + s * s); // less turn rate at high speed
        if (vF < -5f) yawTarget = -yawTarget;
        if (InHandbrake) yawTarget *= 1.4f;
        YawRate += (yawTarget - YawRate) * MathF.Min(1f, 12f * h);
        YawRate = Math.Clamp(YawRate, -5f, 5f);
        Heading += YawRate * h;

        // Velocity keeps its world direction; next step the changed heading shows up as sideways slip.
        Vel = f * vF + r * vR;
        Pos += Vel * h;

        ForwardSpeed = vF;
        LateralSpeed = vR;
    }

    /// <summary>
    /// Fastest speed (px/s) at which a car can hold a corner of the given radius,
    /// derived from the same turn-rate curve used in <see cref="Step"/>. Used by the AI.
    /// </summary>
    public static float MaxCornerSpeed(float radius)
    {
        float best = 80f;
        for (float v = 80f; v <= 800f; v += 10f)
        {
            float s = v / 520f;
            float omega = 2.7f * MathF.Min(1f, v / 140f) / (1f + s * s);
            float rNeeded = v / omega;
            if (rNeeded <= radius) best = v;
            else break;
        }
        return best;
    }
}
