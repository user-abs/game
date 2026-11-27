using System.Drawing.Drawing2D;
using System.Numerics;

namespace ApexDrift;

public sealed record TrackDef(
    string Name,
    string Description,
    Vector2[] Control,
    float Scale,
    Color Grass,
    Color Asphalt,
    Color TreeColor);

public static class TrackLibrary
{
    public static readonly TrackDef[] All =
    {
        new("Sunset Circuit", "Fast, flowing sweepers and a long back straight.",
            SunsetPoints(), 1.4f,
            Color.FromArgb(76, 128, 64), Color.FromArgb(60, 62, 68), Color.FromArgb(40, 96, 48)),

        new("Coastal Run", "Wide, open bends that reward a clean racing line.",
            Polar(2000f, 1450f, 1.00f, 0.80f, 0.95f, 1.18f, 0.92f, 0.72f, 0.98f, 1.20f, 1.05f, 0.78f, 1.10f, 1.00f),
            1f,
            Color.FromArgb(206, 188, 132), Color.FromArgb(56, 58, 64), Color.FromArgb(70, 140, 90)),

        new("Mountain Pass", "Tight and technical. Brake early, exit fast.",
            Polar(1500f, 1100f, 1.00f, 0.80f, 0.98f, 0.78f, 1.00f, 1.22f, 1.00f, 0.80f, 1.02f, 1.25f, 1.05f, 0.82f, 1.00f, 1.20f),
            1f,
            Color.FromArgb(104, 118, 98), Color.FromArgb(70, 66, 66), Color.FromArgb(52, 84, 60)),
    };

    private static Vector2[] SunsetPoints() => new[]
    {
        new Vector2(0, 0), new Vector2(900, -60), new Vector2(1700, -300), new Vector2(2300, -100),
        new Vector2(2500, 500), new Vector2(2100, 950), new Vector2(1500, 900), new Vector2(1100, 1300),
        new Vector2(1300, 1800), new Vector2(800, 2100), new Vector2(100, 1900), new Vector2(-300, 1300),
        new Vector2(-200, 700),
    };

    /// <summary>Star-shaped loop: one control point per radius, evenly spaced around an ellipse. Never self-intersects.</summary>
    private static Vector2[] Polar(float rx, float ry, params float[] radii)
    {
        int n = radii.Length;
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = MathF.Tau * i / n - MathF.PI / 2f;
            pts[i] = new Vector2(MathF.Cos(a) * rx * radii[i], MathF.Sin(a) * ry * radii[i]);
        }
        return pts;
    }
}

/// <summary>A slice of the track's static geometry, so only nearby slices are drawn.</summary>
public sealed class TrackChunk : IDisposable
{
    public Vector2 Mid;
    public float Radius;
    public readonly GraphicsPath Edge = new(FillMode.Winding);
    public readonly GraphicsPath KerbRed = new(FillMode.Winding);
    public readonly GraphicsPath KerbWhite = new(FillMode.Winding);
    public readonly GraphicsPath Dashes = new(FillMode.Winding);
    public readonly GraphicsPath BarrierRed = new(FillMode.Winding);
    public readonly GraphicsPath BarrierWhite = new(FillMode.Winding);

    public void Dispose()
    {
        Edge.Dispose(); KerbRed.Dispose(); KerbWhite.Dispose();
        Dashes.Dispose(); BarrierRed.Dispose(); BarrierWhite.Dispose();
    }
}

public struct Blotch
{
    public Vector2 Pos;
    public float Radius;
    public Color Color;
}

public struct Tree
{
    public Vector2 Pos;
    public float Radius;
    public float Shade;
}

public sealed class Track : IDisposable
{
    public const float HalfWidth = 95f;
    public const float KerbWidth = 14f;
    public const float BarrierDist = 175f;
    private const int ChunkSize = 48;

    public string Name { get; }
    public string Description { get; }
    public int N { get; }
    public float Ds { get; }
    public float Length => N * Ds;

    /// <summary>Centre line samples, evenly spaced ~10px apart.</summary>
    public Vector2[] P { get; }
    public Vector2[] Tan { get; }
    /// <summary>Normal pointing to the RIGHT of the direction of travel.</summary>
    public Vector2[] Nrm { get; }
    /// <summary>Signed curvature (1/px). Positive = turning right.</summary>
    public float[] Curv { get; }

    public Color Grass { get; }
    public Color Asphalt { get; }
    public Color TreeColor { get; }
    public Vector2 Min { get; }
    public Vector2 Max { get; }
    public RectangleF Bounds { get; }

    public GraphicsPath AsphaltPath { get; private set; } = null!;
    public GraphicsPath StartBlack { get; private set; } = null!;
    public GraphicsPath StartWhite { get; private set; } = null!;
    public List<TrackChunk> Chunks { get; } = new();
    public List<Blotch> Blotches { get; } = new();
    public List<Tree> Trees { get; } = new();

    public Track(TrackDef def)
    {
        Name = def.Name;
        Description = def.Description;
        Grass = def.Grass;
        Asphalt = def.Asphalt;
        TreeColor = def.TreeColor;

        // 1. Dense Catmull-Rom sampling through the control points.
        var c = def.Control;
        int cn = c.Length;
        var dense = new List<Vector2>(cn * 40);
        for (int i = 0; i < cn; i++)
        {
            Vector2 p0 = c[(i - 1 + cn) % cn] * def.Scale;
            Vector2 p1 = c[i] * def.Scale;
            Vector2 p2 = c[(i + 1) % cn] * def.Scale;
            Vector2 p3 = c[(i + 2) % cn] * def.Scale;
for (int s = 0; s < 40; s++)
{
    float t = s / 40f;
    float t2 = t * t;
    float t3 = t2 * t;

    Vector2 point = (
        p1 * 2f +
        (p2 - p0) * t +
        (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2 +
        (-p0 + p1 * 3f - p2 * 3f + p3) * t3
    ) * 0.5f;

    dense.Add(point);
}
        }

        // 2. Resample to uniform arc length so index maths (look-ahead, progress) is simple.
        int m = dense.Count;
        var cum = new float[m + 1];
        for (int i = 0; i < m; i++)
            cum[i + 1] = cum[i] + Vector2.Distance(dense[i], dense[(i + 1) % m]);
        float total = cum[m];

        N = Math.Max(64, (int)(total / 10f));
        Ds = total / N;
        P = new Vector2[N];
        int seg = 0;
        for (int k = 0; k < N; k++)
        {
            float s = k * Ds;
            while (seg < m - 1 && cum[seg + 1] < s) seg++;
            float len = cum[seg + 1] - cum[seg];
            float u = len > 1e-4f ? (s - cum[seg]) / len : 0f;
            P[k] = Vector2.Lerp(dense[seg], dense[(seg + 1) % m], u);
        }

        // 3. Tangents, normals, curvature.
        Tan = new Vector2[N];
        Nrm = new Vector2[N];
        for (int i = 0; i < N; i++)
        {
            var d = P[(i + 1) % N] - P[(i - 1 + N) % N];
            Tan[i] = Vector2.Normalize(d);
            Nrm[i] = new Vector2(-Tan[i].Y, Tan[i].X);
        }

        var raw = new float[N];
        for (int i = 0; i < N; i++)
        {
            int a = MathUtil.Mod(i - 3, N), b = MathUtil.Mod(i + 3, N);
            float ang = MathUtil.NormalizeAngle(
                MathF.Atan2(Tan[b].Y, Tan[b].X) - MathF.Atan2(Tan[a].Y, Tan[a].X));
            raw[i] = ang / (6f * Ds);
        }
        Curv = new float[N];
        for (int i = 0; i < N; i++)
        {
            float sum = 0f;
            for (int k = -4; k <= 4; k++) sum += raw[MathUtil.Mod(i + k, N)];
            Curv[i] = sum / 9f;
        }

        // 4. Bounds.
        var mn = new Vector2(float.MaxValue);
        var mx = new Vector2(float.MinValue);
        for (int i = 0; i < N; i++)
        {
            mn = Vector2.Min(mn, P[i]);
            mx = Vector2.Max(mx, P[i]);
        }
        Min = mn;
        Max = mx;
        Bounds = new RectangleF(mn.X - 900f, mn.Y - 900f, mx.X - mn.X + 1800f, mx.Y - mn.Y + 1800f);

        BuildGeometry();
        BuildScenery();
    }

    // ------------------------------------------------------------ geometry

    private static void AddQuad(GraphicsPath path, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        path.AddPolygon(new[] { MathUtil.ToPoint(a), MathUtil.ToPoint(b), MathUtil.ToPoint(c), MathUtil.ToPoint(d) });
    }

    private void BuildGeometry()
    {
        // Asphalt: a ring between the left and right edges (even-odd fill).
        var left = new PointF[N];
        var right = new PointF[N];
        for (int i = 0; i < N; i++)
        {
            left[i] = MathUtil.ToPoint(P[i] - Nrm[i] * HalfWidth);
            right[i] = MathUtil.ToPoint(P[i] + Nrm[i] * HalfWidth);
        }
        AsphaltPath = new GraphicsPath(FillMode.Alternate);
        AsphaltPath.AddPolygon(left);
        AsphaltPath.AddPolygon(right);

        const float cornerCurv = 1f / 1400f;
        const float barrierWidth = 14f;

        for (int start = 0; start < N; start += ChunkSize)
        {
            var chunk = new TrackChunk();
            int end = Math.Min(N, start + ChunkSize);
            chunk.Mid = P[(start + end) / 2 % N];
            chunk.Radius = ChunkSize * Ds * 0.5f + BarrierDist + barrierWidth + 40f;

            for (int i = start; i < end; i++)
            {
                int j = (i + 1) % N;
                Vector2 ni = Nrm[i], nj = Nrm[j];

                // White edge lines on both sides.
                AddQuad(chunk.Edge,
                    P[i] + ni * (HalfWidth - 9f), P[j] + nj * (HalfWidth - 9f),
                    P[j] + nj * (HalfWidth - 4f), P[i] + ni * (HalfWidth - 4f));
                AddQuad(chunk.Edge,
                    P[i] - ni * (HalfWidth - 9f), P[j] - nj * (HalfWidth - 9f),
                    P[j] - nj * (HalfWidth - 4f), P[i] - ni * (HalfWidth - 4f));

                // Red/white kerbs on corners.
                if (MathF.Abs(Curv[i]) > cornerCurv)
                {
                    var kerb = (i / 3) % 2 == 0 ? chunk.KerbRed : chunk.KerbWhite;
                    AddQuad(kerb,
                        P[i] + ni * HalfWidth, P[j] + nj * HalfWidth,
                        P[j] + nj * (HalfWidth + KerbWidth), P[i] + ni * (HalfWidth + KerbWidth));
                    AddQuad(kerb,
                        P[i] - ni * HalfWidth, P[j] - nj * HalfWidth,
                        P[j] - nj * (HalfWidth + KerbWidth), P[i] - ni * (HalfWidth + KerbWidth));
                }

                // Dashed centre line.
                if ((i / 4) % 2 == 0)
                    AddQuad(chunk.Dashes,
                        P[i] + ni * 2.5f, P[j] + nj * 2.5f, P[j] - nj * 2.5f, P[i] - ni * 2.5f);

                // Tyre-wall barriers on both sides.
                var wall = (i / 2) % 2 == 0 ? chunk.BarrierRed : chunk.BarrierWhite;
                AddQuad(wall,
                    P[i] + ni * BarrierDist, P[j] + nj * BarrierDist,
                    P[j] + nj * (BarrierDist + barrierWidth), P[i] + ni * (BarrierDist + barrierWidth));
                AddQuad(wall,
                    P[i] - ni * BarrierDist, P[j] - nj * BarrierDist,
                    P[j] - nj * (BarrierDist + barrierWidth), P[i] - ni * (BarrierDist + barrierWidth));
            }
            Chunks.Add(chunk);
        }

        // Chequered start/finish line at sample 0.
        StartBlack = new GraphicsPath(FillMode.Winding);
        StartWhite = new GraphicsPath(FillMode.Winding);
        const int cols = 16;
        const int rows = 2;
        float cw = HalfWidth * 2f / cols;
        float rh = 12f;
        Vector2 origin = P[0];
        Vector2 t0 = Tan[0], n0 = Nrm[0];
        for (int r = 0; r < rows; r++)
        {
            for (int col = 0; col < cols; col++)
            {
                float y0 = -HalfWidth + col * cw;
                float y1 = y0 + cw;
                float x0 = r * rh;
                float x1 = x0 + rh;
                var path = (r + col) % 2 == 0 ? StartWhite : StartBlack;
                AddQuad(path,
                    origin + t0 * x0 + n0 * y0, origin + t0 * x1 + n0 * y0,
                    origin + t0 * x1 + n0 * y1, origin + t0 * x0 + n0 * y1);
            }
        }
    }

    private void BuildScenery()
    {
        var rng = new Random(N * 31 + 7);

        // Soft grass blotches for a little texture.
        for (int i = 0; i < 700; i++)
        {
            float x = Bounds.X + rng.NextSingle() * Bounds.Width;
            float y = Bounds.Y + rng.NextSingle() * Bounds.Height;
            bool dark = rng.Next(2) == 0;
            Blotches.Add(new Blotch
            {
                Pos = new Vector2(x, y),
                Radius = 40f + rng.NextSingle() * 140f,
                Color = dark ? Color.FromArgb(26, 0, 0, 0) : Color.FromArgb(20, 255, 255, 255),
            });
        }

        // Trees around the circuit, kept clear of the barriers.
        for (int attempt = 0; attempt < 4000 && Trees.Count < 280; attempt++)
        {
            var pt = new Vector2(
                Bounds.X + rng.NextSingle() * Bounds.Width,
                Bounds.Y + rng.NextSingle() * Bounds.Height);

            float minD2 = float.MaxValue;
            for (int i = 0; i < N; i += 2)
                minD2 = MathF.Min(minD2, Vector2.DistanceSquared(pt, P[i]));
            float minD = MathF.Sqrt(minD2);
            if (minD < BarrierDist + 70f || minD > 1100f) continue;

            bool clash = false;
            foreach (var t in Trees)
            {
                if (Vector2.DistanceSquared(t.Pos, pt) < 90f * 90f) { clash = true; break; }
            }
            if (clash) continue;

            Trees.Add(new Tree
            {
                Pos = pt,
                Radius = 30f + rng.NextSingle() * 26f,
                Shade = 0.75f + rng.NextSingle() * 0.5f,
            });
        }
    }

    // ------------------------------------------------------------ queries

    public int FindNearest(Vector2 pos)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < N; i++)
        {
            float d = Vector2.DistanceSquared(pos, P[i]);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private float SegmentT(Vector2 pos, int i, out float dist2)
    {
        Vector2 a = P[i];
        Vector2 ab = P[(i + 1) % N] - a;
        float len2 = ab.LengthSquared();
        float t = len2 > 1e-6f ? Math.Clamp(Vector2.Dot(pos - a, ab) / len2, 0f, 1f) : 0f;
        dist2 = Vector2.DistanceSquared(pos, a + ab * t);
        return t;
    }

    /// <summary>
    /// Finds where a point sits relative to the track. Searches a window around <paramref name="hint"/>
    /// (cheap), falling back to a full search if the point is far away (e.g. after a respawn).
    /// </summary>
    public void Project(Vector2 pos, int hint, int window, out int idx, out float t, out float lateral)
    {
        int best = hint;
        float bestD = float.MaxValue;
        for (int k = -window; k <= window; k++)
        {
            int j = MathUtil.Mod(hint + k, N);
            float d = Vector2.DistanceSquared(pos, P[j]);
            if (d < bestD) { bestD = d; best = j; }
        }
        if (bestD > 500f * 500f)
        {
            best = FindNearest(pos);
        }

        int i0 = MathUtil.Mod(best - 1, N);
        float t0 = SegmentT(pos, i0, out float d0);
        float t1 = SegmentT(pos, best, out float d1);
        if (d0 < d1) { idx = i0; t = t0; }
        else { idx = best; t = t1; }

        Vector2 closest = Vector2.Lerp(P[idx], P[(idx + 1) % N], t);
        lateral = Vector2.Dot(pos - closest, Nrm[idx]);
    }

    public void Dispose()
    {
        AsphaltPath.Dispose();
        StartBlack.Dispose();
        StartWhite.Dispose();
        foreach (var c in Chunks) c.Dispose();
        Chunks.Clear();
    }
}
