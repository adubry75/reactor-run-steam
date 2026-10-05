using System;
using System.Collections.Generic;
using Godot;
using static ReactorRun.Config;

namespace ReactorRun;

/// <summary>Escape cinematic: the ship blasts off while the station's line art shatters. About 3 seconds, skippable.</summary>
public sealed class Outro
{
    sealed class Piece { public Vector2 C, Half, V; public float Rot, Vr; }

    static readonly Vector2[][] Polys =
    {
        new Vector2[] { new(430, 60), new(120, 60), new(40, 140), new(0, 160), new(0, 300), new(160, 300), new(160, 400), new(0, 400), new(0, 540), new(40, 560), new(120, 640), new(430, 640) },
        new Vector2[] { new(430, 60), new(430, 640) },
        new Vector2[] { new(60, 100), new(60, 270) }, new Vector2[] { new(120, 100), new(120, 270) }, new Vector2[] { new(180, 100), new(180, 270) },
        new Vector2[] { new(240, 100), new(240, 270) }, new Vector2[] { new(300, 100), new(300, 270) }, new Vector2[] { new(360, 100), new(360, 270) },
        new Vector2[] { new(60, 430), new(60, 600) }, new Vector2[] { new(120, 430), new(120, 600) }, new Vector2[] { new(180, 430), new(180, 600) },
        new Vector2[] { new(240, 430), new(240, 600) }, new Vector2[] { new(300, 430), new(300, 600) }, new Vector2[] { new(360, 430), new(360, 600) },
        new Vector2[] { new(200, 300), new(430, 300) }, new Vector2[] { new(200, 400), new(430, 400) },
    };
    static readonly Vector2 ReactorAt = new(300, 350);
    const float BoomAt = 0.75f, End = 2.9f;

    readonly Ctx _c;
    readonly List<Piece> _pieces = new();
    readonly float _left;
    float _t, _popT;
    bool _boomed;
    public bool Done { get; private set; }

    public Outro(Ctx c, float timeLeft)
    {
        _c = c; _left = timeLeft;
        c.Fx.Clear();
        foreach (var pl in Polys)
            for (int i = 0; i < pl.Length - 1; i++)
            {
                var a = pl[i]; var b = pl[i + 1];
                int n = Math.Max(1, Mathf.CeilToInt(a.DistanceTo(b) / 55f));
                for (int k = 0; k < n; k++)
                {
                    var p0 = a.Lerp(b, k / (float)n); var p1 = a.Lerp(b, (k + 1) / (float)n);
                    _pieces.Add(new Piece { C = (p0 + p1) / 2f, Half = (p1 - p0) / 2f });
                }
            }
        c.Vega.Say(timeLeft < 1f ? "escapeClose" : "escape", true);
    }

    public void Update(float dt, bool skip)
    {
        _t += dt;
        if (!_boomed)
        {
            _c.Fx.Shake = MathF.Min(14f, _t * 18f);
            _popT -= dt;
            if (_popT <= 0f) { _popT = 0.14f; var pc = Rng.Pick(_pieces); _c.Fx.Burst(pc.C, Rng.F() < 0.5f ? Core : Red, 6, 160f, 0.4f); _c.Sfx.Play("rumble", 0.4f); }
            if (_t > BoomAt)
            {
                _boomed = true; _c.Fx.Flash = 1f; _c.Fx.Shake = 26f;
                _c.Sfx.Play("boom"); _c.Sfx.Play("sting_win", 0.8f);
                foreach (var p in _pieces)
                {
                    var d = p.C - ReactorAt; float dist = MathF.Max(1f, d.Length());
                    float sp = Rng.Range(120f, 380f) * (1.2f - MathF.Min(1f, dist / 500f)) * 1.6f;
                    p.V = d / dist * sp; p.Vr = Rng.Range(-4f, 4f);
                }
                _c.Fx.Burst(ReactorAt, White, 30, 560f, 1f); _c.Fx.Burst(ReactorAt, Core, 26, 420f, 0.9f); _c.Fx.Burst(ReactorAt, Red, 20, 300f, 0.8f);
                _c.Fx.Banner("STATION DESTROYED", Loot, _left < 1f ? $"{_left:0.00}s to spare" : "", 2.2f);
            }
        }
        foreach (var p in _pieces) { p.C += p.V * dt; p.Rot += p.Vr * dt; p.V *= 0.995f; }
        if (_t > End || (_t > 0.4f && skip)) Done = true;
    }

    public void Draw(CanvasItem ci)
    {
        var size = _c.Size; float sc = MathF.Min(size.X / 900f, size.Y / 760f);
        _c.Stars.Draw(ci, size, _c.Clock * 160f, 0f);
        var origin = size / 2f + new Vector2(60f * sc, 0f) + _c.Fx.ShakeOffset() - new Vector2(215f, 350f) * sc;
        ci.DrawSetTransform(origin, 0f, new Vector2(sc, sc));
        if (_boomed)
        {
            float r = (_t - BoomAt) * 1100f, a = MathF.Max(0f, 1f - (_t - BoomAt) / 1.2f);
            if (a > 0f) { ReactorRun.Draw.Ring(ci, ReactorAt, r, Alpha(Neon(Core, 2f), a), 3f); ReactorRun.Draw.Ring(ci, ReactorAt, r * 0.7f, Alpha(Neon(White, 1.6f), a), 1.5f); }
        }
        float fade = _boomed ? MathF.Max(0f, 1f - (_t - BoomAt) / 2f) : 1f;
        var pts = new Vector2[_pieces.Count * 2];
        for (int i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i]; var h = p.Half.Rotated(p.Rot);
            pts[i * 2] = p.C - h; pts[i * 2 + 1] = p.C + h;
        }
        if (fade > 0f) ci.DrawMultiline(pts, Alpha(Neon(_boomed ? Core : Line, 1.8f), fade), 2f);
        if (!_boomed) StationPhase.DrawReactor(ci, ReactorAt, true, _c.Clock);
        float sx = 80f - 120f * _t - 420f * _t * _t;
        if (sx > -900f)
        {
            if (Rng.F() < 0.9f) _c.Fx.Trail(new Vector2(sx + 16f, 350f + Rng.Range(-3f, 3f)), new Vector2(Rng.Range(160f, 280f), Rng.Range(-20f, 20f)), Core, 0.3f, 8f);
            FlyPhase.DrawShip(ci, new Vector2(sx, 350f), 1.2f, facingLeft: true);
        }
        _c.Fx.DrawWorld(ci);
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        ReactorRun.Draw.Text(ci, "Press A / Enter to skip", new Vector2(size.X - 16f, size.Y - 16f), 12, Dim, HorizontalAlignment.Right);
    }
}
