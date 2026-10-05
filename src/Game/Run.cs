using System;
using System.Collections.Generic;
using Godot;

namespace ReactorRun;

/// <summary>State for one run, from launch to debrief.</summary>
public sealed class Run
{
    public int Number;
    public int Salvage;
    public int Shield, MaxShield;
    public float Invulnerable;
    public int Mult = 1;
    public bool Overclocked;
    public readonly HashSet<string> Mods = new();
    public readonly List<Config.Mod> ModList = new();

    // ending
    public bool Over, Won, EndHandled;
    public string Reason = "";
    public float OverT, TimeLeft;
    public int CrewCarried, CrewInStation;

    // achievement tracking
    public bool Daily;
    public bool Final;   // station 24 of the campaign
    public bool TookDamage;
    public int StationShots;
    public float ArmedAfter = -1f;   // seconds from docking to arming

    public bool Has(string mod) => Mods.Contains(mod);
}

/// <summary>Everything a phase needs: save, run, effects, audio, screen size and clock.</summary>
public sealed class Ctx
{
    public SaveData Save;
    public Run Run;
    public Fx Fx = new();
    public Vega Vega = new();
    public Sfx Sfx;
    public Music Music;
    public Achievements Ach;
    public Vector2 Size;
    public float Clock;
    public readonly Starfield Stars = new();

    public const int DailyTier = 5;
    public int Tier => Run != null && Run.Daily ? DailyTier : Config.Tier(Save);

    public void Hurt(Vector2 at)
    {
        if (Run.Invulnerable > 0f || Run.Over) return;
        Run.Shield--;
        Run.TookDamage = true;
        Run.Invulnerable = 1.2f;
        Fx.AddShake(12f);
        Sfx.Play("hurt");
        Fx.Burst(at, Config.Hot, 14);
        if (Run.Shield == 1) Vega.Say("lowShield", true);
        if (Run.Shield <= 0) Die("Hull breached. Your suit shield gave out.", at);
    }

    public void Die(string reason, Vector2 at)
    {
        if (Run.Over) return;
        Run.Over = true; Run.Won = false; Run.Reason = reason; Run.OverT = 0f;
        Fx.Shake = 22f; Fx.Flash = 0.8f;
        Sfx.Play("boom");
        Sfx.Play("sting_death", 0.8f);
        Fx.Burst(at, Config.Hot, 40, 320f, 1.2f);
        Fx.Burst(at, Config.Core, 30, 200f, 1f);
    }
}

/// <summary>Screen-space parallax starfield shared by every scene.</summary>
public sealed class Starfield
{
    readonly Vector3[] _stars = new Vector3[160];

    public Starfield()
    {
        for (int i = 0; i < _stars.Length; i++) _stars[i] = new Vector3(Rng.F(), Rng.F(), Rng.Range(0.2f, 1f));
    }

    public void Draw(CanvasItem ci, Vector2 size, float ox, float oy)
    {
        foreach (var s in _stars)
        {
            float x = Mod(s.X * size.X - ox * s.Z * 0.3f, size.X), y = Mod(s.Y * size.Y - oy * s.Z * 0.3f, size.Y);
            ci.DrawRect(new Rect2(x, y, s.Z * 2f, s.Z * 2f), Config.Alpha(Config.Ink, 0.25f + s.Z * 0.5f));
        }
    }

    static float Mod(float a, float m) => ((a % m) + m) % m;
}
