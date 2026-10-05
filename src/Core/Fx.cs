using System;
using System.Collections.Generic;
using Godot;

namespace ReactorRun;

/// <summary>Neon drawing helpers. All world/HUD drawing goes through these so glow intensity stays consistent.</summary>
public static class Draw
{
    public static Font Font => ThemeDB.FallbackFont;

    public static void Text(CanvasItem ci, string text, Vector2 pos, int size, Color col, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        float w = Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        float x = align switch
        {
            HorizontalAlignment.Center => pos.X - w / 2f,
            HorizontalAlignment.Right => pos.X - w,
            _ => pos.X,
        };
        ci.DrawString(Font, new Vector2(x, pos.Y), text, HorizontalAlignment.Left, -1, size, col);
    }

    public static float TextWidth(string text, int size) => Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    public static void Line(CanvasItem ci, Vector2 a, Vector2 b, Color c, float w = 2f) => ci.DrawLine(a, b, c, w, true);

    public static void Poly(CanvasItem ci, Vector2 center, int sides, float r, float rot, Color c, float w = 2f)
    {
        var pts = new Vector2[sides + 1];
        for (int i = 0; i <= sides; i++)
        {
            float a = rot + i / (float)sides * Mathf.Tau;
            pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        ci.DrawPolyline(pts, c, w, true);
    }

    public static void Ring(CanvasItem ci, Vector2 center, float r, Color c, float w = 2f) => ci.DrawArc(center, r, 0f, Mathf.Tau, 32, c, w, true);

    public static void Box(CanvasItem ci, Rect2 r, Color c, float w = 2f) => ci.DrawRect(r, c, false, w);

    /// <summary>Wraps text into lines that fit maxWidth at the given size.</summary>
    public static List<string> Wrap(string text, float maxWidth, int size)
    {
        var lines = new List<string>();
        var cur = "";
        foreach (var word in text.Split(' '))
        {
            var test = cur.Length == 0 ? word : cur + " " + word;
            if (TextWidth(test, size) > maxWidth && cur.Length > 0) { lines.Add(cur); cur = word; }
            else cur = test;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }
}

/// <summary>Particles, floating text, center banners, screen shake/flash and VEGA comms.</summary>
public sealed class Fx
{
    struct Spark { public Vector2 P, V; public float Life, Max, Len; public Color C; }
    struct Floater { public Vector2 P; public string Text; public Color C; public float Life; }

    readonly List<Spark> _sparks = new();
    readonly List<Floater> _floaters = new();
    public float Shake, Flash;
    public bool ShakeEnabled = true;

    // banner
    string _bannerText, _bannerSub;
    Color _bannerCol;
    float _bannerT, _bannerDur;

    public void Burst(Vector2 at, Color c, int n = 16, float speed = 220f, float life = 0.7f)
    {
        for (int i = 0; i < n; i++)
        {
            float a = Rng.F() * Mathf.Tau, s = speed * (0.3f + Rng.F());
            _sparks.Add(new Spark { P = at, V = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s, Life = life * (0.5f + 0.5f * Rng.F()), Max = life, Len = 3f + Rng.F() * 7f, C = c });
        }
        if (_sparks.Count > 500) _sparks.RemoveRange(0, _sparks.Count - 500);
    }

    public void Trail(Vector2 at, Vector2 v, Color c, float life = 0.22f, float len = 5f) =>
        _sparks.Add(new Spark { P = at, V = v, Life = life, Max = life, Len = len, C = c });

    public void Float(Vector2 at, string text, Color c) => _floaters.Add(new Floater { P = at, Text = text, C = c, Life = 1.3f });

    public void Banner(string text, Color c, string sub = "", float dur = 2.6f)
    {
        _bannerText = text; _bannerCol = c; _bannerSub = sub; _bannerT = 0f; _bannerDur = dur;
    }

    public void AddShake(float amount) => Shake = MathF.Max(Shake, amount);

    public void Clear() { _sparks.Clear(); _floaters.Clear(); _bannerText = null; }

    public void Update(float dt)
    {
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var s = _sparks[i];
            s.P += s.V * dt; s.V *= 0.97f; s.Life -= dt;
            if (s.Life <= 0f) _sparks.RemoveAt(i); else _sparks[i] = s;
        }
        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            var f = _floaters[i];
            f.P.Y -= 30f * dt; f.Life -= dt;
            if (f.Life <= 0f) _floaters.RemoveAt(i); else _floaters[i] = f;
        }
        Shake = MathF.Max(0f, Shake - dt * 30f);
        Flash = MathF.Max(0f, Flash - dt * 2.5f);
        if (_bannerText != null) { _bannerT += dt; if (_bannerT > _bannerDur) _bannerText = null; }
    }

    public Vector2 ShakeOffset() => ShakeEnabled && Shake > 0 ? new Vector2(Rng.Range(-Shake, Shake), Rng.Range(-Shake, Shake)) : Vector2.Zero;

    /// <summary>Draws sparks and floating text in whatever transform is current (world space).</summary>
    public void DrawWorld(CanvasItem ci)
    {
        // batch by color + alpha band into a few DrawMultiline calls
        var groups = new Dictionary<(Color, int), List<Vector2>>();
        foreach (var s in _sparks)
        {
            int band = Math.Min(4, (int)(s.Life / s.Max * 5f));
            var key = (s.C, band);
            if (!groups.TryGetValue(key, out var pts)) groups[key] = pts = new List<Vector2>();
            var dir = s.V.LengthSquared() > 0.01f ? s.V.Normalized() : Vector2.Right;
            pts.Add(s.P);
            pts.Add(s.P - dir * s.Len);
        }
        foreach (var ((c, band), pts) in groups)
            ci.DrawMultiline(pts.ToArray(), Config.Alpha(Config.Neon(c, 1.6f), (band + 1) / 5f), 2f);
        foreach (var f in _floaters)
            Draw.Text(ci, f.Text, f.P, 14, Config.Alpha(f.C, Math.Min(1f, f.Life)), HorizontalAlignment.Center);
    }

    /// <summary>Center-screen banner + white flash, in screen space.</summary>
    public void DrawScreen(CanvasItem ci, Vector2 size)
    {
        if (_bannerText != null)
        {
            float a = MathF.Min(1f, MathF.Min(_bannerT * 6f, (_bannerDur - _bannerT) * 2f));
            float y = size.Y * 0.36f;
            int fs = (int)MathF.Min(30f, size.X / 16f);
            float tw = MathF.Max(Draw.TextWidth(_bannerText, fs), string.IsNullOrEmpty(_bannerSub) ? 0f : Draw.TextWidth(_bannerSub, 15));
            float h = string.IsNullOrEmpty(_bannerSub) ? 56f : 78f;
            var r = new Rect2(size.X / 2f - tw / 2f - 24f, y - 40f, tw + 48f, h);
            ci.DrawRect(r, new Color(0.016f, 0.02f, 0.043f, 0.75f * a));
            Draw.Box(ci, r, Config.Alpha(Config.Neon(_bannerCol, 1.4f), a), 1.5f);
            Draw.Text(ci, _bannerText, new Vector2(size.X / 2f, y), fs, Config.Alpha(Config.Neon(_bannerCol, 1.3f), a), HorizontalAlignment.Center);
            if (!string.IsNullOrEmpty(_bannerSub))
                Draw.Text(ci, _bannerSub, new Vector2(size.X / 2f, y + 26f), 15, Config.Alpha(Config.Ink, a), HorizontalAlignment.Center);
        }
        if (Flash > 0f) ci.DrawRect(new Rect2(Vector2.Zero, size), new Color(1, 1, 1, MathF.Min(0.8f, Flash)));
    }
}

/// <summary>VEGA, the ship AI: short typed-out comm lines reacting to events.</summary>
public sealed class Vega
{
    static readonly Dictionary<string, string[]> Lines = new()
    {
        ["runStart"] = new[] { "Station ahead. Try not to die, I just finished calibrating you.", "Another reactor, another terrible idea. Love it.", "Fighters inbound. Shoot the pointy ones." },
        ["shieldDown"] = new[] { "Shield's down. Door's open. Manners optional.", "Bay is clear. Park it, hotshot." },
        ["dock"] = new[] { "Docked. Find the reactor. I'll keep the engine warm.", "Smells like trouble in there. And ozone.", "Security never sleeps. Neither do I. Move." },
        ["crew"] = new[] { "Survivor aboard. They say thanks. I say hurry.", "Another passenger. I'm charging them for snacks.", "Rescue logged. Your hero rating went up 2%." },
        ["crewFull"] = new[] { "Bay's full. Leave them a nice note.", "No room. Upgrade the crew bay, hero." },
        ["reactorFound"] = new[] { "That's the core. Big, glowy, extremely angry.", "Reactor in sight. Do the thing." },
        ["arm"] = new[] { "Fuse is lit. RUN.", "Oh, we're doing this. Go go go!", "Countdown started. My circuits are sweating." },
        ["overclock"] = new[] { "Overclocked?! You absolute legend. Or idiot. RUN.", "Double salvage, half the time. Respect. RUN." },
        ["lockdown"] = new[] { "Security's awake. Drones go through walls. You don't.", "Lockdown. Should've hurried." },
        ["lowShield"] = new[] { "Hull integrity is... let's call it vibes.", "One more hit and I'm writing your eulogy." },
        ["lowTime"] = new[] { "FIVE SECONDS. NO PRESSURE.", "Hatch is open! Legs are not optional!" },
        ["cursed"] = new[] { "Cursed tech. I love it. I hate it. I love it.", "That module is screaming. Totally normal." },
        ["mod"] = new[] { "Ooh, shiny. Install complete.", "Upgrade accepted. You're 12% cooler." },
        ["fuel"] = new[] { "Tank's dry. Find a floor.", "No fuel. Gravity wins this round." },
        ["finalStart"] = new[] { "This is it. The last reactor in the sector. Make it loud.", "Final station. Everything they have is out there. So are we." },
        ["won"] = new[] { "That's the last one. The sector's free. I'm... proud of you?", "Campaign complete. I'm framing this telemetry." },
        ["gameOver"] = new[] { "No ships left. I'll file the report. It's a short one.", "That's the campaign. We'll get them next time, pilot." },
        ["shipLost"] = new[] { "We lost the ship. Luckily we have spares. Fewer now.", "Ship's gone. You're not. Let's not make a habit of it." },
        ["escapeClose"] = new[] { "Point. Three. Seconds. I need a minute.", "I aged a decade. Do it again.", "That wasn't a margin. That was a rounding error." },
        ["escape"] = new[] { "Clean exit. The station didn't make it.", "And that's how you leave a party.", "Boom. Salvage secured." },
        ["death"] = new[] { "Signal lost. Recovering what I can.", "Salvage drone is sweeping for scraps. Again.", "Well. That was educational." },
        ["buy"] = new[] { "Upgrade installed. I'd hug you if I had arms.", "Shiny. Try not to scratch it.", "Installed. Your odds went from bad to less bad." },
        ["hangar"] = new[] { "Hangar's quiet. Too quiet. Let's go blow something up.", "Crew is restless. So am I.", "Ready when you are, pilot." },
        ["poor"] = new[] { "Salvage is thin. Greed is a virtue out there.", "We're broke. Grab a crate or two next time." },
    };

    string _text;
    float _t, _cooldown;
    int _shown;
    const float Life = 4.4f, CharsPerSec = 48f;

    public static string Pick(string key) => Rng.Pick(Lines[key]);

    public void Say(string key, bool force = false)
    {
        if (!Lines.ContainsKey(key) || (!force && _cooldown > 0f)) return;
        _text = Pick(key); _t = 0f; _shown = 0; _cooldown = 3f;
    }

    public void Clear() => _text = null;

    public void Update(float dt, Sfx sfx)
    {
        _cooldown = MathF.Max(0f, _cooldown - dt);
        if (_text == null) return;
        _t += dt;
        int n = Math.Min(_text.Length, (int)(_t * CharsPerSec));
        if (n > _shown) { if (n % 3 == 0) sfx.Play("blip", 0.35f, 0.15f); _shown = n; }
        if (_t > Life) _text = null;
    }

    public void Draw(CanvasItem ci, Vector2 size, float clock)
    {
        if (_text == null) return;
        float a = MathF.Min(1f, MathF.Min(_t * 5f, (Life - _t) * 2f));
        float w = MathF.Min(400f, size.X - 32f);
        var lines = ReactorRun.Draw.Wrap(_text[..Math.Min(_text.Length, (int)(_t * CharsPerSec))], w - 76f, 14);
        float h = 34f + Math.Max(1, lines.Count) * 18f;
        var r = new Rect2(16f, size.Y - 16f - h, w, h);
        ci.DrawRect(r, new Color(0.016f, 0.03f, 0.063f, 0.85f * a));
        ReactorRun.Draw.Box(ci, r, Config.Alpha(Config.Neon(Config.Line, 1.3f), a), 1f);
        ReactorRun.Draw.Poly(ci, new Vector2(r.Position.X + 30f, r.Position.Y + h / 2f), 6, 15f, Mathf.Pi / 6f, Config.Alpha(Config.Neon(Config.Line), a), 1.5f);
        ReactorRun.Draw.Ring(ci, new Vector2(r.Position.X + 30f, r.Position.Y + h / 2f), 4f + Mathf.Sin(clock * 9f) * 1.5f, Config.Alpha(Config.Neon(Config.Line), a), 1.5f);
        ReactorRun.Draw.Text(ci, "VEGA · SHIP AI", new Vector2(r.Position.X + 60f, r.Position.Y + 18f), 11, Config.Alpha(Config.Dim, a));
        for (int i = 0; i < lines.Count; i++)
            ReactorRun.Draw.Text(ci, lines[i], new Vector2(r.Position.X + 60f, r.Position.Y + 38f + i * 18f), 14, Config.Alpha(Config.Ink, a));
    }
}
