using System.Numerics;

namespace ApexDrift;

public enum RaceState { Countdown, Racing, Finished }

public enum Difficulty { Easy, Medium, Hard }

/// <summary>Player controls for the current frame, filled in by the window.</summary>
public sealed class PlayerInput
{
    public float Throttle;
    public float Brake;
    public float Steer;
    public bool Handbrake;
    public bool Nitro;
    public bool Respawn;   // one-shot, consumed by the race
}

public sealed class Race
{
    private const float FixedStep = 1f / 120f;

    public Track Track { get; }
    public List<Car> Cars { get; } = new();
    public List<Car> Ranking { get; } = new();
    public Car Player { get; private set; } = null!;
    public int TotalLaps { get; }
    public Difficulty Difficulty { get; }

    public RaceState State { get; private set; } = RaceState.Countdown;
    public float Countdown { get; private set; } = 3.8f;
    public float RaceTime { get; private set; }
    public float Time { get; private set; }
    public float Shake { get; private set; }
    public int FinishedCount { get; private set; }

    public ParticleSystem Fx { get; } = new();
    public SkidMarks Skids { get; } = new();

    public float RubberBand => Difficulty switch
    {
        Difficulty.Easy => 0.07f,
        Difficulty.Medium => 0.05f,
        _ => 0.03f,
    };

    private readonly Random rng = new();
    private float acc;

    public Race(Track track, int laps, Difficulty difficulty, int aiCount = 5)
    {
        Track = track;
        TotalLaps = laps;
        Difficulty = difficulty;

        string[] names = { "Vega", "Nova", "Rex", "Blitz", "Kuro" };
        Color[] bodies =
        {
            Color.FromArgb(40, 110, 230), Color.FromArgb(235, 200, 40), Color.FromArgb(232, 232, 238),
            Color.FromArgb(240, 120, 30), Color.FromArgb(150, 70, 210),
        };
        Color[] accents =
        {
            Color.White, Color.FromArgb(30, 30, 30), Color.FromArgb(200, 30, 30),
            Color.FromArgb(30, 30, 30), Color.FromArgb(240, 220, 90),
        };

        float baseSkill = difficulty switch
        {
            Difficulty.Easy => 0.80f,
            Difficulty.Medium => 0.90f,
            _ => 0.97f,
        };

        int total = aiCount + 1;
        for (int slot = 0; slot < total; slot++)
        {
            Car car;
            if (slot == total - 1)
            {
                car = new Car("You", Color.FromArgb(220, 40, 40), Color.White, isPlayer: true);
                Player = car;
            }
            else
            {
                int k = slot % names.Length;
                car = new Car(names[k], bodies[k], accents[k], isPlayer: false)
                {
                    Skill = baseSkill + (rng.NextSingle() - 0.5f) * 0.06f,
                    BaseLane = (rng.NextSingle() - 0.5f) * 70f,
                };
            }
            PlaceOnGrid(car, slot);
            Cars.Add(car);
        }

        foreach (var c in Cars)
        {
            UpdateTrackState(c);
            Ranking.Add(c);
        }
        UpdateRanking();
    }

    private void PlaceOnGrid(Car car, int slot)
    {
        int row = slot / 2;
        int col = slot % 2;
        float back = 45f + row * 62f + (col == 1 ? 24f : 0f);
        int idx = MathUtil.Mod(-(int)MathF.Round(back / Track.Ds), Track.N);
        float lat = col == 0 ? -30f : 30f;

        car.Pos = Track.P[idx] + Track.Nrm[idx] * lat;
        car.Heading = MathF.Atan2(Track.Tan[idx].Y, Track.Tan[idx].X);
        car.Vel = Vector2.Zero;
        car.Index = idx;
        car.PrevFrac = idx;
        car.Progress = idx - Track.N;       // negative: still behind the start line
        car.Lane = lat;
    }

    public int PlaceOf(Car c) => Ranking.IndexOf(c) + 1;

    public void Respawn(Car c)
    {
        int i = c.Index;
        c.Pos = Track.P[i];
        c.Heading = MathF.Atan2(Track.Tan[i].Y, Track.Tan[i].X);
        c.Vel = Vector2.Zero;
        c.YawRate = 0f;
        c.Steer = 0f;
        c.Skidding = false;
        c.Lane = 0f;
    }

    // --------------------------------------------------------------- update

    public void Update(float dt, PlayerInput input)
    {
        acc += dt;
        int steps = 0;
        while (acc >= FixedStep && steps < 10)
        {
            Tick(FixedStep, input);
            acc -= FixedStep;
            steps++;
        }
        if (steps == 10) acc = 0f;
    }

    private void Tick(float h, PlayerInput input)
    {
        Time += h;
        Shake = MathF.Max(0f, Shake - h * 2.2f);
        Fx.Update(h);

        if (State == RaceState.Countdown)
        {
            Countdown -= h;
            input.Respawn = false;
            if (Countdown <= 0f)
            {
                State = RaceState.Racing;
                RaceTime = 0f;
            }
            return;
        }

        RaceTime += h;

        foreach (var c in Cars)
        {
            c.LapFlash = MathF.Max(0f, c.LapFlash - h);
            c.OnGrass = MathF.Abs(c.Lateral) > Track.HalfWidth + Track.KerbWidth;

            if (!c.IsAi)
            {
                c.InThrottle = input.Throttle;
                c.InBrake = input.Brake;
                c.InSteer = input.Steer;
                c.InHandbrake = input.Handbrake;
                c.InNitro = input.Nitro;
                if (input.Respawn)
                {
                    input.Respawn = false;
                    Respawn(c);
                }
            }
            else
            {
                AiDriver.Drive(this, c, h);
            }

            c.Step(h);
            UpdateTrackState(c);
            BarrierCollide(c);
            SpawnEffects(c, h);
        }

        for (int i = 0; i < Cars.Count; i++)
            for (int j = i + 1; j < Cars.Count; j++)
                CarCollide(Cars[i], Cars[j]);

        UpdateRanking();
    }

    private void UpdateTrackState(Car c)
    {
        Track.Project(c.Pos, c.Index, 24, out int idx, out float t, out float lateral);
        c.Index = idx;
        c.SegT = t;
        c.Lateral = lateral;

        // Unwrapped progress: robust against the start-line wrap and against reversing over the line.
        float frac = idx + t;
        float delta = frac - c.PrevFrac;
        float half = Track.N * 0.5f;
        if (delta > half) delta -= Track.N;
        else if (delta < -half) delta += Track.N;
        c.Progress += delta;
        c.PrevFrac = frac;

        if (State == RaceState.Racing)
        {
            int laps = (int)MathF.Floor(c.Progress / Track.N);
            if (laps > c.LapsDone)
            {
                float lapTime = RaceTime - c.LapStart;
                c.LastLap = lapTime;
                if (lapTime < c.BestLap) c.BestLap = lapTime;
                c.LapStart = RaceTime;
                c.LapsDone = laps;
                c.LapFlash = 4f;

                if (c.LapsDone >= TotalLaps && !c.Finished)
                    FinishCar(c);
            }
        }
    }

    private void FinishCar(Car c)
    {
        c.Finished = true;
        c.FinishTime = RaceTime;
        FinishedCount++;
        if (c.IsPlayer)
        {
            State = RaceState.Finished;
            c.IsAi = true;          // autopilot takes over so the car doesn't just sit there
            c.Skill = 0.8f;
        }
    }

    private void UpdateRanking()
    {
        Ranking.Sort((a, b) =>
        {
            if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
            if (a.Finished) return a.FinishTime.CompareTo(b.FinishTime);
            return b.Progress.CompareTo(a.Progress);
        });
    }

    // ------------------------------------------------------------ collisions

    private void BarrierCollide(Car c)
    {
        float lim = Track.BarrierDist - Car.Radius - 2f;
        float abs = MathF.Abs(c.Lateral);
        if (abs <= lim) return;

        int i = c.Index;
        int j = (i + 1) % Track.N;
        Vector2 closest = Vector2.Lerp(Track.P[i], Track.P[j], c.SegT);
        Vector2 n = Track.Nrm[i] * (c.Lateral >= 0f ? 1f : -1f);   // outward normal
        c.Pos = closest + n * lim;

        float vn = Vector2.Dot(c.Vel, n);
        if (vn > 0f)
        {
            c.Vel -= n * vn * 1.3f;     // bounce with a little restitution
            c.Vel *= 0.94f;
            c.YawRate *= 0.5f;
            if (vn > 60f)
            {
                Sparks(c.Pos + n * Car.Radius, 6 + (int)(vn / 40f));
                if (c.IsPlayer) Shake = MathF.Min(1f, Shake + vn / 500f);
            }
        }
    }

    private void CarCollide(Car a, Car b)
    {
        Vector2 fa = a.Forward, fb = b.Forward;
        const float offset = 11f;
        const float min = Car.Radius * 2f;

        for (int i = -1; i <= 1; i += 2)
        {
            for (int j = -1; j <= 1; j += 2)
            {
                Vector2 ca = a.Pos + fa * (i * offset);
                Vector2 cb = b.Pos + fb * (j * offset);
                Vector2 d = cb - ca;
                float dist = d.Length();
                if (dist >= min || dist < 0.001f) continue;

                Vector2 n = d / dist;
                float pen = min - dist;
                a.Pos -= n * pen * 0.5f;
                b.Pos += n * pen * 0.5f;

                float rel = Vector2.Dot(b.Vel - a.Vel, n);
                if (rel >= 0f) continue;

                float imp = -(1f + 0.35f) * rel * 0.5f;
                a.Vel -= n * imp;
                b.Vel += n * imp;

                // A bit of spin from where the contact happened.
                Vector2 cp = (ca + cb) * 0.5f;
                Vector2 ra = cp - a.Pos, rb = cp - b.Pos;
                a.YawRate += (ra.X * -n.Y - ra.Y * -n.X) * imp * 0.0003f;
                b.YawRate += (rb.X * n.Y - rb.Y * n.X) * imp * 0.0003f;

                if (-rel > 70f)
                {
                    Sparks(cp, 5);
                    if (a.IsPlayer || b.IsPlayer) Shake = MathF.Min(1f, Shake + -rel / 600f);
                }
            }
        }
    }

    // --------------------------------------------------------------- effects

    private void Sparks(Vector2 pos, int count)
    {
        for (int k = 0; k < count; k++)
        {
            float angle = rng.NextSingle() * MathF.Tau;
            float speed = 80f + rng.NextSingle() * 240f;
            Fx.Emit(pos, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
                0.25f + rng.NextSingle() * 0.25f, 3.5f, 1f, Color.FromArgb(255, 255, 210, 90), 3f);
        }
    }

    private void SpawnEffects(Car c, float h)
    {
        Vector2 f = c.Forward, r = c.RightVec;
        Vector2 rl = c.Pos - f * 13f - r * 9f;
        Vector2 rr = c.Pos - f * 13f + r * 9f;
        float spd = c.Speed;

        bool sliding = !c.OnGrass &&
            (c.Slip > 0.30f ||
             (c.InHandbrake && spd > 140f) ||
             (c.Braking && c.InBrake > 0.9f && spd > 380f));

        if (sliding)
        {
            if (c.Skidding)
            {
                if ((rl - c.LastRearL).LengthSquared() > 25f)
                {
                    Skids.Add(c.LastRearL, rl, Time);
                    Skids.Add(c.LastRearR, rr, Time);
                    c.LastRearL = rl;
                    c.LastRearR = rr;
                }
            }
            else
            {
                c.LastRearL = rl;
                c.LastRearR = rr;
                c.Skidding = true;
            }

            if (rng.NextSingle() < h * 70f)
            {
                Vector2 jitter = new((rng.NextSingle() - 0.5f) * 40f, (rng.NextSingle() - 0.5f) * 40f);
                Fx.Emit(rl, c.Vel * 0.15f + jitter, 0.7f, 8f, 34f, Color.FromArgb(140, 230, 230, 230), 2.2f);
                Fx.Emit(rr, c.Vel * 0.15f + jitter, 0.7f, 8f, 34f, Color.FromArgb(140, 230, 230, 230), 2.2f);
            }
        }
        else
        {
            c.Skidding = false;
        }

        if (c.OnGrass && spd > 90f && rng.NextSingle() < h * 60f)
        {
            Vector2 jitter = new((rng.NextSingle() - 0.5f) * 60f, (rng.NextSingle() - 0.5f) * 60f);
            Fx.Emit(rl, jitter - c.Vel * 0.1f, 0.6f, 6f, 22f, Color.FromArgb(130, 120, 100, 70), 2.5f);
            Fx.Emit(rr, jitter - c.Vel * 0.1f, 0.6f, 6f, 22f, Color.FromArgb(130, 120, 100, 70), 2.5f);
        }

        if (c.NitroActive && rng.NextSingle() < h * 140f)
        {
            Vector2 back = c.Pos - f * 26f;
            Vector2 v = -f * (220f + rng.NextSingle() * 120f) + c.Vel * 0.5f;
            Fx.Emit(back, v, 0.25f, 10f, 2f, Color.FromArgb(230, 255, 170, 40), 3f);
            if (rng.Next(3) == 0)
                Fx.Emit(back, v * 0.8f, 0.18f, 6f, 1f, Color.FromArgb(200, 120, 180, 255), 3f);
        }
    }
}
