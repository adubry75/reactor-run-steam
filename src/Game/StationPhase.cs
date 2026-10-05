using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using static ReactorRun.Config;

namespace ReactorRun;

/// <summary>Phase 2 and 3: explore the station, arm the reactor by touching it, then outrun the blast wave.</summary>
public sealed class StationPhase
{
    sealed class Drone { public Vector2 P, V; public int Hp = 2; public float Phase; }
    struct Shot { public Vector2 P, V; public float Life; }

    readonly Ctx _c;
    public readonly Station S;
    Rect2 _p;               // player box (position = top-left)
    Vector2 _v;
    int _face = 1;
    bool _onGround, _thrust, _empty, _reactorHinted, _warned, _saidLow;
    float _fuel, _sputT, _cd, _scale = 1f, _beepT;
    int _lastSec = -1;
    Vector2 _cam;
    readonly List<Shot> _shots = new(), _turretShots = new();
    readonly List<Drone> _drones = new();
    readonly List<Station.Crew> _carried = new();
    readonly bool[] _collapsed;
    bool _lock;
    float _alert, _alertMax, _droneT;
    string _prompt = "";

    public bool Armed { get; private set; }
    public float Fuse { get; private set; }
    public float ArmT { get; private set; }
    float _cl;

    /// <summary>Set when the player touches a relay terminal; Main shows the picker and calls ChooseMod.</summary>
    public List<Mod> PendingChoices { get; private set; }

    public IReadOnlyList<Station.Crew> Carried => _carried;

    public StationPhase(Ctx c)
    {
        _c = c;
        S = new Station(c.Tier, c.Run.Daily ? DailySeed() : null);
        _collapsed = new bool[S.N];
        _p = new Rect2(S.Ship.Position.X + S.Ship.Size.X + 12f, S.Ship.Position.Y + 40f - 23f, 14f, 22f);
        _fuel = MaxFuel;
        _cam = _p.GetCenter();
        _alertMax = AlertSeconds(S.N, c.Tier);
        c.Run.CrewInStation = S.CrewList.Count;
        c.Fx.Flash = 0.6f;
        c.Vega.Say("dock", true);
        bool first = c.Save.HintOnce("dock");
        c.Fx.Banner("FIND THE REACTOR", Core, first ? "Your jetpack refuels only when you land on something." : "", first ? 3.4f : 2f);
    }

    public static int DailySeed() => int.Parse(System.DateTime.Now.ToString("yyyyMMdd"));
    float _sinceDock;

    float MaxFuel => BaseFuel + FuelPerLevel * _c.Save.Level("fuel");
    int CrewCap => 1 + _c.Save.Level("crew");
    public float TimeLeft => Fuse - ArmT;

    public MusicState Music => _c.Run.Over ? MusicState.None : Armed ? MusicState.Countdown : _lock ? MusicState.Lockdown : MusicState.Explore;

    float FuseFor(bool oc) => Config.Fuse(S.D, _c.Save.Level("fuse"), oc, _c.Run.Has("overcore"));

    void Arm(bool overclock)
    {
        Armed = true; ArmT = 0f; Fuse = FuseFor(overclock);
        _c.Run.ArmedAfter = _sinceDock;
        if (_sinceDock < 30f) _c.Ach.Unlock("speed_demon");
        _c.Run.Mult = overclock ? 2 : 1; _c.Run.Overclocked = overclock;
        _c.Fx.Shake = 16f; _c.Fx.Flash = 0.5f;
        _c.Sfx.Play("arm");
        _c.Fx.Burst(S.ReactorPos, Core, 40, 300f, 1f);
        Lockdown();
        _c.Fx.Banner(overclock ? "OVERCLOCKED" : "REACTOR ARMED", overclock ? Hot : Core, overclock ? "x2 salvage. Get back to your ship!" : "Get back to your ship!", 2f);
        _c.Vega.Say(overclock ? "overclock" : "arm", true);
    }

    void Lockdown()
    {
        if (_lock) return;
        _lock = true; _droneT = 1.5f;
        _c.Sfx.Play("alarm", 0.7f);
        if (!Armed) { _c.Fx.Banner("LOCKDOWN", Hot, "Hunter drones incoming. They fly through walls.", 2.6f); _c.Vega.Say("lockdown", true); }
    }

    public void ChooseMod(int i)
    {
        if (PendingChoices == null || i < 0 || i >= PendingChoices.Count) return;
        var m = PendingChoices[i];
        var run = _c.Run;
        run.Mods.Add(m.Key); run.ModList.Add(m);
        PendingChoices = null;
        if (m.Key == "plating") { run.MaxShield++; run.Shield = run.MaxShield; }
        if (m.Key == "jammer" && !_lock) _alertMax += 30f;
        if (m.Key == "desperate") { run.Shield = Math.Max(1, run.Shield - 1); _c.Fx.AddShake(8f); _c.Sfx.Play("hurt"); }
        _c.Fx.Banner(m.Name.ToUpperInvariant(), m.Cursed ? Hot : Loot, m.Desc, 2.2f);
        _c.Sfx.Play("win");
        _c.Vega.Say(m.Cursed ? "cursed" : "mod", true);
    }

    void OpenTerminal(Station.Terminal t)
    {
        t.Used = true;
        var pool = Mods.Where(m => !_c.Run.Has(m.Key) && !(m.Key == "desperate" && _c.Run.Shield <= 1)).ToList();
        Rng.Shuffle(pool);
        var choices = pool.Take(3).ToList();
        if (choices.Count == 0) return;
        PendingChoices = choices;
        _c.Sfx.Play("pick");
    }

    void KillClock(Vector2 at)
    {
        if (Armed && _c.Run.Has("killclock")) { Fuse += 1.5f; _c.Fx.Float(at + new Vector2(0, -32), "+1.5s", Core); }
    }

    // ------------------------------------------------------------------ autotest hooks
    internal void DebugWarp(Vector2 center) { _p.Position = center - _p.Size / 2f; _v = Vector2.Zero; _cam = center; }
    internal void DebugOpenTerminal() { if (S.Terminals.Count > 0) OpenTerminal(S.Terminals[0]); }
    internal void DebugArm(bool overclock) { if (!Armed) Arm(overclock); }
    internal void DebugLockdown() => Lockdown();
    internal void DebugSetFuse(float remaining) => Fuse = ArmT + remaining;
    internal Vector2 PlayerCenter => _p.GetCenter();

    // trailer autopilot: follows waypoints, ignores collisions, never dies
    public List<Vector2> AutoPath;
    public float AutoSpeed = 230f;
    public bool NoDeath;
    int _autoIdx;

    public void SetAutoPath(List<Vector2> path, float seconds)
    {
        AutoPath = path; _autoIdx = 0;
        float len = 0f; var at = _p.GetCenter();
        foreach (var w in path) { len += at.DistanceTo(w); at = w; }
        AutoSpeed = len / MathF.Max(0.5f, seconds);
    }

    Vector2 Bottom(int c) => new(S.CellX(c) + 6 * Tile, S.CellY(c) + 7.5f * Tile);

    /// <summary>Waypoints through the maze from cell a to cell b (via doorways and shafts).</summary>
    public List<Vector2> PathBetween(int a, int b)
    {
        var prev = new int[S.N]; for (int i = 0; i < S.N; i++) prev[i] = -2;
        var q = new Queue<int>(); q.Enqueue(a); prev[a] = -1;
        while (q.Count > 0) { int c = q.Dequeue(); if (c == b) break; foreach (int j in S.Links[c]) if (prev[j] == -2) { prev[j] = c; q.Enqueue(j); } }
        var cells = new List<int>(); for (int c = b; c != -1; c = prev[c]) cells.Insert(0, c);
        var pts = new List<Vector2> { Bottom(cells[0]) };
        for (int i = 1; i < cells.Count; i++)
        {
            int p = cells[i - 1], c = cells[i];
            if (Math.Abs(c - p) == 1) pts.Add(new Vector2(S.CellX(Math.Max(p, c)) + Tile / 2f, S.CellY(c) + 7.5f * Tile));
            else pts.Add(new Vector2(S.CellX(c) + 6 * Tile, S.CellY(Math.Max(p, c)) + Tile / 2f));
            pts.Add(Bottom(c));
        }
        return pts;
    }

    public List<Vector2> PathToReactor() { var p = PathBetween(S.Start, S.Reactor); p.Add(S.ReactorPos); return p; }
    public List<Vector2> PathToShip() { var p = PathBetween(S.Reactor, S.Start); p.Add(S.Ship.GetCenter() + new Vector2(10, 0)); return p; }

    bool AutoMove(float dt)
    {
        if (AutoPath == null) return false;
        var c = _p.GetCenter();
        if (_autoIdx < AutoPath.Count)
        {
            var to = AutoPath[_autoIdx] - c; float d = to.Length(), step = AutoSpeed * dt;
            if (d <= step) { c = AutoPath[_autoIdx]; _autoIdx++; }
            else c += to / d * step;
            if (MathF.Abs(to.X) > 1f) _face = to.X > 0 ? 1 : -1;
            _thrust = to.Y < -4f;
            _v = d > 0.01f ? to / d * AutoSpeed : Vector2.Zero;
        }
        else _thrust = false;
        _p.Position = c - _p.Size / 2f;
        _onGround = false;
        return true;
    }

    // ------------------------------------------------------------------ update
    public void Update(float dt)
    {
        var run = _c.Run;
        _scale = MathF.Min(_c.Size.Y / ViewHeight, _c.Size.X / 600f);
        if (PendingChoices != null) return; // station frozen while choosing
        if (run.Over) { run.OverT += dt; return; }
        run.Invulnerable = MathF.Max(0f, run.Invulnerable - dt);
        _cd -= dt;
        _sinceDock += dt;

        // movement
        float tm = (1f + 0.08f * _c.Save.Level("thrust")) * (1f - (run.Has("harness") ? 0.04f : 0.08f) * _carried.Count) * (run.Has("afterburner") ? 1.15f : 1f);
        bool auto = AutoPath != null;
        float ix = auto ? 0f : GameInput.MoveX();
        if (MathF.Abs(ix) > 0.2f) _face = ix > 0 ? 1 : -1;
        _v.X += ix * RunAccel * tm * dt;
        if (MathF.Abs(ix) < 0.2f) _v.X -= _v.X * MathF.Min(1f, dt * (_onGround ? 12f : 2.5f));
        _v.X = Mathf.Clamp(_v.X, -RunMax * tm, RunMax * tm);
        _v.Y += Gravity * dt;
        bool wantUp = !auto && GameInput.Held(GameInput.Up);
        _thrust = wantUp && _fuel > 0f;
        if (_thrust) { _v.Y -= Thrust * tm * dt; if (!run.Has("desperate")) _fuel = MathF.Max(0f, _fuel - dt); }
        if (_onGround && !_thrust) _fuel = MathF.Min(MaxFuel, _fuel + dt * RefuelRate);
        else if (run.Has("recycler") && !_thrust) _fuel = MathF.Min(MaxFuel, _fuel + dt * 0.4f);
        if (_fuel <= 0f && wantUp && !_empty) { _empty = true; _c.Fx.Banner("OUT OF FUEL", Red, "Land on any surface to refuel", 1.8f); _c.Vega.Say("fuel"); }
        if (_fuel > 0.4f) _empty = false;
        _v.Y = Mathf.Clamp(_v.Y, -MaxRise * tm, MaxFall);
        if (!AutoMove(dt)) MoveBox(dt);
        var pc = _p.GetCenter();
        if (_thrust && Rng.F() < 0.7f) _c.Fx.Trail(new Vector2(pc.X - _face * 6f, _p.End.Y - 6f), new Vector2(Rng.Range(-30f, 30f), Rng.Range(150f, 240f)), Core);
        _sputT -= dt;
        if (wantUp && !_thrust && _sputT <= 0f) { _sputT = 0.35f; _c.Sfx.Play("sputter", 0.5f); _c.Fx.Trail(new Vector2(pc.X - _face * 6f, _p.End.Y - 6f), new Vector2(0, 60), Dim, 0.3f, 3f); }

        // shooting
        bool autoFire = auto && _drones.Exists(d => d.P.DistanceTo(pc) < 260f && MathF.Sign(d.P.X - pc.X) == _face);
        if ((autoFire || GameInput.Held(GameInput.Fire)) && _cd <= 0f)
        {
            foreach (float vy in run.Has("spread") ? new[] { -110f, 0f, 110f } : new[] { 0f })
                _shots.Add(new Shot { P = new Vector2(pc.X + _face * 10f, _p.Position.Y + 11f), V = new Vector2(_face * ShotSpeed, vy), Life = 1.2f });
            _cd = run.Has("rapid") ? 0.09f : ShotCooldown;
            run.StationShots++;
            _c.Sfx.Play("shoot", 0.5f, 0.05f);
        }
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i]; s.P += s.V * dt; s.Life -= dt;
            if (S.SolidAt(Mathf.FloorToInt(s.P.X / Tile), Mathf.FloorToInt(s.P.Y / Tile))) { s.Life = 0f; _c.Fx.Burst(s.P, Line, 4, 90f, 0.25f); }
            foreach (var t in S.Turrets)
            {
                if (s.Life <= 0f || t.Hp <= 0 || s.P.DistanceTo(t.P + new Vector2(0, 8)) >= 16f) continue;
                s.Life = 0f; t.Hp--; _c.Fx.Burst(s.P, Hot, 6, 120f, 0.3f);
                if (t.Hp <= 0)
                {
                    run.Salvage += 10; _c.Fx.Burst(t.P + new Vector2(0, 8), Hot, 26, 240f);
                    _c.Fx.Float(t.P + new Vector2(0, 30), "+10", Loot); _c.Sfx.Play("pop"); _c.Fx.AddShake(6f); KillClock(t.P + new Vector2(0, 30));
                    _c.Ach.Stat("turrets");
                }
            }
            foreach (var d in _drones)
            {
                if (s.Life <= 0f || d.Hp <= 0 || s.P.DistanceTo(d.P) >= 15f) continue;
                s.Life = 0f; d.Hp--; _c.Fx.Burst(s.P, Hot, 6, 120f, 0.3f);
                if (d.Hp <= 0) { run.Salvage += 8; _c.Fx.Float(d.P + new Vector2(0, -16), "+8", Loot); _c.Fx.Burst(d.P, Hot, 22, 240f); _c.Sfx.Play("pop"); KillClock(d.P); _c.Ach.Stat("drones"); }
            }
            if (s.Life <= 0f) _shots.RemoveAt(i); else _shots[i] = s;
        }

        // turrets
        foreach (var t in S.Turrets)
        {
            if (t.Hp <= 0 || _collapsed[t.Cell]) continue;
            var tp = t.P + new Vector2(0, 10);
            if (tp.DistanceTo(pc) < 400f && S.LineOfSight(tp, pc))
            {
                float a = (pc - tp).Angle();
                t.Ang += Mathf.AngleDifference(t.Ang, a) * MathF.Min(1f, dt * 5f);
                t.Cd -= dt;
                if (t.Cd <= 0f)
                {
                    _turretShots.Add(new Shot { P = tp + Vector2.FromAngle(t.Ang) * 14f, V = Vector2.FromAngle(t.Ang) * 210f, Life = 3f });
                    t.Cd = Rng.Range(1.3f, 2.1f) * Pace(_c.Tier) * (run.Has("jammer") ? 1.4f : 1f);
                    _c.Sfx.Play("enemy_shot", 0.4f, 0.1f);
                }
            }
            else t.Cd = MathF.Max(t.Cd, 0.6f);
        }
        for (int i = _turretShots.Count - 1; i >= 0; i--)
        {
            var s = _turretShots[i]; s.P += s.V * dt; s.Life -= dt;
            if (S.SolidAt(Mathf.FloorToInt(s.P.X / Tile), Mathf.FloorToInt(s.P.Y / Tile))) s.Life = 0f;
            else if (_p.Grow(2f).HasPoint(s.P)) { s.Life = 0f; _c.Hurt(pc); }
            if (s.Life <= 0f) _turretShots.RemoveAt(i); else _turretShots[i] = s;
        }

        // crates (+ magnet)
        foreach (var cr in S.Crates)
        {
            if (cr.Got) continue;
            if (run.Has("magnet") && cr.Box.GetCenter().DistanceTo(pc) < 130f)
                cr.Box.Position += (pc - cr.Box.GetCenter()) * MathF.Min(1f, dt * 5f);
            if (!cr.Box.Intersects(_p)) continue;
            cr.Got = true;
            if (run.Has("magnet")) cr.Value = Mathf.RoundToInt(cr.Value * 1.25f);
            run.Salvage += cr.Value;
            _c.Fx.Burst(cr.Box.GetCenter(), Loot, 18, 180f);
            _c.Fx.Float(cr.Box.Position + new Vector2(12, -10), "+" + cr.Value, Loot);
            _c.Sfx.Play("pick");
        }

        // crew
        foreach (var cr in S.CrewList)
        {
            cr.MsgT = MathF.Max(0f, cr.MsgT - dt);
            if (cr.Following || !cr.Box.Intersects(_p)) continue;
            if (_carried.Count < CrewCap)
            {
                cr.Following = true; _carried.Add(cr); _c.Sfx.Play("pick");
                _c.Fx.Float(cr.Box.Position + new Vector2(0, -16), $"CREW {_carried.Count}/{CrewCap} · SLOWER", Loot);
                _c.Vega.Say("crew");
            }
            else if (cr.MsgT <= 0f)
            {
                cr.MsgT = 2f; _c.Vega.Say("crewFull");
                if (_c.Save.HintOnce("crewfull")) _c.Fx.Banner("CREW BAY FULL", Hot, "Upgrade the Crew bay in the hangar for more slots", 3.2f);
                else _c.Fx.Float(cr.Box.Position + new Vector2(0, -16), "CREW BAY FULL", Hot);
            }
        }
        for (int i = 0; i < _carried.Count; i++)
        {
            var cr = _carried[i];
            var lead = i == 0 ? _p.Position : _carried[i - 1].Box.Position;
            float k = MathF.Min(1f, dt * 7f);
            cr.Face = _face;
            cr.Box.Position += (new Vector2(lead.X - _face * 16f, lead.Y) - cr.Box.Position) * k;
        }

        UpdateSecurity(dt, pc);
        if (run.Over) return;

        // terminals + reactor
        foreach (var t in S.Terminals)
            if (!t.Used && t.Box.Intersects(_p)) { OpenTerminal(t); return; }
        float dr = pc.DistanceTo(S.ReactorPos);
        _prompt = "";
        if (!Armed && dr < 260f && !_reactorHinted)
        {
            _reactorHinted = true;
            bool first = _c.Save.HintOnce("reactor");
            _c.Fx.Banner("REACTOR FOUND", Core, first ? "Touch it to arm it. Hold Q as you touch it to overclock for x2 salvage" : "", first ? 3.8f : 1.6f);
            _c.Vega.Say("reactorFound");
        }
        if (!Armed && dr < 170f) _prompt = "reactor";
        if (!Armed && dr < 40f) { _prompt = ""; Arm(GameInput.Held(GameInput.Overclock)); }
        if (!Armed && _p.Intersects(S.Ship)) _prompt = "ship";

        // countdown + blast wave
        if (Armed)
        {
            ArmT += dt;
            float left = TimeLeft;
            if (left < 5f && !_saidLow) { _saidLow = true; _c.Vega.Say("lowTime", true); }
            _beepT -= dt;
            if (_beepT <= 0f) { _c.Sfx.Play(left < 5f ? "tick_hi" : "beep", 0.5f); _beepT = left < 5f ? 0.25f : left < 10f ? 0.5f : 1f; }
            _cl = MathF.Max(0f, (ArmT - 2f) / (Fuse - 2f)) * (S.D + 0.5f);
            for (int i = 0; i < S.N; i++)
            {
                if (_collapsed[i] || _cl <= S.DistFromReactor[i] + 0.5f) continue;
                _collapsed[i] = true;
                var cc = new Vector2(S.CellX(i) + CellW * Tile / 2f, S.CellY(i) + CellH * Tile / 2f);
                _c.Fx.Burst(cc, Red, 30, 380f, 1f); _c.Fx.Burst(cc, Core, 16, 220f, 0.8f);
                if (cc.DistanceTo(pc) < 900f) { _c.Fx.AddShake(9f); _c.Sfx.Play("rumble", 0.6f); }
            }
            if (!NoDeath && _collapsed[S.CellOf(pc)]) { _c.Die("Caught in the blast wave. That corridor sealed before you got through.", pc); return; }
            if (left <= 0f && !NoDeath) { _c.Die("The reactor went critical with you still aboard.", pc); return; }
            if (_p.Intersects(S.Ship))
            {
                run.Over = true; run.Won = true; run.TimeLeft = left; run.OverT = 0f; run.CrewCarried = _carried.Count;
                _c.Sfx.Play("win"); _c.Fx.Flash = 0.7f; _c.Fx.Burst(S.Ship.GetCenter(), Line, 40, 300f, 1f);
            }
        }

        // camera
        float vw = _c.Size.X / _scale, vh = _c.Size.Y / _scale, ck = MathF.Min(1f, dt * 8f);
        _cam += (pc - _cam) * ck;
        _cam.X = S.MapW < vw ? S.MapW / 2f : Mathf.Clamp(_cam.X, vw / 2f, S.MapW - vw / 2f);
        _cam.Y = S.MapH < vh ? S.MapH / 2f : Mathf.Clamp(_cam.Y, vh / 2f, S.MapH - vh / 2f);
    }

    void UpdateSecurity(float dt, Vector2 pc)
    {
        if (!_lock)
        {
            _alert += dt;
            float left = _alertMax - _alert;
            if (left <= 10f && !_warned) { _warned = true; _c.Fx.Banner("SECURITY SWEEP IN 10s", Core, "Hunter drones are coming. Find the reactor!", 2.6f); }
            if (left <= 10f) { int s = Mathf.CeilToInt(left); if (s != _lastSec) { _lastSec = s; _c.Sfx.Play(s <= 3 ? "tick_hi" : "tick", 0.5f); } }
            if (_alert >= _alertMax) Lockdown();
        }
        if (_lock)
        {
            _droneT -= dt;
            if (_droneT <= 0f && _drones.Count < DronesAlive(_c.Tier))
            {
                int pcell = S.CellOf(pc), px = pcell % S.Cols, py = pcell / S.Cols;
                var pool = Enumerable.Range(0, S.N).Where(i => Math.Abs(i % S.Cols - px) + Math.Abs(i / S.Cols - py) >= 2 && !_collapsed[i]).ToList();
                if (pool.Count > 0)
                {
                    int cell = Rng.Pick(pool);
                    var at = new Vector2(S.CellX(cell) + CellW * Tile / 2f, S.CellY(cell) + CellH * Tile / 2f);
                    _drones.Add(new Drone { P = at, Phase = Rng.Range(0f, 6f) });
                    _c.Fx.Burst(at, Hot, 12, 140f, 0.5f);
                }
                _droneT = DroneInterval(_c.Tier);
            }
        }
        float spd = DroneSpeed(_c.Tier) * (_c.Run.Has("ghost") ? 0.65f : 1f);
        foreach (var d in _drones)
        {
            if (d.Hp <= 0) continue;
            var to = pc - d.P; float dist = to.Length();
            var want = dist > 0.01f ? to / dist * spd : Vector2.Zero;
            d.V += (want - d.V) * MathF.Min(1f, dt * 1.8f);
            d.P += d.V * dt;
            if (_collapsed[S.CellOf(d.P)]) { d.Hp = 0; _c.Fx.Burst(d.P, Red, 10, 140f, 0.4f); continue; }
            if (dist < 16f) { d.Hp = 0; _c.Fx.Burst(d.P, Hot, 16, 200f); _c.Hurt(pc); }
        }
        _drones.RemoveAll(d => d.Hp <= 0);
    }

    void MoveBox(float dt)
    {
        var pos = _p.Position; var sz = _p.Size;
        pos.X += _v.X * dt;
        int y0 = Mathf.FloorToInt(pos.Y / Tile), y1 = Mathf.FloorToInt((pos.Y + sz.Y - 0.01f) / Tile);
        if (_v.X > 0) { int tx = Mathf.FloorToInt((pos.X + sz.X) / Tile); for (int ty = y0; ty <= y1; ty++) if (S.SolidAt(tx, ty)) { pos.X = tx * Tile - sz.X - 0.01f; _v.X = 0; break; } }
        else if (_v.X < 0) { int tx = Mathf.FloorToInt(pos.X / Tile); for (int ty = y0; ty <= y1; ty++) if (S.SolidAt(tx, ty)) { pos.X = (tx + 1) * Tile + 0.01f; _v.X = 0; break; } }
        pos.Y += _v.Y * dt; _onGround = false;
        int x0 = Mathf.FloorToInt(pos.X / Tile), x1 = Mathf.FloorToInt((pos.X + sz.X - 0.01f) / Tile);
        if (_v.Y > 0) { int ty = Mathf.FloorToInt((pos.Y + sz.Y) / Tile); for (int tx = x0; tx <= x1; tx++) if (S.SolidAt(tx, ty)) { pos.Y = ty * Tile - sz.Y - 0.01f; _v.Y = 0; _onGround = true; break; } }
        else if (_v.Y < 0) { int ty = Mathf.FloorToInt(pos.Y / Tile); for (int tx = x0; tx <= x1; tx++) if (S.SolidAt(tx, ty)) { pos.Y = (ty + 1) * Tile + 0.01f; _v.Y = 0; break; } }
        _p.Position = pos;
    }

    // ------------------------------------------------------------------ drawing
    public static void DrawAstronaut(CanvasItem ci, Vector2 topLeft, float w, int face, Color col)
    {
        float cx = topLeft.X + w / 2f, y = topLeft.Y; var c = Neon(col, 1.6f);
        ReactorRun.Draw.Ring(ci, new Vector2(cx, y + 5f), 5f, c, 2f);
        ci.DrawMultiline(new[]
        {
            new Vector2(cx + face, y + 4), new Vector2(cx + face * 4, y + 4),
            new Vector2(cx, y + 10), new Vector2(cx, y + 16),
            new Vector2(cx, y + 16), new Vector2(cx - 4, y + 22), new Vector2(cx, y + 16), new Vector2(cx + 4, y + 22),
            new Vector2(cx, y + 12), new Vector2(cx + face * 9, y + 12),
        }, c, 2f);
        ci.DrawRect(new Rect2(cx - face * 4 - (face > 0 ? 4 : 0), y + 9, 4, 8), c, false, 2f);
    }

    public static void DrawReactor(CanvasItem ci, Vector2 at, bool armed, float clock)
    {
        var col = armed ? (MathF.Sin(clock * 22f) > 0 ? Neon(Red, 2f) : Neon(White, 1.6f)) : Neon(Core, 1.8f);
        for (int k = 0; k < 3; k++) ReactorRun.Draw.Poly(ci, at, 6, 20f + k * 13f, clock * (k % 2 == 1 ? -1f : 1f) * (armed ? 3f : 0.6f) + k, col, 2f);
        ReactorRun.Draw.Ring(ci, at, 7f + MathF.Sin(clock * (armed ? 18f : 3f)) * 3f, col, 2f);
    }

    public void Draw(CanvasItem ci)
    {
        var size = _c.Size; float sc = _scale; float clock = _c.Clock;
        _c.Stars.Draw(ci, size, _cam.X * sc * 0.4f, _cam.Y * sc * 0.4f);
        if (Armed) ci.DrawRect(new Rect2(Vector2.Zero, size), Alpha(Red, 0.05f + 0.05f * MathF.Sin(clock * 10f)));
        var origin = size / 2f - _cam * sc + _c.Fx.ShakeOffset();
        ci.DrawSetTransform(origin, 0f, new Vector2(sc, sc));

        // visible solid tiles (fill), then the glowing edges
        float vw = size.X / sc, vh = size.Y / sc;
        int tx0 = Math.Max(0, Mathf.FloorToInt((_cam.X - vw / 2f) / Tile) - 1), tx1 = Math.Min(S.TilesW - 1, Mathf.CeilToInt((_cam.X + vw / 2f) / Tile) + 1);
        int ty0 = Math.Max(0, Mathf.FloorToInt((_cam.Y - vh / 2f) / Tile) - 1), ty1 = Math.Min(S.TilesH - 1, Mathf.CeilToInt((_cam.Y + vh / 2f) / Tile) + 1);
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
                if (S.Solid[ty * S.TilesW + tx] == 1) ci.DrawRect(new Rect2(tx * Tile, ty * Tile, Tile, Tile), WallFill);
        ci.DrawMultiline(S.WallSegments, Neon(Line, 1.7f), 2f);

        // blast wave
        if (Armed)
            for (int i = 0; i < S.N; i++)
            {
                float s = _cl - S.DistFromReactor[i];
                if (s <= 0f) continue;
                var r = new Rect2(S.CellX(i), S.CellY(i), CellW * Tile, CellH * Tile);
                if (_collapsed[i])
                {
                    ci.DrawRect(r, Alpha(Red, 0.16f));
                    var cracks = new List<Vector2>();
                    for (int j = 0; j < 6; j++)
                    {
                        float yy = r.Position.Y + Rng.F() * r.Size.Y;
                        var a = new Vector2(r.Position.X + Rng.F() * r.Size.X * 0.3f, yy);
                        var b = new Vector2(r.Position.X + Rng.Range(0.3f, 0.6f) * r.Size.X, yy + Rng.Range(-30f, 30f));
                        var c2 = new Vector2(r.Position.X + Rng.Range(0.6f, 1f) * r.Size.X, yy + Rng.Range(-30f, 30f));
                        cracks.Add(a); cracks.Add(b); cracks.Add(b); cracks.Add(c2);
                    }
                    ci.DrawMultiline(cracks.ToArray(), Alpha(Neon(Red, 1.6f), 0.8f), 1.5f);
                }
                else if ((int)(clock * 8f) % 2 == 0) ci.DrawRect(r.Grow(-Tile - 4f), Alpha(Neon(Red, 1.6f), 0.8f), false, 3f);
            }

        // ship
        var sh = S.Ship.Position; var shc = Neon(Line);
        ci.DrawPolyline(new[] { sh + new Vector2(4, 40), sh + new Vector2(14, 24), sh + new Vector2(30, 6), sh + new Vector2(62, 6), sh + new Vector2(80, 24), sh + new Vector2(72, 40) }, shc, 2f, true);
        ci.DrawMultiline(new[] { sh + new Vector2(14, 24), sh + new Vector2(80, 24), sh + new Vector2(40, 6), sh + new Vector2(46, -6) }, shc, 2f);
        if (Armed) ci.DrawRect(new Rect2(sh + new Vector2(-6, -12), S.Ship.Size + new Vector2(12, 14)), Alpha(Neon(Loot), 0.5f + 0.5f * MathF.Sin(clock * 8f)), false, 2f);

        foreach (var cr in S.Crates)
        {
            if (cr.Got) continue;
            var b = cr.Box; var c = Neon(Loot);
            ci.DrawRect(b, c, false, 2f);
            ci.DrawMultiline(new[] { b.Position, b.End, new Vector2(b.End.X, b.Position.Y), new Vector2(b.Position.X, b.End.Y) }, c, 2f);
        }
        foreach (var t in S.Terminals)
        {
            float bob = t.Used ? 0f : MathF.Sin(clock * 3f + t.Cell) * 3f;
            var c = t.Used ? Alpha(Dim, 0.4f) : Neon(Loot);
            var p = t.Box.Position + new Vector2(0, bob);
            ci.DrawRect(new Rect2(p + new Vector2(2, 6), new Vector2(24, 18)), c, false, 2f);
            ci.DrawMultiline(new[] { p + new Vector2(14, 24), p + new Vector2(14, 34), p + new Vector2(6, 34), p + new Vector2(22, 34) }, c, 2f);
            if (!t.Used) ReactorRun.Draw.Text(ci, "RELAY", p + new Vector2(14, -4), 12, Loot, HorizontalAlignment.Center);
        }
        foreach (var t in S.Turrets)
        {
            if (t.Hp <= 0) continue;
            var c = _collapsed[t.Cell] ? Dim : Neon(Hot);
            ci.DrawPolyline(new[] { t.P + new Vector2(-12, 0), t.P + new Vector2(-8, 10), t.P + new Vector2(8, 10), t.P + new Vector2(12, 0) }, c, 2f, true);
            ReactorRun.Draw.Ring(ci, t.P + new Vector2(0, 10), 6f, c, 2f);
            ci.DrawLine(t.P + new Vector2(0, 10), t.P + new Vector2(0, 10) + Vector2.FromAngle(t.Ang) * 16f, c, 2f);
        }
        DrawReactor(ci, S.ReactorPos, Armed, clock);
        foreach (var cr in S.CrewList)
        {
            if (cr.Following && _c.Run.Over && !_c.Run.Won) continue;
            DrawAstronaut(ci, cr.Box.Position, 12f, cr.Face, Loot);
            if (!cr.Following) ReactorRun.Draw.Text(ci, (int)(clock * 3f + cr.Cell) % 2 == 1 ? "HELP" : "!", cr.Box.Position + new Vector2(6, -10), 16, Loot, HorizontalAlignment.Center);
        }
        foreach (var d in _drones)
        {
            ReactorRun.Draw.Poly(ci, d.P, 5, 11f, clock * 4f + d.Phase, Alpha(Neon(Hot), 0.85f), 2f);
            ReactorRun.Draw.Ring(ci, d.P, 3f + MathF.Sin(clock * 12f + d.Phase), Neon(Hot), 2f);
            for (int k = 0; k < 8; k++) ci.DrawArc(d.P, 17f, k * Mathf.Tau / 8f, k * Mathf.Tau / 8f + 0.35f, 4, Alpha(Neon(Hot), 0.7f), 1.5f, true);
        }
        foreach (var s in _shots) ci.DrawLine(s.P - s.V.Normalized() * 10f, s.P, Neon(Line), 2f);
        foreach (var s in _turretShots) ReactorRun.Draw.Ring(ci, s.P, 3f, Neon(Hot), 2f);

        var run = _c.Run; var pc = _p.GetCenter();
        if ((!run.Over || run.Won) && !(run.Invulnerable > 0f && (int)(clock * 20f) % 2 == 1)) DrawAstronaut(ci, _p.Position, 14f, _face, Ink);
        if (!run.Over && !run.Has("desperate") && _fuel / MaxFuel < 0.25f && (int)(clock * 5f) % 2 == 0)
            ReactorRun.Draw.Text(ci, _fuel <= 0f ? "NO FUEL" : "LOW FUEL", new Vector2(pc.X, _p.Position.Y - 12f), 12, _fuel <= 0f ? Red : Core, HorizontalAlignment.Center);

        // scanner arrow
        int scan = _c.Save.Level("scan");
        if (!run.Over && (Armed ? scan >= 1 : scan >= 2))
        {
            var target = Armed ? S.Ship.GetCenter() : S.ReactorPos;
            if (target.DistanceTo(pc) > 180f)
            {
                float a = (target - pc).Angle();
                var at = pc + Vector2.FromAngle(a) * 42f;
                var col = Neon(Armed ? Loot : Core);
                ci.DrawPolyline(new[] { at + new Vector2(-5, -7).Rotated(a), at + new Vector2(4, 0).Rotated(a), at + new Vector2(-5, 7).Rotated(a) }, col, 2f, true);
            }
        }
        _c.Fx.DrawWorld(ci);
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    public void DrawHud(CanvasItem ci, bool coarse)
    {
        var size = _c.Size; var run = _c.Run; float clock = _c.Clock;
        float cx = size.X / 2f;
        if (Armed && !(run.Over && !run.Won))
        {
            float left = MathF.Max(0f, run.Over ? run.TimeLeft : TimeLeft);
            bool urgent = left < 5f;
            ReactorRun.Draw.Text(ci, left.ToString("0.0"), new Vector2(cx, 62f), urgent ? 46 : 38, urgent && (int)(clock * 8f) % 2 == 1 ? Neon(White, 1.4f) : Neon(Red, 1.6f), HorizontalAlignment.Center);
            ReactorRun.Draw.Text(ci, run.Over ? "ESCAPED" : "GET BACK TO YOUR SHIP", new Vector2(cx, 84f), 14, Ink, HorizontalAlignment.Center);
        }
        else if (!Armed)
        {
            if (_lock)
            {
                ReactorRun.Draw.Text(ci, "FIND THE REACTOR · HUNTER DRONES ACTIVE", new Vector2(cx, 24f), 14, Dim, HorizontalAlignment.Center);
                ReactorRun.Draw.Text(ci, "LOCKDOWN", new Vector2(cx, 58f), 28, (int)(clock * 4f) % 2 == 1 ? Neon(Hot, 1.5f) : Neon(Red, 1.5f), HorizontalAlignment.Center);
            }
            else
            {
                float left = MathF.Max(0f, _alertMax - _alert), fr = left / _alertMax;
                var col = left < 10f ? ((int)(clock * 6f) % 2 == 1 ? White : Hot) : left < 20f ? Core : Line;
                ReactorRun.Draw.Text(ci, "FIND THE REACTOR · SECURITY SWEEP IN", new Vector2(cx, 24f), 14, Dim, HorizontalAlignment.Center);
                ReactorRun.Draw.Text(ci, $"{(int)left / 60}:{(int)left % 60:00}", new Vector2(cx, 58f), left < 10f ? 32 : 28, Neon(col, 1.4f), HorizontalAlignment.Center);
                float bw = MathF.Min(260f, size.X - 180f);
                ci.DrawRect(new Rect2(cx - bw / 2f, 68f, bw, 4f), Alpha(Line, 0.15f));
                ci.DrawRect(new Rect2(cx - bw / 2f, 68f, bw * fr, 4f), Neon(col, 1.4f));
            }
        }
        // fuel gauge
        float fr2 = _fuel / MaxFuel;
        ci.DrawRect(new Rect2(16.5f, 42.5f, 96f, 6f), Dim, false, 1f);
        ci.DrawRect(new Rect2(17f, 43f, 95f * fr2, 5f), fr2 < 0.25f ? Neon(Red, 1.4f) : Neon(Core, 1.4f));
        ReactorRun.Draw.Text(ci, "FUEL", new Vector2(120f, 49f), 11, Dim);
        // crew + power-ups
        ReactorRun.Draw.Text(ci, $"CREW {_carried.Count}/{CrewCap} · {S.CrewList.Count} ABOARD STATION", new Vector2(size.X - 16f, 50f), 14, _carried.Count > 0 ? Loot : Dim, HorizontalAlignment.Right);
        for (int i = 0; i < run.ModList.Count; i++)
            ReactorRun.Draw.Text(ci, "+ " + run.ModList[i].Name, new Vector2(16f, 92f + i * 16f), 12, run.ModList[i].Cursed ? Hot : Loot);
        if (_c.Save.Level("scan") >= 3) DrawMinimap(ci);
        if (_prompt.Length > 0 && !run.Over)
        {
            float y = size.Y - (coarse ? 96f : 32f);
            if (_prompt == "reactor")
            {
                ReactorRun.Draw.Text(ci, $"TOUCH THE CORE TO ARM IT · {FuseFor(false):0.0}s fuse", new Vector2(cx, y - 22f), 15, Core, HorizontalAlignment.Center);
                ReactorRun.Draw.Text(ci, $"HOLD Q / Y AS YOU TOUCH TO OVERCLOCK · {FuseFor(true):0.0}s · x2 salvage", new Vector2(cx, y), 15, Hot, HorizontalAlignment.Center);
            }
            else ReactorRun.Draw.Text(ci, "Arm the reactor before you leave", new Vector2(cx, y), 15, Dim, HorizontalAlignment.Center);
        }
    }

    void DrawMinimap(CanvasItem ci)
    {
        var size = _c.Size;
        float cs = Mathf.Clamp(Mathf.Floor(150f / S.Cols), 6f, 14f), x0 = size.X - 16f - S.Cols * cs, y0 = 64f;
        ci.DrawRect(new Rect2(x0 - 6f, y0 - 6f, S.Cols * cs + 12f, S.Rows * cs + 12f), new Color(0.016f, 0.03f, 0.063f, 0.75f));
        Vector2 Center(int i) => new(x0 + i % S.Cols * cs + cs / 2f, y0 + i / S.Cols * cs + cs / 2f);
        var links = new List<Vector2>();
        for (int i = 0; i < S.N; i++) foreach (int j in S.Links[i]) if (j > i) { links.Add(Center(i)); links.Add(Center(j)); }
        if (links.Count > 0) ci.DrawMultiline(links.ToArray(), Alpha(Line, 0.7f), 1.5f);
        for (int i = 0; i < S.N; i++) if (_collapsed[i]) ci.DrawRect(new Rect2(Center(i) - new Vector2(cs / 2f, cs / 2f), new Vector2(cs, cs)), Alpha(Red, 0.5f));
        ci.DrawCircle(Center(S.Start), 3f, Line);
        ci.DrawCircle(Center(S.Reactor), 3.5f, Core);
        foreach (var cr in S.CrewList) if (!cr.Following) ci.DrawCircle(Center(cr.Cell), 2.5f, Loot);
        foreach (var t in S.Terminals) if (!t.Used) ci.DrawRect(new Rect2(Center(t.Cell) - new Vector2(3, 3), new Vector2(6, 6)), Loot, false, 1f);
        if ((int)(_c.Clock * 4f) % 2 == 1) ci.DrawCircle(Center(S.CellOf(_p.GetCenter())), 2.5f, White);
    }
}
