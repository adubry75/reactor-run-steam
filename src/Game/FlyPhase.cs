using System;
using System.Collections.Generic;
using Godot;
using static ReactorRun.Config;

namespace ReactorRun;

/// <summary>Phase 1: shoot down enough fighters to drop the docking shield, then fly into the bay.</summary>
public sealed class FlyPhase
{
    enum Kind { Dart, Diver, Gunship }

    sealed class Enemy { public Kind Type; public int Hp; public Vector2 P; public float BaseY, Vx, Phase, Fire; public bool Dead; }
    struct Shot { public Vector2 P, V; public float Life; }

    static (int hp, float r, int val, float s0, float s1) Stats(Kind k) => k switch
    {
        Kind.Diver => (1, 18f, 6, 150f, 210f),
        Kind.Gunship => (3, 26f, 12, 60f, 90f),
        _ => (1, 18f, 5, 110f, 170f),
    };

    readonly Ctx _c;
    Vector2 _ship = new(140f, 350f), _vel;
    readonly List<Shot> _shots = new(), _enemyShots = new();
    readonly List<Enemy> _enemies = new();
    float _t, _cooldown, _spawnT = 1.2f, _zap, _docking, _camX, _scale = 1f;
    readonly int _need, _maxAlive;
    int _killed;
    bool _shieldWasUp = true;

    public bool Docked { get; private set; }

    /// <summary>Trailer autopilot: drifts forward, tracks the nearest fighter, fires constantly.</summary>
    public bool Auto;

    float AutoY()
    {
        Enemy best = null;
        foreach (var e in _enemies) if (e.P.X > _ship.X && (best == null || e.P.X < best.P.X)) best = e;
        if (best == null) return 0f;
        float d = best.P.Y - _ship.Y;
        return MathF.Abs(d) < 8f ? 0f : MathF.Sign(d);
    }

    /// <summary>Picks a spawn height that keeps new fighters out of the player's face.</summary>
    float SpawnY(float x)
    {
        float best = Rng.Range(90f, FlyWorldH - 90f), bd = new Vector2(x, best).DistanceTo(_ship);
        for (int i = 0; i < 8 && bd < 260f; i++)
        {
            float y = Rng.Range(90f, FlyWorldH - 90f), d = new Vector2(x, y).DistanceTo(_ship);
            if (d > bd) { best = y; bd = d; }
        }
        return best;
    }

    public FlyPhase(Ctx c)
    {
        _c = c;
        _need = c.Run.Final ? 25 : FightersNeeded(c.Tier);
        _maxAlive = c.Run.Final ? 8 : FightersAlive(c.Tier);
        if (c.Run.Final) { c.Vega.Say("finalStart", true); c.Fx.Banner("FINAL STATION", Hot, $"Destroy {_need} fighters. Then end this.", 3f); return; }
        c.Vega.Say("runStart", true);
        string sub = c.Save.HintOnce("fly") ? "That drops the docking shield. Space to fire." : c.Run.Daily ? "Daily Run" : $"Station {c.Run.Number} of {Stations}";
        c.Fx.Banner($"DESTROY {_need} FIGHTERS", Hot, sub, 2.4f);
    }

    public MusicState Music => _c.Run.Over ? MusicState.None : _docking > 0 ? MusicState.Explore : MusicState.Fight;

    Kind PickType()
    {
        var pool = new List<Kind> { Kind.Dart, Kind.Dart, Kind.Dart };
        int t = _c.Tier;
        if (t >= 1) { pool.Add(Kind.Diver); pool.Add(Kind.Diver); }
        if (t >= 3) pool.Add(Kind.Gunship);
        if (t >= 6) { pool.Add(Kind.Gunship); pool.Add(Kind.Diver); }
        return Rng.Pick(pool);
    }

    public void Update(float dt)
    {
        var run = _c.Run;
        _t += dt;
        _scale = MathF.Min(_c.Size.Y / FlyWorldH, _c.Size.X / 560f);
        float viewW = _c.Size.X / _scale;
        if (run.Over) { run.OverT += dt; return; }
        run.Invulnerable = MathF.Max(0f, run.Invulnerable - dt);
        _cooldown -= dt; _zap = MathF.Max(0f, _zap - dt);

        if (_docking > 0f)
        {
            _docking += dt;
            _ship.X += 260f * dt;
            _ship.Y += (350f - _ship.Y) * MathF.Min(1f, dt * 4f);
            if (_docking > 0.9f) Docked = true;
            return;
        }

        float tm = 1f + 0.08f * _c.Save.Level("thrust");
        float acc = ShipAccel * tm, max = ShipMaxSpeed * tm;
        float ix = Auto ? (_ship.X < 700f ? 0.35f : 0f) : GameInput.MoveX();
        float iy = Auto ? AutoY() : (GameInput.Held(GameInput.Down) ? 1f : 0f) - (GameInput.Held(GameInput.Up) ? 1f : 0f);
        _vel += new Vector2(ix, iy) * acc * dt;
        if (MathF.Abs(ix) < 0.1f) _vel.X -= _vel.X * MathF.Min(1f, dt * ShipDrag);
        if (MathF.Abs(iy) < 0.1f) _vel.Y -= _vel.Y * MathF.Min(1f, dt * ShipDrag);
        if (_vel.Length() > max) _vel = _vel.Normalized() * max;
        _ship += _vel * dt;
        _ship.Y = Mathf.Clamp(_ship.Y, 40f, FlyWorldH - 40f);
        _ship.X = MathF.Max(40f, _ship.X);
        if (ix > 0.1f && Rng.F() < 0.7f) _c.Fx.Trail(_ship + new Vector2(-14f, Rng.Range(-3f, 3f)), new Vector2(-Rng.Range(120f, 220f), Rng.Range(-20f, 20f)), Core, 0.25f, 6f);

        bool shieldUp = _killed < _need, inBay = _ship.Y > Bay0 + 14f && _ship.Y < Bay1 - 14f;
        if (_ship.X > StationX - 18f)
        {
            if (shieldUp)
            {
                _ship.X = StationX - 18f; _vel.X = -MathF.Abs(_vel.X) * 0.6f - 80f;
                if (_zap <= 0f) { _zap = 0.4f; _c.Sfx.Play("zap"); _c.Fx.Burst(new Vector2(StationX - 6f, _ship.Y), Hot, 10, 160f); _c.Fx.Float(_ship + new Vector2(0, -26), "SHIELD UP", Hot); }
            }
            else if (!inBay) { _ship.X = StationX - 18f; _vel.X = -MathF.Abs(_vel.X) * 0.4f; }
        }
        if (!shieldUp && inBay && _ship.X > StationX + 30f) { _docking = 0.01f; _c.Fx.Float(_ship + new Vector2(0, -26), "DOCKING", Line); }
        if (shieldUp != _shieldWasUp)
        {
            _shieldWasUp = shieldUp;
            if (!shieldUp) { _c.Sfx.Play("shield_down"); _c.Fx.Banner("SHIELD DOWN", Loot, "Fly into the docking bay", 2.4f); _c.Vega.Say("shieldDown", true); }
        }

        // player fire
        if ((Auto || GameInput.Held(GameInput.Fire)) && _cooldown <= 0f)
        {
            _shots.Add(new Shot { P = _ship + new Vector2(20f, 0f), V = new Vector2(ShipShotSpeed, 0f), Life = 2f });
            _cooldown = ShipShotCooldown; _c.Sfx.Play("shoot", 0.6f, 0.05f);
        }
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i]; s.P += s.V * dt; s.Life -= dt; _shots[i] = s;
            if (s.Life <= 0f || s.P.X > MathF.Min(StationX, _camX + viewW + 40f)) _shots.RemoveAt(i);
        }

        // enemies
        _spawnT -= dt;
        int remaining = _need - _killed;
        if (remaining > _enemies.Count && _enemies.Count < _maxAlive && _spawnT <= 0f)
        {
            var type = PickType(); var st = Stats(type);
            float x = MathF.Min(_camX + viewW + 60f, StationX - 50f), y = SpawnY(x);
            _enemies.Add(new Enemy { Type = type, Hp = st.hp, P = new Vector2(x, y), BaseY = y, Vx = -Rng.Range(st.s0, st.s1), Phase = Rng.Range(0f, 6f), Fire = Rng.Range(1f, 2.2f) });
            _spawnT = Rng.Range(0.6f, 1.4f) * Pace(_c.Tier);
            if (x < _camX + viewW) _c.Fx.Burst(new Vector2(x, y), Hot, 8, 120f, 0.4f);
        }
        foreach (var e in _enemies)
        {
            var st = Stats(e.Type);
            e.P.X += e.Vx * dt;
            if (e.Type == Kind.Diver) { e.BaseY += Mathf.Clamp(_ship.Y - e.BaseY, -1f, 1f) * 120f * dt; e.P.Y = e.BaseY + MathF.Sin(_t * 5f + e.Phase) * 14f; }
            else e.P.Y = e.BaseY + MathF.Sin(_t * (e.Type == Kind.Gunship ? 1f : 2f) + e.Phase) * (e.Type == Kind.Gunship ? 30f : 60f);
            if (e.P.X < _camX - 80f) { e.P.X = MathF.Min(_camX + viewW + 60f, StationX - 50f); e.BaseY = SpawnY(e.P.X); }
            e.Fire -= dt;
            if (e.Fire <= 0f && e.P.X < _camX + viewW && e.P.X > _camX)
            {
                float a = (_ship - e.P).Angle();
                foreach (var o in e.Type == Kind.Gunship ? new[] { -0.25f, 0f, 0.25f } : new[] { 0f })
                    _enemyShots.Add(new Shot { P = e.P, V = Vector2.FromAngle(a + o) * EnemyShotSpeed, Life = 4f });
                e.Fire = Rng.Range(1.4f, 2.6f) * Pace(_c.Tier) * (e.Type == Kind.Gunship ? 1.3f : 1f);
                _c.Sfx.Play("enemy_shot", 0.4f, 0.1f);
            }
            for (int i = _shots.Count - 1; i >= 0 && !e.Dead; i--)
            {
                if (_shots[i].P.DistanceTo(e.P) < st.r)
                {
                    _c.Fx.Burst(_shots[i].P, Hot, 5, 100f, 0.25f);
                    _shots.RemoveAt(i);
                    if (--e.Hp <= 0) e.Dead = true;
                }
            }
            if (!e.Dead && _c.Run.Invulnerable <= 0f && _ship.DistanceTo(e.P) < st.r + 6f) { e.Dead = true; _c.Hurt(_ship); }
            if (e.Dead)
            {
                _killed++; _c.Run.Salvage += st.val;
                _c.Ach.Stat("kills");
                if (e.Type == Kind.Gunship) _c.Ach.Unlock("gunship_down");
                _c.Fx.Burst(e.P, Hot, e.Type == Kind.Gunship ? 36 : 22, 260f);
                _c.Fx.Float(e.P + new Vector2(0, -18), "+" + st.val, Loot);
                _c.Fx.AddShake(e.Type == Kind.Gunship ? 9f : 5f);
                _c.Sfx.Play("pop", 0.8f, 0.1f);
            }
        }
        _enemies.RemoveAll(e => e.Dead);
        for (int i = _enemyShots.Count - 1; i >= 0; i--)
        {
            var s = _enemyShots[i]; s.P += s.V * dt; s.Life -= dt; _enemyShots[i] = s;
            if (s.P.DistanceTo(_ship) < 12f) { s.Life = 0f; _c.Hurt(_ship); }
            if (s.Life <= 0f) _enemyShots.RemoveAt(i);
        }
        _camX = Mathf.Clamp(_ship.X - viewW * 0.35f, 0f, MathF.Max(0f, FlyWorldW - viewW));
    }

    public static void DrawShip(CanvasItem ci, Vector2 at, float scale, bool facingLeft = false)
    {
        float f = facingLeft ? -scale : scale;
        var c = Neon(Line);
        ci.DrawPolyline(new[] { at + new Vector2(22, 0) * new Vector2(f, scale), at + new Vector2(-14, -12) * new Vector2(f, scale), at + new Vector2(-8, 0) * new Vector2(f, scale), at + new Vector2(-14, 12) * new Vector2(f, scale), at + new Vector2(22, 0) * new Vector2(f, scale) }, c, 2f, true);
        ci.DrawPolyline(new[] { at + new Vector2(6, -3) * new Vector2(f, scale), at + new Vector2(12, 0) * new Vector2(f, scale), at + new Vector2(6, 3) * new Vector2(f, scale) }, c, 2f, true);
    }

    public void Draw(CanvasItem ci)
    {
        var size = _c.Size; float sc = _scale;
        _c.Stars.Draw(ci, size, _camX * sc, 0f);
        var origin = new Vector2(-_camX * sc, (size.Y - FlyWorldH * sc) / 2f) + _c.Fx.ShakeOffset();
        ci.DrawSetTransform(origin, 0f, new Vector2(sc, sc));

        // station hull
        var hull = Neon(Line);
        ci.DrawPolyline(new Vector2[]
        {
            new(FlyWorldW + 50, 60), new(StationX + 120, 60), new(StationX + 40, 140), new(StationX, 160), new(StationX, Bay0), new(StationX + 160, Bay0),
            new(StationX + 160, Bay1), new(StationX, Bay1), new(StationX, FlyWorldH - 160), new(StationX + 40, FlyWorldH - 140), new(StationX + 120, FlyWorldH - 60), new(FlyWorldW + 50, FlyWorldH - 60),
        }, hull, 2f, true);
        var struts = new List<Vector2>();
        for (int i = 0; i < 6; i++)
        {
            float x = StationX + 60 + i * 60;
            struts.Add(new Vector2(x, 100 + i % 2 * 20)); struts.Add(new Vector2(x, Bay0 - 30));
            struts.Add(new Vector2(x, Bay1 + 30)); struts.Add(new Vector2(x, FlyWorldH - 100 - i % 2 * 20));
        }
        ci.DrawMultiline(struts.ToArray(), Alpha(Line, 0.4f), 1f);
        ReactorRun.Draw.Poly(ci, new Vector2(StationX + 300, FlyWorldH / 2f), 6, 26f + MathF.Sin(_c.Clock * 3f) * 3f, _c.Clock * 0.6f, Neon(Core), 2f);

        if (_killed < _need)
        {
            float a = 0.5f + MathF.Sin(_c.Clock * 14f) * 0.25f;
            for (float y = 60; y < FlyWorldH - 60; y += 18f)
            {
                float off = (_c.Clock * 60f) % 18f;
                ci.DrawLine(new Vector2(StationX - 8, y + off), new Vector2(StationX - 8, MathF.Min(FlyWorldH - 60, y + off + 10)), Alpha(Neon(Hot), a), 2f);
            }
        }

        foreach (var e in _enemies)
        {
            var p = e.P;
            if (e.Type == Kind.Gunship)
            {
                ci.DrawPolyline(new[] { p + new Vector2(-26, 0), p + new Vector2(-10, -14), p + new Vector2(18, -14), p + new Vector2(24, 0), p + new Vector2(18, 14), p + new Vector2(-10, 14), p + new Vector2(-26, 0) }, Neon(Hot), 2f, true);
                ci.DrawMultiline(new[] { p + new Vector2(-26, 0), p + new Vector2(-36, -6), p + new Vector2(-26, 0), p + new Vector2(-36, 6) }, Neon(Hot), 2f);
                for (int i = 0; i < 3; i++) ci.DrawRect(new Rect2(p + new Vector2(-6 + i * 8, -4), new Vector2(5, 8)), Alpha(Neon(Hot), i < e.Hp ? 1f : 0.25f), false, 1.5f);
            }
            else if (e.Type == Kind.Diver)
                ci.DrawPolyline(new[] { p + new Vector2(-18, 0), p + new Vector2(8, -10), p + new Vector2(2, 0), p + new Vector2(8, 10), p + new Vector2(-18, 0) }, Neon(Core), 2f, true);
            else
            {
                ci.DrawPolyline(new[] { p + new Vector2(-16, 0), p + new Vector2(0, -9), p + new Vector2(10, 0), p + new Vector2(0, 9), p + new Vector2(-16, 0) }, Neon(Hot), 2f, true);
                ci.DrawMultiline(new[] { p + new Vector2(2, -9), p + new Vector2(10, -16), p + new Vector2(2, 9), p + new Vector2(10, 16) }, Neon(Hot), 2f);
            }
        }
        foreach (var s in _shots) ci.DrawLine(s.P - new Vector2(12, 0), s.P, Neon(Line), 2f);
        foreach (var s in _enemyShots) ReactorRun.Draw.Ring(ci, s.P, 3f, Neon(Hot), 2f);
        var run = _c.Run;
        if (!run.Over && !(run.Invulnerable > 0f && (int)(_c.Clock * 20f) % 2 == 1)) DrawShip(ci, _ship, 1f);
        _c.Fx.DrawWorld(ci);
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    public void DrawHud(CanvasItem ci)
    {
        var size = _c.Size;
        int left = _need - _killed;
        if (left > 0)
        {
            ReactorRun.Draw.Text(ci, "DOCKING SHIELD UP · FIGHTERS DOWN", new Vector2(size.X / 2f, 24f), 14, Dim, HorizontalAlignment.Center);
            ReactorRun.Draw.Text(ci, $"{_killed} / {_need}", new Vector2(size.X / 2f, 56f), 26, Neon(Hot, 1.4f), HorizontalAlignment.Center);
            float pw = MathF.Min(14f, MathF.Min(size.X - 40f, 360f) / _need), x0 = size.X / 2f - pw * _need / 2f;
            for (int i = 0; i < _need; i++) ci.DrawRect(new Rect2(x0 + i * pw + 1f, 66f, pw - 3f, 4f), i < _killed ? Neon(Hot) : Alpha(Hot, 0.22f));
        }
        else ReactorRun.Draw.Text(ci, "SHIELD DOWN · FLY INTO THE DOCKING BAY", new Vector2(size.X / 2f, 30f), 15, Neon(Loot, 1.2f), HorizontalAlignment.Center);
    }
}
