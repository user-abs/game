using System.Numerics;

namespace DungeonDelve;

public enum Tile : byte { Wall, Floor, Stairs }

public sealed class Room
{
    public int X, Y, W, H;
    public int CX => X + W / 2;
    public int CY => Y + H / 2;
}

public sealed class DungeonMap
{
    public const int TileSize = 32;
    public const int Width = 64;
    public const int Height = 46;

    public readonly Tile[,] Tiles = new Tile[Width, Height];
    public readonly bool[,] Visible = new bool[Width, Height];
    public readonly bool[,] Explored = new bool[Width, Height];
    public readonly float[,] Light = new float[Width, Height];
    public readonly byte[,] Variant = new byte[Width, Height];

    /// <summary>Steps to the player's tile (-1 = unreachable). Monsters walk downhill on this.</summary>
    public readonly int[,] Flow = new int[Width, Height];

    public readonly List<Room> Rooms = new();
    public int StartRoom;
    public int StairsRoom;
    public Point StairsTile;

    /// <summary>Bumped whenever new tiles are explored (lets the minimap know to redraw).</summary>
    public int ExploredVersion;

    private static readonly (int dx, int dy)[] Dirs =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    // ------------------------------------------------------------ generation

    public static DungeonMap Generate(int floor, Random rng)
    {
        var m = new DungeonMap();

        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                m.Variant[x, y] = (byte)rng.Next(256);

        // 1. Scatter non-overlapping rooms.
        int target = Math.Min(16, 9 + floor / 2);
        for (int attempt = 0; attempt < 500 && m.Rooms.Count < target; attempt++)
        {
            int w = rng.Next(6, 12);
            int h = rng.Next(5, 10);
            int x = rng.Next(2, Width - w - 2);
            int y = rng.Next(2, Height - h - 2);

            bool overlap = false;
            foreach (var r in m.Rooms)
            {
                if (x < r.X + r.W + 2 && x + w + 2 > r.X && y < r.Y + r.H + 2 && y + h + 2 > r.Y)
                {
                    overlap = true;
                    break;
                }
            }
            if (overlap) continue;

            m.Rooms.Add(new Room { X = x, Y = y, W = w, H = h });
            for (int ry = y; ry < y + h; ry++)
                for (int rx = x; rx < x + w; rx++)
                    m.Tiles[rx, ry] = Tile.Floor;
        }

        // 2. Connect every room to its nearest earlier room (guarantees a connected map)...
        for (int i = 1; i < m.Rooms.Count; i++)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int j = 0; j < i; j++)
            {
                float dx = m.Rooms[i].CX - m.Rooms[j].CX;
                float dy = m.Rooms[i].CY - m.Rooms[j].CY;
                float d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = j; }
            }
            m.Connect(m.Rooms[i], m.Rooms[best], rng);
        }

        // ...then add a few extra links so there are loops to run around.
        int extra = 2 + floor / 4;
        for (int k = 0; k < extra && m.Rooms.Count > 2; k++)
        {
            int a = rng.Next(m.Rooms.Count);
            int b = rng.Next(m.Rooms.Count);
            if (a != b) m.Connect(m.Rooms[a], m.Rooms[b], rng);
        }

        // 3. Start in room 0, put the stairs in the room that is furthest away by walking distance.
        m.StartRoom = 0;
        m.ComputeFlow(m.Rooms[0].CX, m.Rooms[0].CY);
        int far = 1, farDist = -1;
        for (int i = 1; i < m.Rooms.Count; i++)
        {
            int d = m.Flow[m.Rooms[i].CX, m.Rooms[i].CY];
            if (d > farDist) { farDist = d; far = i; }
        }
        m.StairsRoom = far;
        m.StairsTile = new Point(m.Rooms[far].CX, m.Rooms[far].CY);
        m.Tiles[m.StairsTile.X, m.StairsTile.Y] = Tile.Stairs;
        return m;
    }

    private void Carve(int x, int y)
    {
        if (x < 1 || y < 1 || x > Width - 2 || y > Height - 2) return;
        Tiles[x, y] = Tile.Floor;
    }

    /// <summary>Two-tile-wide L-shaped corridor between room centres.</summary>
    private void Connect(Room a, Room b, Random rng)
    {
        int x1 = a.CX, y1 = a.CY, x2 = b.CX, y2 = b.CY;
        if (rng.Next(2) == 0)
        {
            CarveH(x1, x2, y1);
            CarveV(y1, y2, x2);
        }
        else
        {
            CarveV(y1, y2, x1);
            CarveH(x1, x2, y2);
        }
    }

    private void CarveH(int xa, int xb, int y)
    {
        for (int x = Math.Min(xa, xb); x <= Math.Max(xa, xb); x++)
        {
            Carve(x, y);
            Carve(x, y + 1);
        }
    }

    private void CarveV(int ya, int yb, int x)
    {
        for (int y = Math.Min(ya, yb); y <= Math.Max(ya, yb); y++)
        {
            Carve(x, y);
            Carve(x + 1, y);
        }
    }

    // --------------------------------------------------------------- queries

    public static Point TileOf(Vector2 p) =>
        new((int)MathF.Floor(p.X / TileSize), (int)MathF.Floor(p.Y / TileSize));

    public static Vector2 TileCenter(int x, int y) => new((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);

    public Vector2 RandomPointInRoom(Room r, Random rng) =>
        TileCenter(rng.Next(r.X + 1, r.X + r.W - 1), rng.Next(r.Y + 1, r.Y + r.H - 1));

    public bool IsSolid(int tx, int ty) =>
        tx < 0 || ty < 0 || tx >= Width || ty >= Height || Tiles[tx, ty] == Tile.Wall;

    public bool IsSolidAt(Vector2 p) =>
        IsSolid((int)MathF.Floor(p.X / TileSize), (int)MathF.Floor(p.Y / TileSize));

    public bool Walkable(int x, int y) => !IsSolid(x, y);

    // ------------------------------------------------------------- collision

    /// <summary>True if a circle overlaps any wall tile.</summary>
    public bool CircleBlocked(Vector2 p, float r)
    {
        int x0 = (int)MathF.Floor((p.X - r) / TileSize);
        int x1 = (int)MathF.Floor((p.X + r) / TileSize);
        int y0 = (int)MathF.Floor((p.Y - r) / TileSize);
        int y1 = (int)MathF.Floor((p.Y + r) / TileSize);

        for (int ty = y0; ty <= y1; ty++)
        {
            for (int tx = x0; tx <= x1; tx++)
            {
                if (!IsSolid(tx, ty)) continue;
                float cx = Math.Clamp(p.X, tx * TileSize, (tx + 1) * TileSize);
                float cy = Math.Clamp(p.Y, ty * TileSize, (ty + 1) * TileSize);
                float dx = p.X - cx, dy = p.Y - cy;
                if (dx * dx + dy * dy < r * r) return true;
            }
        }
        return false;
    }

    /// <summary>Moves a circle by delta, sliding along walls (axes resolved separately, in small steps).</summary>
    public void Move(ref Vector2 pos, Vector2 delta, float radius)
    {
        float len = delta.Length();
        if (len < 1e-5f) return;
        int steps = Math.Max(1, (int)MathF.Ceiling(len / 8f));
        Vector2 step = delta / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector2 nx = pos + new Vector2(step.X, 0f);
            if (!CircleBlocked(nx, radius)) pos = nx;
            Vector2 ny = pos + new Vector2(0f, step.Y);
            if (!CircleBlocked(ny, radius)) pos = ny;
        }
    }

    public bool HasLineOfSight(Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        float len = d.Length();
        int steps = (int)(len / 8f) + 1;
        for (int i = 1; i < steps; i++)
        {
            if (IsSolidAt(a + d * (i / (float)steps))) return false;
        }
        return true;
    }

    // ----------------------------------------------------------- fog of war

    public void UpdateVisibility(Vector2 pos, int radius)
    {
        Array.Clear(Visible);
        Array.Clear(Light);
        Point pt = TileOf(pos);
        bool discovered = false;

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int tx = pt.X + dx, ty = pt.Y + dy;
                if (tx < 0 || ty < 0 || tx >= Width || ty >= Height) continue;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > radius) continue;
                if (!LineClear(pt.X, pt.Y, tx, ty)) continue;

                Visible[tx, ty] = true;
                Light[tx, ty] = Math.Clamp(1.15f - dist / (radius + 1f), 0.14f, 1f);
                if (!Explored[tx, ty])
                {
                    Explored[tx, ty] = true;
                    discovered = true;
                }
            }
        }
        if (discovered) ExploredVersion++;
    }

    /// <summary>Bresenham walk; the target tile itself is visible even if it is a wall.</summary>
    private bool LineClear(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int x = x0, y = y0;

        while (!(x == x1 && y == y1))
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
            if (x == x1 && y == y1) return true;
            if (IsSolid(x, y)) return false;
        }
        return true;
    }

    // ----------------------------------------------------------- flow field

    /// <summary>Breadth-first distances from a tile (8-way, no corner cutting).</summary>
    public void ComputeFlow(int sx, int sy)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                Flow[x, y] = -1;
        if (!Walkable(sx, sy)) return;

        var queue = new Queue<(int x, int y)>();
        Flow[sx, sy] = 0;
        queue.Enqueue((sx, sy));

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            int d = Flow[x, y];
            foreach (var (dx, dy) in Dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (!Walkable(nx, ny) || Flow[nx, ny] >= 0) continue;
                if (dx != 0 && dy != 0 && (!Walkable(x + dx, y) || !Walkable(x, y + dy))) continue;
                Flow[nx, ny] = d + 1;
                queue.Enqueue((nx, ny));
            }
        }
    }

    /// <summary>Centre of the neighbouring tile that is closer to the player, if any.</summary>
    public Vector2 FlowTarget(Vector2 pos, out bool found)
    {
        found = false;
        Point t = TileOf(pos);
        if (IsSolid(t.X, t.Y)) return pos;
        int cur = Flow[t.X, t.Y];
        if (cur <= 0) return pos;

        int best = cur, bx = t.X, by = t.Y;
        foreach (var (dx, dy) in Dirs)
        {
            int nx = t.X + dx, ny = t.Y + dy;
            if (!Walkable(nx, ny)) continue;
            if (dx != 0 && dy != 0 && (!Walkable(t.X + dx, t.Y) || !Walkable(t.X, t.Y + dy))) continue;
            int f = Flow[nx, ny];
            if (f >= 0 && f < best) { best = f; bx = nx; by = ny; }
        }
        if (best == cur) return pos;
        found = true;
        return TileCenter(bx, by);
    }
}
