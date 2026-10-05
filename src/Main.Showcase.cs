using System;
using System.Collections.Generic;
using Godot;
using static ReactorRun.Config;

namespace ReactorRun;

/// <summary>
/// Marketing tools, all driven from the command line:
///   -- --trailer     scripted 30s trailer (pair with --write-movie trailer.avi --fixed-fps 30)
///   -- --storeshots  1920x1080 store screenshots of the hangar, relay and debrief
///   -- --art         every Steam capsule / library image at its exact size
/// Output goes to the user data folder (user://).
/// </summary>
public partial class Main
{
    enum Showcase { None, Trailer, StoreShots, Art }
    Showcase _show = Showcase.None;
    float _showT;
    int _showStep;
    string _card = "";            // "title" | "end" | "" while a showcase card is up
    bool _escapeQueued;

    void StartShowcase(string[] args)
    {
        if (Array.IndexOf(args, "--trailer") >= 0) _show = Showcase.Trailer;
        else if (Array.IndexOf(args, "--storeshots") >= 0) _show = Showcase.StoreShots;
        else if (Array.IndexOf(args, "--art") is int ai && ai >= 0) { _show = Showcase.Art; if (ai + 1 < args.Length) int.TryParse(args[ai + 1], out _artIndex); }
        if (_show == Showcase.None) return;
        // throwaway progress so nothing touches the player's real save
        var s = new SaveData { NoPersist = true, Escapes = 6, CrewTotal = 14, Salvage = 1240, Runs = 23, Best = 612, BestLeft = 0.31f };
        foreach (var u in Upgrades) s.Up[u.Key] = Math.Min(2, u.Cost.Length);
        s.Hints.UnionWith(new[] { "fly", "dock", "reactor", "crewfull", "relay" });
        s.Glow = true; s.Crt = true; s.ScreenShake = true;
        _c.Save = s;
        _c.Ach = new Achievements(s, _c.Sfx);
        ApplySettings();
    }

    void Capture(string name)
    {
        GetViewport().GetTexture().GetImage().SavePng($"user://{name}.png");
        GD.Print($"[showcase] saved {name}.png");
    }

    void UpdateShowcase(float dt)
    {
        _showT += dt;
        switch (_show)
        {
            case Showcase.Trailer: TrailerStep(); break;
            case Showcase.StoreShots: StoreShotsStep(); break;
            case Showcase.Art: ArtStep(); break;
        }
    }

    // ------------------------------------------------------------------ trailer
    void TrailerStep()
    {
        (float at, Action act)[] steps =
        {
            (0.0f, () => { _card = "title"; _screen = Screen.Title; _menu.Clear(); }),
            (3.0f, () => { _card = ""; StartRun(false); _fly.Auto = true; }),
            (6.4f, () => Capture("shot_dogfight")),
            (8.4f, () =>
            {
                _c.Save.Escapes = 2;
                _station = new StationPhase(_c) { NoDeath = true };
                _screen = Screen.Station;
                _station.DebugLockdown();
                _station.SetAutoPath(_station.PathToReactor(), 6.2f);
            }),
            (12.0f, () => Capture("shot_station")),
            (19.0f, () => Capture("shot_escape")),
        };
        while (_showStep < steps.Length && _showT >= steps[_showStep].at) { steps[_showStep].act(); _showStep++; }

        // once the reactor is armed by touch, sprint back with the fuse tuned for a photo finish
        if (_screen == Screen.Station && _station.Armed && !_escapeQueued)
        {
            _escapeQueued = true;
            _station.SetAutoPath(_station.PathToShip(), 6.6f);
            _station.DebugSetFuse(6.6f + 0.34f);
        }
        if (_card == "end" && _showT > _endAt + 4f) GetTree().Quit();
    }

    float _endAt = 1e9f;

    void DrawShowcaseCard()
    {
        var size = _c.Size; float t = _card == "end" ? _showT - _endAt : _showT;
        _c.Stars.Draw(this, size, _c.Clock * 40f, 0f);
        float k = MathF.Min(size.X, size.Y) / 200f;
        DrawSetTransform(size / 2f + new Vector2(0, -20f), 0f, new Vector2(k, k));
        StationPhase.DrawReactor(this, Vector2.Zero, _card == "end", _c.Clock);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.016f, 0.02f, 0.043f, 0.55f));
        float a = MathF.Min(1f, t * 1.5f);
        int fs = (int)(size.Y / 7.5f);
        string logo = "REACTOR RUN";
        int shown = Math.Min(logo.Length, (int)(t * 9f));
        ReactorRun.Draw.Text(this, logo[..shown], new Vector2(size.X / 2f, size.Y * 0.47f), fs, Alpha(Neon(Line, 2f), a), HorizontalAlignment.Center);
        if (t > 1.2f) ReactorRun.Draw.Text(this, "Fly in. Blow the core. Get out alive.", new Vector2(size.X / 2f, size.Y * 0.47f + fs * 0.6f), fs / 3, Alpha(Neon(Core, 1.4f), MathF.Min(1f, (t - 1.2f) * 2f)), HorizontalAlignment.Center);
        if (_card == "end" && t > 1.5f)
            ReactorRun.Draw.Text(this, "WISHLIST NOW ON STEAM", new Vector2(size.X / 2f, size.Y * 0.80f), fs / 3, Alpha(Neon(Loot, 1.6f), MathF.Min(1f, (t - 1.5f) * 2f)), HorizontalAlignment.Center);
    }

    // ------------------------------------------------------------------ store screenshots
    void StoreShotsStep()
    {
        (float at, Action act)[] steps =
        {
            (0.2f, ShowHangar),
            (1.6f, () => Capture("shot_hangar")),
            (1.8f, () => { StartRun(false); _c.Save.Escapes = 4; _station = new StationPhase(_c) { NoDeath = true }; _screen = Screen.Station; _station.DebugOpenTerminal(); }),
            (3.0f, () => Capture("shot_relay")),
            (3.2f, () =>
            {
                _overlay = Overlay.None; _station.ChooseMod(0);
                var r = _c.Run; r.Salvage = 186; r.Over = true; r.Won = true; r.TimeLeft = 0.27f; r.CrewCarried = 3; r.CrewInStation = 4; r.Mult = 2; r.Overclocked = true;
                _c.Save.Best = 100; _c.Save.BestLeft = 0.5f;
                EndRun();
            }),
            (7.5f, () => Capture("shot_debrief")),
            (7.7f, () => GetTree().Quit()),
        };
        while (_showStep < steps.Length && _showT >= steps[_showStep].at) { steps[_showStep].act(); _showStep++; }
    }

    // ------------------------------------------------------------------ Steam art
    static readonly (string name, int w, int h, string layout)[] ArtSpecs =
    {
        ("steam_header_capsule_920x430", 920, 430, "wide"),
        ("steam_small_capsule_462x174", 462, 174, "logo"),
        ("steam_main_capsule_1232x706", 1232, 706, "wide"),
        ("steam_vertical_capsule_748x896", 748, 896, "tall"),
        ("steam_library_capsule_600x900", 600, 900, "tall"),
        ("steam_library_hero_3840x1240", 3840, 1240, "hero"),
        ("steam_library_logo_1280x720", 1280, 720, "logoonly"),
        ("steam_page_background_1438x810", 1438, 810, "bg"),
    };
    string _artLayout = "";

    int _artIndex;

    void ArtStep()
    {
        // one image per launch: godot --resolution WxH -- --art N
        var spec = ArtSpecs[Math.Clamp(_artIndex, 0, ArtSpecs.Length - 1)];
        _artLayout = spec.layout; _crt.Visible = false;
        if (_showT > 0.6f && _showStep == 0) { _showStep = 1; Capture(spec.name); GetTree().Quit(); }
    }

    void DrawArt()
    {
        var size = _c.Size; float clock = 2.3f;
        DrawRect(new Rect2(Vector2.Zero, size), _artLayout == "logoonly" ? new Color(0, 0, 0) : Bg);
        if (_artLayout == "logoonly")
        {
            int f = (int)(size.Y / 4.2f);
            ReactorRun.Draw.Text(this, "REACTOR", new Vector2(size.X / 2f, size.Y * 0.47f), f, Neon(Line, 2f), HorizontalAlignment.Center);
            ReactorRun.Draw.Text(this, "RUN", new Vector2(size.X / 2f, size.Y * 0.47f + f * 0.95f), f, Neon(Core, 2f), HorizontalAlignment.Center);
            return;
        }
        _c.Stars.Draw(this, size, 0f, 0f);
        bool tall = _artLayout == "tall", small = _artLayout == "logo";
        float u = MathF.Min(size.X, size.Y);
        var reactor = tall ? new Vector2(size.X * 0.5f, size.Y * 0.64f)
            : small ? new Vector2(size.X * 0.86f, size.Y * 0.5f)
            : new Vector2(size.X * (_artLayout == "hero" ? 0.62f : 0.74f), size.Y * 0.5f);
        float rk = u / (tall ? 150f : small ? 190f : 150f);

        // station frame + blast wave rings around the core
        var frame = new List<Vector2>();
        for (int k = -6; k <= 6; k++)
        {
            float x = reactor.X + k * u * 0.16f;
            frame.Add(new Vector2(x, 0)); frame.Add(new Vector2(x, size.Y * 0.18f));
            frame.Add(new Vector2(x, size.Y * 0.82f)); frame.Add(new Vector2(x, size.Y));
        }
        frame.Add(new Vector2(0, size.Y * 0.18f)); frame.Add(new Vector2(size.X, size.Y * 0.18f));
        frame.Add(new Vector2(0, size.Y * 0.82f)); frame.Add(new Vector2(size.X, size.Y * 0.82f));
        DrawMultiline(frame.ToArray(), Alpha(Neon(Line, 1.3f), 0.35f), MathF.Max(1.5f, u / 300f));
        for (int r = 1; r <= 4; r++) ReactorRun.Draw.Ring(this, reactor, u * (0.12f + r * 0.11f), Alpha(Neon(r % 2 == 0 ? Red : Core, 1.8f), 0.55f - r * 0.1f), MathF.Max(2f, u / 200f));
        DrawRect(new Rect2(Vector2.Zero, size), Alpha(Red, 0.06f));
        DrawSetTransform(reactor, 0f, new Vector2(rk, rk));
        StationPhase.DrawReactor(this, Vector2.Zero, true, clock);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // astronaut sprinting away from the core, with jetpack flame
        if (small) goto Logo;
        var astro = tall ? new Vector2(size.X * 0.5f - u * 0.3f, size.Y * 0.8f) : new Vector2(reactor.X - u * 0.62f, size.Y * 0.62f);
        float ak = u / 120f;
        DrawSetTransform(astro, 0f, new Vector2(ak, ak));
        var flame = new List<Vector2>();
        for (int i = 0; i < 9; i++) { float s = i - 4f; flame.Add(new Vector2(-1f, 20f)); flame.Add(new Vector2(-1f + s * 0.7f, 30f + MathF.Abs(s) * -1.2f + 6f)); }
        DrawMultiline(flame.ToArray(), Neon(Core, 2f), 1.5f);
        StationPhase.DrawAstronaut(this, new Vector2(-7f, 0f), 14f, -1, Ink);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        // the escape ship waiting at the edge
        if (!tall && !small) FlyPhase.DrawShip(this, new Vector2(size.X * 0.08f, size.Y * 0.62f), u / 160f, facingLeft: true);

        if (_artLayout == "hero" || _artLayout == "bg") return;
        Logo:
        // logo, fitted to its space
        static int Fit(string text, int want, float maxW) => (int)MathF.Min(want, want * maxW / MathF.Max(1f, ReactorRun.Draw.TextWidth(text, want)));
        if (tall)
        {
            int fs = Fit("REACTOR", (int)(size.X / 5f), size.X * 0.86f);
            var lp = new Vector2(size.X / 2f, size.Y * 0.17f);
            ReactorRun.Draw.Text(this, "REACTOR", lp, fs, Neon(Line, 1.5f), HorizontalAlignment.Center);
            ReactorRun.Draw.Text(this, "RUN", lp + new Vector2(0, fs * 0.95f), fs, Neon(Core, 1.5f), HorizontalAlignment.Center);
        }
        else
        {
            float maxW = size.X * (small ? 0.66f : 0.5f);
            int fs = Fit("REACTOR RUN", (int)(size.Y / (small ? 2.6f : 5f)), maxW);
            var lp = new Vector2(size.X * (small ? 0.36f : 0.29f), size.Y * (small ? 0.62f : 0.42f));
            ReactorRun.Draw.Text(this, "REACTOR RUN", lp, fs, Neon(Line, 1.5f), HorizontalAlignment.Center);
            if (!small) ReactorRun.Draw.Text(this, "Fly in. Blow the core. Get out alive.", lp + new Vector2(0, fs * 0.7f), Fit("Fly in. Blow the core. Get out alive.", fs / 3, maxW), Neon(Core, 1.3f), HorizontalAlignment.Center);
        }
    }
}
