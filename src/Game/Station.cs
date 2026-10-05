using System.Collections.Generic;
using System.Linq;
using Godot;
using static ReactorRun.Config;

namespace ReactorRun;

/// <summary>Procedural station: a cell maze carved into a tile grid, plus everything placed in it.</summary>
public sealed class Station
{
    public sealed class Crate { public Rect2 Box; public int Value; public int Cell; public bool Got; }
    public sealed class Crew { public Rect2 Box; public int Cell; public bool Following; public int Face = 1; public float MsgT; }
    public sealed class Terminal { public Rect2 Box; public int Cell; public bool Used; }
    public sealed class Turret { public Vector2 P; public int Hp = 3; public float Cd, Ang = Mathf.Pi / 2f; public int Cell; }

    public readonly int Cols, Rows, N, TilesW, TilesH;
    public readonly byte[] Solid;
    public readonly HashSet<int>[] Links;
    public readonly int Start, Reactor;
    public readonly int[] DistFromReactor;
    public int D => DistFromReactor[Start];
    public readonly List<Crate> Crates = new();
    public readonly List<Crew> CrewList = new();
    public readonly List<Terminal> Terminals = new();
    public readonly List<Turret> Turrets = new();
    public readonly Rect2 Ship;
    public readonly Vector2 ReactorPos;
    public readonly Vector2[] WallSegments;
    public float MapW => TilesW * Tile;
    public float MapH => TilesH * Tile;

    readonly System.Random _r;
    float F() => (float)_r.NextDouble();
    float Range(float a, float b) => a + (b - a) * F();
    T Pick<T>(IList<T> l) => l[_r.Next(l.Count)];
    void Shuffle<T>(IList<T> l) { for (int i = l.Count - 1; i > 0; i--) { int j = _r.Next(i + 1); (l[i], l[j]) = (l[j], l[i]); } }

    /// <summary>Builds a station. Pass a seed for a reproducible layout (Daily Run).</summary>
    public Station(int tier, int? seed = null, bool final = false)
    {
        _r = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        (Cols, Rows) = StationSize(tier);
        N = Cols * Rows;
        Links = new HashSet<int>[N];
        for (int i = 0; i < N; i++) Links[i] = new HashSet<int>();
        Start = Idx(0, Rows - 1);

        // depth-first maze + a few extra loops
        var seen = new bool[N];
        var stack = new Stack<int>();
        stack.Push(Start); seen[Start] = true;
        while (stack.Count > 0)
        {
            int c = stack.Peek();
            var open = Neighbors(c).Where(j => !seen[j]).ToList();
            if (open.Count == 0) { stack.Pop(); continue; }
            int nxt = Pick(open);
            Link(c, nxt); seen[nxt] = true; stack.Push(nxt);
        }
        for (int k = 0; k < N / 6; k++)
        {
            int c = _r.Next(N);
            var o = Neighbors(c).Where(j => !Links[c].Contains(j)).ToList();
            if (o.Count > 0) Link(c, Pick(o));
        }
        var dS = Bfs(Start);
        Reactor = Start;
        for (int i = 0; i < N; i++) if (dS[i] > dS[Reactor]) Reactor = i;
        DistFromReactor = Bfs(Reactor);

        // carve tiles
        TilesW = Cols * CellW + 1; TilesH = Rows * CellH + 1;
        Solid = Enumerable.Repeat((byte)1, TilesW * TilesH).ToArray();
        for (int cy = 0; cy < Rows; cy++)
            for (int cx = 0; cx < Cols; cx++)
            {
                for (int y = cy * CellH + 1; y < cy * CellH + CellH; y++)
                    for (int x = cx * CellW + 1; x < cx * CellW + CellW; x++) Set(x, y, 0);
                int i = Idx(cx, cy);
                if (cx < Cols - 1 && Links[i].Contains(i + 1)) { int x = (cx + 1) * CellW; for (int y = cy * CellH + 4; y < cy * CellH + CellH; y++) Set(x, y, 0); }
                if (cy < Rows - 1 && Links[i].Contains(i + Cols)) { int y = (cy + 1) * CellH; for (int x = cx * CellW + 4; x < cx * CellW + 8; x++) Set(x, y, 0); }
            }
        // ledges
        for (int i = 0; i < N; i++)
        {
            if (i == Reactor || i == Start || F() > 0.6f) continue;
            int cx = i % Cols, cy = i / Cols, x0 = cx * CellW + (F() < 0.5f ? 1 : 8), y = cy * CellH + 5;
            for (int x = x0; x < x0 + 3; x++) Set(x, y, 1);
        }

        // contents
        var deadEnds = Enumerable.Range(0, N).Where(i => i != Start && i != Reactor && Links[i].Count == 1).ToList();
        int CrateVal() => Mathf.RoundToInt(Range(14f, 26f) + tier * 2);
        foreach (var c in deadEnds) Crates.Add(new Crate { Box = new Rect2(CellX(c) + 9 * Tile, FloorY(c) - 20, 24, 20), Value = CrateVal() + 8, Cell = c });
        for (int i = 0; i < N; i++)
            if (i != Start && i != Reactor && Links[i].Count > 1 && F() < 0.22f)
                Crates.Add(new Crate { Box = new Rect2(CellX(i) + 9 * Tile, FloorY(i) - 20, 24, 20), Value = CrateVal(), Cell = i });

        var others = Enumerable.Range(0, N).Where(i => i != Start && i != Reactor && !deadEnds.Contains(i)).ToList();
        var de = deadEnds.ToList(); Shuffle(de); Shuffle(others);
        int crewCount = final ? 4 : System.Math.Min(4, 1 + N / 7);
        var crewCells = de.Concat(others).Take(crewCount).ToList();
        foreach (var c in crewCells) CrewList.Add(new Crew { Box = new Rect2(CellX(c) + 2.5f * Tile, FloorY(c) - 22, 12, 22), Cell = c });

        var termPool = others.Where(c => !crewCells.Contains(c)).Concat(de.Where(c => !crewCells.Contains(c))).ToList();
        int termCount = N >= 20 ? 2 : 1;
        foreach (var c in termPool.Take(termCount))
            Terminals.Add(new Terminal { Box = new Rect2(CellX(c) + 6 * Tile - 14, CellY(c) + CellH * Tile / 2f - 4, 28, 34), Cell = c });

        float tc = TurretChance(tier) + (final ? 0.15f : 0f);
        for (int i = 0; i < N; i++)
        {
            if (i == Start || dS[i] < 2 || F() > tc) continue;
            float col = F() < 0.5f ? 2.5f : 9.5f;
            Turrets.Add(new Turret { P = new Vector2(CellX(i) + col * Tile, CellY(i) + Tile), Cd = Range(1f, 2f), Cell = i });
        }

        Ship = new Rect2(CellX(Start) + Tile + 6, FloorY(Start) - 40, 84, 40);
        ReactorPos = new Vector2(CellX(Reactor) + CellW * Tile / 2f, CellY(Reactor) + CellH * Tile / 2f + Tile / 2f);
        WallSegments = BuildWalls();
    }

    int Idx(int x, int y) => y * Cols + x;
    void Link(int a, int b) { Links[a].Add(b); Links[b].Add(a); }
    void Set(int x, int y, byte v) => Solid[y * TilesW + x] = v;

    IEnumerable<int> Neighbors(int i)
    {
        int x = i % Cols, y = i / Cols;
        if (x > 0) yield return i - 1;
        if (x < Cols - 1) yield return i + 1;
        if (y > 0) yield return i - Cols;
        if (y < Rows - 1) yield return i + Cols;
    }

    int[] Bfs(int s)
    {
        var d = Enumerable.Repeat(-1, N).ToArray();
        d[s] = 0;
        var q = new Queue<int>();
        q.Enqueue(s);
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            foreach (int j in Links[c]) if (d[j] < 0) { d[j] = d[c] + 1; q.Enqueue(j); }
        }
        return d;
    }

    public float CellX(int c) => c % Cols * CellW * Tile;
    public float CellY(int c) => c / Cols * CellH * Tile;
    public float FloorY(int c) => CellY(c) + CellH * Tile;

    public bool SolidAt(int tx, int ty) => tx < 0 || ty < 0 || tx >= TilesW || ty >= TilesH || Solid[ty * TilesW + tx] == 1;

    public int CellOf(Vector2 p)
    {
        int cx = Mathf.Clamp(Mathf.FloorToInt(p.X / (CellW * Tile)), 0, Cols - 1);
        int cy = Mathf.Clamp(Mathf.FloorToInt(p.Y / (CellH * Tile)), 0, Rows - 1);
        return cy * Cols + cx;
    }

    public bool LineOfSight(Vector2 a, Vector2 b)
    {
        float d = a.DistanceTo(b);
        int n = Mathf.CeilToInt(d / 10f);
        for (int i = 1; i < n; i++)
        {
            var p = a.Lerp(b, i / (float)n);
            if (SolidAt(Mathf.FloorToInt(p.X / Tile), Mathf.FloorToInt(p.Y / Tile))) return false;
        }
        return true;
    }

    Vector2[] BuildWalls()
    {
        var seg = new List<Vector2>();
        for (int y = 0; y < TilesH; y++)
            for (int x = 0; x < TilesW; x++)
            {
                if (Solid[y * TilesW + x] == 0) continue;
                bool Open(int tx, int ty) => tx >= 0 && ty >= 0 && tx < TilesW && ty < TilesH && Solid[ty * TilesW + tx] == 0;
                if (Open(x - 1, y)) { seg.Add(new Vector2(x * Tile, y * Tile)); seg.Add(new Vector2(x * Tile, (y + 1) * Tile)); }
                if (Open(x + 1, y)) { seg.Add(new Vector2((x + 1) * Tile, y * Tile)); seg.Add(new Vector2((x + 1) * Tile, (y + 1) * Tile)); }
                if (Open(x, y - 1)) { seg.Add(new Vector2(x * Tile, y * Tile)); seg.Add(new Vector2((x + 1) * Tile, y * Tile)); }
                if (Open(x, y + 1)) { seg.Add(new Vector2(x * Tile, (y + 1) * Tile)); seg.Add(new Vector2((x + 1) * Tile, (y + 1) * Tile)); }
            }
        return seg.ToArray();
    }
}
