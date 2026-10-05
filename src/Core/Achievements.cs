using System;
using System.Collections.Generic;
using Godot;
using static ReactorRun.Config;

namespace ReactorRun;

public record AchievementDef(string Id, string Name, string Desc);

/// <summary>
/// Local achievements with unlock toasts. Ids match the API names to create in Steamworks;
/// a Steam bridge only has to listen to <see cref="Unlocked"/> and call SetAchievement(id).
/// </summary>
public sealed class Achievements
{
    public static readonly AchievementDef[] All =
    {
        new("first_escape", "Get Out Alive", "Escape your first station."),
        new("photo_finish", "Photo Finish", "Escape with under 1 second on the fuse."),
        new("by_a_hair", "By a Hair", "Escape with under 0.25 seconds on the fuse."),
        new("overclocked", "Overclocked", "Escape after overclocking the reactor."),
        new("full_house", "Full House", "Escape carrying 4 survivors."),
        new("untouchable", "Untouchable", "Escape without taking a single hit."),
        new("pacifist", "Pacifist", "Escape without firing a shot inside the station."),
        new("speed_demon", "Speed Demon", "Arm the reactor within 30 seconds of docking."),
        new("cursed", "Cursed and Loving It", "Escape with both cursed power-ups."),
        new("kitted_out", "Kitted Out", "Escape with 2 power-ups."),
        new("greed", "Greed Is Good", "Bank 500 salvage in one run."),
        new("gunship_down", "Gunship Down", "Destroy a gunship."),
        new("ace", "Ace Pilot", "Destroy 100 fighters."),
        new("drone_hunter", "Drone Hunter", "Destroy 50 hunter drones."),
        new("demolition", "Demolition Crew", "Destroy 100 turrets."),
        new("rescuer", "Search and Rescue", "Rescue 25 survivors."),
        new("veteran", "Veteran", "Escape 10 stations."),
        new("wrangler", "Reactor Wrangler", "Destroy all 24 stations and win the campaign."),
        new("baron", "Salvage Baron", "Bank 5,000 salvage in total."),
        new("fully_loaded", "Fully Loaded", "Max out every hangar upgrade."),
        new("daily_driver", "Daily Driver", "Escape a Daily Run."),
        new("learning", "Learning Experience", "Lose 10 runs. It happens."),
    };

    readonly SaveData _save;
    readonly Sfx _sfx;
    readonly Queue<AchievementDef> _toasts = new();
    AchievementDef _cur;
    float _t;

    public event Action<string> Unlocked;

    public Achievements(SaveData save, Sfx sfx) { _save = save; _sfx = sfx; }

    public int Count => _save.Achievements.Count;
    public bool Has(string id) => _save.Achievements.Contains(id);

    public void Unlock(string id)
    {
        if (!_save.Achievements.Add(id)) return;
        _save.Save();
        var def = Array.Find(All, a => a.Id == id);
        if (def != null) _toasts.Enqueue(def);
        Unlocked?.Invoke(id);
    }

    /// <summary>Adds to a lifetime stat and unlocks any threshold achievements it crosses.</summary>
    public void Stat(string key, int add = 1)
    {
        int v = _save.Stat(key) + add;
        _save.Stats[key] = v;
        switch (key)
        {
            case "kills" when v >= 100: Unlock("ace"); break;
            case "drones" when v >= 50: Unlock("drone_hunter"); break;
            case "turrets" when v >= 100: Unlock("demolition"); break;
            case "deaths" when v >= 10: Unlock("learning"); break;
            case "banked" when v >= 5000: Unlock("baron"); break;
        }
    }

    public void Update(float dt)
    {
        if (_cur == null && _toasts.Count > 0) { _cur = _toasts.Dequeue(); _t = 0f; _sfx.Play("win", 0.7f); }
        if (_cur == null) return;
        _t += dt;
        if (_t > 3.6f) _cur = null;
    }

    public void Draw(CanvasItem ci, Vector2 size)
    {
        if (_cur == null) return;
        float slide = MathF.Min(1f, MathF.Min(_t * 4f, (3.6f - _t) * 4f));
        float w = 340f, h = 66f, x = size.X - 16f - w * slide, y = 84f;
        var r = new Rect2(x, y, w, h);
        ci.DrawRect(r, new Color(0.02f, 0.035f, 0.07f, 0.92f));
        ci.DrawRect(r, Neon(Core, 1.6f), false, 2f);
        ReactorRun.Draw.Poly(ci, new Vector2(x + 32f, y + h / 2f), 6, 18f, Mathf.Pi / 6f, Neon(Core, 1.8f), 2f);
        ReactorRun.Draw.Poly(ci, new Vector2(x + 32f, y + h / 2f), 6, 9f, Mathf.Pi / 6f, Neon(Core, 1.8f), 2f);
        ReactorRun.Draw.Text(ci, "ACHIEVEMENT UNLOCKED", new Vector2(x + 62f, y + 22f), 11, Core);
        ReactorRun.Draw.Text(ci, _cur.Name, new Vector2(x + 62f, y + 42f), 17, Neon(Ink, 1.2f));
        ReactorRun.Draw.Text(ci, _cur.Desc, new Vector2(x + 62f, y + 58f), 12, Dim);
    }
}
