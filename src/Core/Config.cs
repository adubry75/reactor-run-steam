using System;
using Godot;

namespace ReactorRun;

/// <summary>Palette, tuning numbers and difficulty formulas. Mirrors the "Build 0.5 spec" tab of the pitch doc.</summary>
public static class Config
{
    // ---------- palette (base colors; Neon() pushes them above 1.0 so the HDR glow catches them) ----------
    public static readonly Color Bg = new(0.0157f, 0.0196f, 0.0431f);
    public static readonly Color Line = new(0.275f, 0.945f, 1f);
    public static readonly Color Hot = new(1f, 0.239f, 0.545f);
    public static readonly Color Core = new(1f, 0.714f, 0.153f);
    public static readonly Color Loot = new(0.549f, 1f, 0.620f);
    public static readonly Color Ink = new(0.851f, 0.965f, 1f);
    public static readonly Color Dim = new(0.435f, 0.553f, 0.612f);
    public static readonly Color Red = new(1f, 0.165f, 0.227f);
    public static readonly Color White = new(1f, 1f, 1f);
    public static readonly Color WallFill = new(0.027f, 0.063f, 0.102f);

    public static Color Neon(Color c, float k = 1.8f) => new(c.R * k, c.G * k, c.B * k, c.A);
    public static Color Alpha(Color c, float a) => new(c.R, c.G, c.B, a);

    // ---------- shared ----------
    public const float MaxDt = 1f / 20f;

    // ---------- dogfight ----------
    public const float FlyWorldW = 2150f, FlyWorldH = 700f, StationX = 1720f, Bay0 = 300f, Bay1 = 400f;
    public const float ShipAccel = 950f, ShipMaxSpeed = 330f, ShipDrag = 2.4f;
    public const float ShipShotSpeed = 760f, ShipShotCooldown = 0.15f, EnemyShotSpeed = 240f;

    // ---------- station ----------
    public const int Tile = 32, CellW = 12, CellH = 9;
    public const float RunAccel = 1100f, RunMax = 230f, Gravity = 420f, Thrust = 930f, MaxRise = 270f, MaxFall = 440f;
    public const float BaseFuel = 2.4f, FuelPerLevel = 0.5f, RefuelRate = 1.6f;
    public const float ShotSpeed = 620f, ShotCooldown = 0.18f;
    public const float ViewHeight = 520f;

    // ---------- difficulty (tier = successful escapes) ----------
    public static int Tier(SaveData s) => s.Escapes;
    public static float Pace(int tier) => MathF.Max(0.55f, 1f - tier * 0.04f);
    public static (int cols, int rows) StationSize(int tier) => (4 + Math.Min(4, tier / 2), 3 + Math.Min(3, tier / 3));
    public static int FightersNeeded(int tier) => Math.Min(20, 5 + 2 * tier);
    public static int FightersAlive(int tier) => Math.Min(7, 2 + tier / 2);
    public static float TurretChance(int tier) => 0.30f + 0.06f * Math.Min(8, tier);
    public static float AlertSeconds(int cells, int tier) => MathF.Max(30f, 35f + cells * 1.5f - MathF.Min(15f, tier * 1.5f));
    public static float DroneInterval(int tier) => MathF.Max(3.5f, 9f - tier * 0.4f);
    public static int DronesAlive(int tier) => Math.Min(6, 2 + tier / 3);
    public static float DroneSpeed(int tier) => 70f + 6f * Math.Min(10, tier);
    public static float Fuse(int distance, int fuseLevel, bool overclock, bool unstable) =>
        (6f + distance * 2.6f + 1.5f * fuseLevel) * (overclock ? 0.65f : 1f) * (unstable ? 0.75f : 1f);

    // ---------- upgrades ----------
    public record Upgrade(string Key, string Name, string[] Desc, int[] Cost);

    static int[] Costs(int b, int n)
    {
        var a = new int[n];
        for (int i = 0; i < n; i++) a[i] = (int)Math.Round(b * Math.Pow(1.7, i) / 5.0) * 5;
        return a;
    }

    public static readonly Upgrade[] Upgrades =
    {
        new("shield", "Suit shield", new[] { "+1 shield pip" }, Costs(100, 4)),
        new("fuel", "Fuel tank", new[] { "+0.5s of jetpack thrust" }, Costs(50, 5)),
        new("thrust", "Thrusters", new[] { "+8% speed and lift, ship and jetpack" }, Costs(60, 5)),
        new("fuse", "Long fuse", new[] { "+1.5s on every countdown" }, Costs(50, 5)),
        new("crew", "Crew bay", new[] { "Carry one more survivor" }, Costs(90, 3)),
        new("scan", "Scanner", new[] { "Arrow to your ship during the escape", "Arrow to the reactor", "Full station map" }, new[] { 150, 300, 550 }),
    };

    // ---------- relay power-ups ----------
    public record Mod(string Key, string Name, string Desc, bool Cursed = false);

    public static readonly Mod[] Mods =
    {
        new("recycler", "Fuel Recycler", "Your jetpack slowly refuels in the air."),
        new("killclock", "Kill Clock", "Kills during the countdown add 1.5s to the fuse."),
        new("magnet", "Salvage Magnet", "Nearby crates fly to you and are worth 25% more."),
        new("rapid", "Rapid Fire", "Fire twice as fast."),
        new("spread", "Spread Shot", "Fire three shots at once."),
        new("plating", "Emergency Plating", "+1 shield pip for this run, and a full repair."),
        new("ghost", "Ghost Signal", "Hunter drones move 35% slower."),
        new("harness", "Crew Harness", "Crew you carry slow you half as much."),
        new("jammer", "Signal Jammer", "+30s on the security sweep. Turrets fire slower."),
        new("afterburner", "Afterburner", "+15% speed and lift."),
        new("overcore", "Unstable Core", "Fuse is 25% shorter, but salvage pays x1.5.", true),
        new("desperate", "Desperate Measures", "Lose 1 shield pip now. Unlimited jetpack fuel.", true),
    };
}

/// <summary>Small seeded-or-not RNG helper so gameplay code never touches engine globals.</summary>
public static class Rng
{
    static readonly Random R = new();
    public static float F() => (float)R.NextDouble();
    public static float Range(float a, float b) => a + (b - a) * F();
    public static int Int(int maxExclusive) => R.Next(maxExclusive);
    public static T Pick<T>(System.Collections.Generic.IList<T> list) => list[R.Next(list.Count)];

    public static void Shuffle<T>(System.Collections.Generic.IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = R.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
