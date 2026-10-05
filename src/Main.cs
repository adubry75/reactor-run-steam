using System;
using System.Collections.Generic;
using Godot;
using static ReactorRun.Config;

namespace ReactorRun;

/// <summary>Root node: owns the game state machine, screens, overlays, glow and CRT post-processing.</summary>
public partial class Main : Node2D
{
    enum Screen { Title, Hangar, Fly, Station, Outro, Debrief }
    enum Overlay { None, Pause, Settings, Relay, Scores, Trophies }

    sealed class DebriefData
    {
        public string Head = "", Reason = "", VegaLine = "", Stamp = "";
        public bool Won;
        public readonly List<(string label, string value)> Rows = new();
        public int Total;
        public float T;
        public int Shown;
        public bool StampPlayed;
        public string Extra = "";
        public readonly List<string> NewAchievements = new();
    }

    Ctx _c;
    Screen _screen = Screen.Title;
    Overlay _overlay = Overlay.None, _settingsReturn = Overlay.None;
    FlyPhase _fly;
    StationPhase _station;
    Outro _outro;
    DebriefData _debrief;
    readonly Menu _menu = new(), _overlayMenu = new();
    bool _resetArmed, _musicMuted;
    string _relayHint = "";
    readonly List<string> _pendingAch = new();
    WorldEnvironment _env;
    ColorRect _crt;

    const float PanelW = 560f;

    // ------------------------------------------------------------------ setup
    public override void _Ready()
    {
        GameInput.Register();
        var save = SaveData.Load();
        var music = new Music(); AddChild(music);
        var sfx = new Sfx(); AddChild(sfx);
        _c = new Ctx { Save = save, Music = music, Sfx = sfx, Run = new Run() };
        _c.Ach = new Achievements(save, sfx);
        _c.Ach.Unlocked += id => _pendingAch.Add(id);

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Canvas,
            GlowEnabled = true,
            GlowIntensity = 0.9f,
            GlowStrength = 1.1f,
            GlowBloom = 0.08f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
        };
        _env = new WorldEnvironment { Environment = env };
        AddChild(_env);

        var layer = new CanvasLayer { Layer = 10 };
        AddChild(layer);
        _crt = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        _crt.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _crt.Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/crt.gdshader") };
        layer.AddChild(_crt);

        ApplySettings();
        ShowTitle();
        _autotest = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--autotest") >= 0;
        StartShowcase(OS.GetCmdlineUserArgs());
    }

    // ------------------------------------------------------------------ autotest (godot -- --autotest): plays a scripted run and saves screenshots
    bool _autotest;
    float _autoT;
    int _autoStep;

    void Shot(string name)
    {
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng($"user://autotest_{name}.png");
        GD.Print($"[autotest] {name} screen={_screen} overlay={_overlay} salvage={_c.Save.Salvage}");
    }

    void AutoTest(float dt)
    {
        _autoT += dt;
        (float at, System.Action act)[] steps =
        {
            (0.6f, () => Shot("title")),
            (0.8f, ShowHangar),
            (1.8f, () => Shot("hangar")),
            (2.0f, () => StartRun(false)),
            (4.0f, () => Shot("dogfight")),
            (4.2f, () => { _station = new StationPhase(_c); _screen = Screen.Station; }),
            (6.0f, () => Shot("station")),
            (6.2f, () => _station.DebugOpenTerminal()),
            (6.8f, () => Shot("relay")),
            (7.0f, () => { if (_overlay == Overlay.Relay) _overlayMenu.Items[0].Activate(); }),
            (7.4f, () => { _station.DebugWarp(_station.S.ReactorPos + new Vector2(0, 30)); _station.DebugArm(false); }),
            (9.0f, () => Shot("countdown")),
            (9.2f, () => _station.DebugWarp(_station.S.Ship.GetCenter())),
            (10.4f, () => Shot("outro")),
            (14.5f, () => Shot("debrief")),
            (15.5f, OpenScores),
            (16.2f, () => Shot("halloffame")),
            (16.4f, OpenTrophies),
            (17.4f, () => Shot("achievements")),
            (17.6f, () => { _overlay = Overlay.None; StartRun(true); }),
            (18.8f, () => { _station = new StationPhase(_c); _screen = Screen.Station; }),
            (20.0f, () => Shot("daily")),
            (20.2f, () => { _c.Save.ResetProgress(); GD.Print($"[autotest] done, achievements unlocked in run: ok"); GetTree().Quit(); }),
        };
        while (_autoStep < steps.Length && _autoT >= steps[_autoStep].at) { steps[_autoStep].act(); _autoStep++; }
    }

    void ApplySettings()
    {
        var s = _c.Save;
        _c.Music.Volume = _musicMuted ? 0f : s.MusicVolume;
        _c.Music.ApplyVolume();
        _c.Sfx.Volume = s.SfxVolume;
        _env.Environment.GlowEnabled = s.Glow;
        _crt.Visible = s.Crt;
        _c.Fx.ShakeEnabled = s.ScreenShake;
        var want = s.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != want) DisplayServer.WindowSetMode(want);
    }

    // ------------------------------------------------------------------ screens
    void ShowTitle()
    {
        _screen = Screen.Title;
        _menu.Clear();
        _menu.Add("Enter the hangar", ShowHangar);
        _menu.Add("Settings", () => OpenSettings(Overlay.None));
        _menu.Add("Quit", () => GetTree().Quit());
    }

    void ShowHangar()
    {
        _screen = Screen.Hangar;
        _overlay = Overlay.None;
        BuildHangarMenu(0);
        if (_c.Save.Salvage < 40 && _c.Save.Runs > 0) _c.Vega.Say("poor"); else _c.Vega.Say("hangar");
    }

    void BuildHangarMenu(int keepSelected)
    {
        _menu.Clear();
        var save = _c.Save;
        for (int i = 0; i < Upgrades.Length; i++)
        {
            var u = Upgrades[i]; int lvl = Math.Min(save.Level(u.Key), u.Cost.Length); bool max = lvl >= u.Cost.Length;
            string desc = u.Desc.Length > 1 ? (max ? "All tiers installed" : "Next: " + u.Desc[lvl]) : u.Desc[0];
            int idx = i;
            var item = _menu.Add(u.Name, () => Buy(idx), desc, max ? "MAXED" : $"BUY · {u.Cost[lvl]}", !max && save.Salvage >= u.Cost[lvl], Loot);
            item.Pips = lvl; item.PipMax = u.Cost.Length;
        }
        _menu.Add($"Launch station {save.Runs + 1}", () => StartRun(false), accent: Core);
        bool dailyToday = save.DailyDate == Today;
        _menu.Add("Daily run", () => StartRun(true), "", dailyToday && save.DailyBest > 0 ? $"BEST {save.DailyBest}" : "", accent: Hot);
        _menu.Add("Hall of Fame", OpenScores, accent: Loot);
        _menu.Add($"Achievements {_c.Ach.Count}/{Achievements.All.Length}", OpenTrophies, accent: Core);
        _menu.Add("Settings", () => OpenSettings(Overlay.None));
        _menu.Add(_resetArmed ? "Confirm: wipe all" : "Reset progress", ResetProgress, accent: Hot);
        _menu.Selected = Math.Clamp(keepSelected, 0, _menu.Items.Count - 1);
    }

    static string Today => System.DateTime.Now.ToString("yyyy-MM-dd");

    void OpenScores()
    {
        _overlay = Overlay.Scores;
        _overlayMenu.Clear(); _overlayMenu.Horizontal = false;
        _overlayMenu.Add("Back", () => _overlay = Overlay.None, accent: Core);
    }

    void OpenTrophies()
    {
        _overlay = Overlay.Trophies;
        _overlayMenu.Clear(); _overlayMenu.Horizontal = false;
        _overlayMenu.Add("Back", () => _overlay = Overlay.None, accent: Core);
    }

    void Buy(int i)
    {
        var u = Upgrades[i]; var save = _c.Save; int lvl = save.Level(u.Key);
        if (lvl >= u.Cost.Length || save.Salvage < u.Cost[lvl]) return;
        save.Salvage -= u.Cost[lvl];
        save.Up[u.Key] = lvl + 1;
        save.Save();
        var sp = HangarScene.ShipPos(_c.Size, PanelW);
        _c.Fx.Burst(sp + new Vector2(0, -20), Loot, 30, 260f, 0.9f);
        _c.Fx.Burst(sp + new Vector2(0, -20), Line, 16, 160f, 0.7f);
        _c.Fx.Flash = 0.15f;
        _c.Sfx.Play("win", 0.7f);
        _c.Vega.Say("buy", true);
        if (System.Array.TrueForAll(Upgrades, x => save.Level(x.Key) >= x.Cost.Length)) _c.Ach.Unlock("fully_loaded");
        BuildHangarMenu(_menu.Selected);
    }

    void ResetProgress()
    {
        if (!_resetArmed) { _resetArmed = true; BuildHangarMenu(_menu.Selected); return; }
        _resetArmed = false;
        _c.Save.ResetProgress();
        BuildHangarMenu(0);
    }

    void StartRun(bool daily)
    {
        _resetArmed = false;
        var st = _c.Save.Level("shield") + 3;
        _c.Run = new Run { Number = _c.Save.Runs + 1, Shield = st, MaxShield = st, Daily = daily };
        if (daily)
        {
            if (_c.Save.DailyDate != Today) { _c.Save.DailyDate = Today; _c.Save.DailyBest = 0; _c.Save.DailyTries = 0; }
            _c.Save.DailyTries++;
            _c.Save.Save();
        }
        _c.Fx.Clear(); _c.Vega.Clear();
        _overlay = Overlay.None;
        _fly = new FlyPhase(_c);
        _station = null; _outro = null;
        _screen = Screen.Fly;
    }

    void OpenSettings(Overlay returnTo)
    {
        _settingsReturn = returnTo;
        _overlay = Overlay.Settings;
        BuildSettingsMenu(0);
    }

    void BuildSettingsMenu(int keep)
    {
        var s = _c.Save;
        _overlayMenu.Clear();
        _overlayMenu.Horizontal = false;
        string Pct(float v) => $"{Mathf.RoundToInt(v * 100)}%";
        string On(bool b) => b ? "ON" : "OFF";
        void Changed(int sel) { s.Save(); ApplySettings(); BuildSettingsMenu(sel); }
        _overlayMenu.Add("Music volume", null, "Left / right to change", Pct(s.MusicVolume), adjust: d => { s.MusicVolume = Mathf.Clamp(s.MusicVolume + d * 0.1f, 0f, 1f); Changed(0); });
        _overlayMenu.Add("Effects volume", null, "Left / right to change", Pct(s.SfxVolume), adjust: d => { s.SfxVolume = Mathf.Clamp(s.SfxVolume + d * 0.1f, 0f, 1f); Changed(1); });
        _overlayMenu.Add("Glow", () => { s.Glow = !s.Glow; Changed(2); }, "", On(s.Glow), adjust: _ => { s.Glow = !s.Glow; Changed(2); });
        _overlayMenu.Add("CRT filter", () => { s.Crt = !s.Crt; Changed(3); }, "", On(s.Crt), adjust: _ => { s.Crt = !s.Crt; Changed(3); });
        _overlayMenu.Add("Screen shake", () => { s.ScreenShake = !s.ScreenShake; Changed(4); }, "", On(s.ScreenShake), adjust: _ => { s.ScreenShake = !s.ScreenShake; Changed(4); });
        _overlayMenu.Add("Fullscreen", () => { s.Fullscreen = !s.Fullscreen; Changed(5); }, "F11 also works", On(s.Fullscreen), adjust: _ => { s.Fullscreen = !s.Fullscreen; Changed(5); });
        _overlayMenu.Add("Back", CloseSettings, accent: Core);
        _overlayMenu.Selected = Math.Clamp(keep, 0, _overlayMenu.Items.Count - 1);
    }

    void CloseSettings()
    {
        _overlay = _settingsReturn;
        if (_overlay == Overlay.Pause) BuildPauseMenu();
    }

    void BuildPauseMenu()
    {
        _overlayMenu.Clear();
        _overlayMenu.Horizontal = false;
        _overlayMenu.Add("Resume", () => _overlay = Overlay.None, accent: Core);
        _overlayMenu.Add("Settings", () => OpenSettings(Overlay.Pause));
        _overlayMenu.Add("Abandon run", () => { _overlay = Overlay.None; _c.Run.Over = true; _c.Run.Won = false; _c.Run.Reason = "You pulled out. The salvage drone grabbed what it could."; EndRun(); }, "Keeps 40% of this run's salvage", accent: Hot);
        _overlayMenu.Add("Quit game", () => GetTree().Quit());
    }

    void BuildRelayMenu()
    {
        _overlayMenu.Clear();
        _overlayMenu.Horizontal = true;
        var choices = _station.PendingChoices;
        for (int i = 0; i < choices.Count; i++)
        {
            int idx = i; var m = choices[i];
            _overlayMenu.Add(m.Name, () => { _station.ChooseMod(idx); _overlay = Overlay.None; }, m.Desc, "", true, m.Cursed ? Hot : Loot);
        }
        _relayHint = _c.Save.HintOnce("relay") ? "Lasts for this run only. The station is frozen while you choose." : "Lasts for this run only.";
        _overlay = Overlay.Relay;
    }

    // ------------------------------------------------------------------ end of run
    void EndRun()
    {
        var run = _c.Run; var save = _c.Save;
        var d = new DebriefData { Won = run.Won };
        if (run.Won)
        {
            float core = run.Has("overcore") ? 1.5f : 1f;
            int b = Mathf.RoundToInt(run.Salvage * run.Mult * core), crew = run.CrewCarried * 40;
            int photo = run.TimeLeft < 3f ? Mathf.RoundToInt(30f + (3f - run.TimeLeft) * 40f) : 0;
            d.Total = b + crew + photo;
            d.Rows.Add(("Salvage collected", run.Salvage.ToString()));
            if (run.Mult > 1 || core > 1f)
            {
                var parts = new List<string>();
                if (run.Mult > 1) parts.Add("Overclock x2");
                if (core > 1f) parts.Add("Unstable Core x1.5");
                d.Rows.Add((string.Join(" + ", parts), "+" + (b - run.Salvage)));
            }
            if (crew > 0) d.Rows.Add(($"Crew rescued ({run.CrewCarried} of {run.CrewInStation})", "+" + crew));
            if (photo > 0) d.Rows.Add(($"Photo finish ({run.TimeLeft:0.00}s left)", "+" + photo));
            if (run.ModList.Count > 0) d.Rows.Add(("Power-ups", string.Join(", ", run.ModList.ConvertAll(m => m.Name))));
            d.Rows.Add(("Time left on the fuse", $"{run.TimeLeft:0.00}s"));
            var stamps = new List<string>();
            if (d.Total > save.Best && save.Runs > 0) stamps.Add("NEW RECORD");
            if (save.BestLeft.HasValue && run.TimeLeft < save.BestLeft.Value) stamps.Add("CLOSEST CALL");
            if (!save.BestLeft.HasValue || run.TimeLeft < save.BestLeft.Value) save.BestLeft = run.TimeLeft;
            d.Stamp = string.Join(" · ", stamps);
            save.Escapes++;
            save.CrewTotal += run.CrewCarried;
            d.Head = "ESCAPED";
            d.Reason = run.TimeLeft < 1f ? "That was way too close. Do it again." : "The station is debris. Your hold is full.";
            d.VegaLine = Vega.Pick(run.TimeLeft < 1f ? "escapeClose" : "escape");
        }
        else
        {
            d.Total = Mathf.RoundToInt(run.Salvage * 0.4f);
            d.Rows.Add(("Salvage collected", run.Salvage.ToString()));
            d.Rows.Add(("Recovered by the salvage drone (40%)", d.Total.ToString()));
            d.Head = "LOST WITH THE STATION";
            d.Reason = run.Reason;
            d.VegaLine = Vega.Pick("death");
            if (d.Total > save.Best && save.Runs > 0) d.Stamp = "NEW RECORD";
        }
        d.Rows.Add(("Banked", d.Total.ToString()));
        var ach = _c.Ach;
        if (run.Won)
        {
            ach.Unlock("first_escape");
            if (run.TimeLeft < 1f) ach.Unlock("photo_finish");
            if (run.TimeLeft < 0.25f) ach.Unlock("by_a_hair");
            if (run.Overclocked) ach.Unlock("overclocked");
            if (run.CrewCarried >= 4) ach.Unlock("full_house");
            if (!run.TookDamage) ach.Unlock("untouchable");
            if (run.StationShots == 0) ach.Unlock("pacifist");
            if (run.ModList.FindAll(m => m.Cursed).Count >= 2) ach.Unlock("cursed");
            if (run.ModList.Count >= 2) ach.Unlock("kitted_out");
            if (d.Total >= 500) ach.Unlock("greed");
            if (run.Daily) ach.Unlock("daily_driver");
            if (save.Escapes >= 10) ach.Unlock("veteran");
            if (save.Escapes >= 25) ach.Unlock("wrangler");
            if (save.CrewTotal >= 25) ach.Unlock("rescuer");
        }
        else ach.Stat("deaths");
        ach.Stat("banked", d.Total);
        int rank = save.AddScore(new ScoreEntry { Banked = d.Total, Station = run.Number, Won = run.Won, TimeLeft = run.Won ? run.TimeLeft : 0f, Crew = run.CrewCarried, Daily = run.Daily, Date = Today });
        if (run.Daily)
        {
            bool best = d.Total > save.DailyBest;
            if (best) save.DailyBest = d.Total;
            d.Extra = best ? $"NEW DAILY BEST: {d.Total}  ·  attempt {save.DailyTries} today" : $"Daily best: {save.DailyBest}  ·  attempt {save.DailyTries} today";
        }
        else if (rank > 0 && d.Total > 0) d.Extra = rank == 1 ? "#1 IN THE HALL OF FAME" : $"#{rank} IN THE HALL OF FAME";
        save.Salvage += d.Total;
        save.Runs++;
        save.Best = Math.Max(save.Best, d.Total);
        save.Save();
        foreach (var id in _pendingAch) d.NewAchievements.Add(id);
        _pendingAch.Clear();
        _debrief = d;
        _c.Vega.Clear();
        _c.Fx.Clear();
        _screen = Screen.Debrief;
        _menu.Clear();
        _menu.Add("Back to hangar", ShowHangar, accent: Core);
    }

    // ------------------------------------------------------------------ loop
    public override void _Process(double delta)
    {
        float dt = MathF.Min((float)delta, MaxDt);
        _c.Clock += dt;
        _c.Size = GetViewportRect().Size;

        if (GameInput.Hit(GameInput.Music)) { _musicMuted = !_musicMuted; ApplySettings(); }
        if (GameInput.Hit(GameInput.Fullscreen)) { _c.Save.Fullscreen = !_c.Save.Fullscreen; _c.Save.Save(); ApplySettings(); }

        if (_autotest) AutoTest(dt);
        if (_show != Showcase.None) UpdateShowcase(dt);
        if (_overlay != Overlay.None) UpdateOverlay();
        else UpdateScreen(dt);

        _c.Fx.Update(_overlay == Overlay.None ? dt : 0f);
        _c.Vega.Update(dt, _c.Sfx);
        _c.Ach.Update(dt);
        _c.Music.Set(_screen switch
        {
            Screen.Fly => _fly.Music,
            Screen.Station => _station.Music,
            Screen.Outro => MusicState.None,
            _ => MusicState.Hangar,
        });
        QueueRedraw();
    }

    void UpdateOverlay()
    {
        bool back = GameInput.Hit(GameInput.Back) || GameInput.Hit(GameInput.Pause);
        switch (_overlay)
        {
            case Overlay.Pause:
                if (back) { _overlay = Overlay.None; return; }
                _overlayMenu.HandleInput(_c.Sfx);
                break;
            case Overlay.Settings:
                if (back) { CloseSettings(); return; }
                _overlayMenu.HandleInput(_c.Sfx);
                break;
            case Overlay.Relay:
                _overlayMenu.HandleInput(_c.Sfx);
                break;
            case Overlay.Scores:
            case Overlay.Trophies:
                if (back) { _overlay = Overlay.None; return; }
                _overlayMenu.HandleInput(_c.Sfx);
                break;
        }
    }

    void UpdateScreen(float dt)
    {
        switch (_screen)
        {
            case Screen.Title:
            case Screen.Hangar:
                _menu.HandleInput(_c.Sfx);
                break;

            case Screen.Fly:
                if (GameInput.Hit(GameInput.Pause) && !_c.Run.Over) { _overlay = Overlay.Pause; BuildPauseMenu(); return; }
                _fly.Update(dt);
                if (_fly.Docked) { _station = new StationPhase(_c); _screen = Screen.Station; }
                else if (_c.Run.Over && _c.Run.OverT > 1.8f && !_c.Run.EndHandled) { _c.Run.EndHandled = true; EndRun(); }
                break;

            case Screen.Station:
                if (_station.PendingChoices != null) { BuildRelayMenu(); return; }
                if (GameInput.Hit(GameInput.Pause) && !_c.Run.Over) { _overlay = Overlay.Pause; BuildPauseMenu(); return; }
                _station.Update(dt);
                var run = _c.Run;
                if (run.Over && !run.EndHandled)
                {
                    if (run.Won && run.OverT > 0.5f) { run.EndHandled = true; _outro = new Outro(_c, run.TimeLeft); _screen = Screen.Outro; }
                    else if (!run.Won && run.OverT > 1.8f) { run.EndHandled = true; EndRun(); }
                }
                break;

            case Screen.Outro:
                _outro.Update(dt, GameInput.Hit(GameInput.Confirm) || GameInput.Hit(GameInput.Fire));
                if (_outro.Done)
                {
                    if (_show == Showcase.Trailer) { _card = "end"; _screen = Screen.Title; _menu.Clear(); _endAt = _showT; }
                    else EndRun();
                }
                break;

            case Screen.Debrief:
                UpdateDebrief(dt);
                _menu.HandleInput(_c.Sfx);
                break;
        }
    }

    void UpdateDebrief(float dt)
    {
        var d = _debrief;
        d.T += dt;
        int want = Math.Min(d.Rows.Count, (int)((d.T - 0.3f) / 0.3f) + 1);
        if (want > d.Shown) { d.Shown = want; _c.Sfx.Play(want == d.Rows.Count ? "tick" : "tick_hi", 0.5f); }
        if (CountUpDone(d) && !d.StampPlayed && d.Stamp.Length > 0 && d.T > RevealEnd(d) + CountDur(d) + 0.25f)
        {
            d.StampPlayed = true; _c.Sfx.Play("pop"); _c.Sfx.Play("win", 0.8f); _c.Fx.AddShake(6f);
        }
    }

    static float RevealEnd(DebriefData d) => 0.3f + (d.Rows.Count - 1) * 0.3f;
    static float CountDur(DebriefData d) => MathF.Min(1.2f, 0.3f + d.Total * 0.003f);
    static bool CountUpDone(DebriefData d) => d.T >= RevealEnd(d) + CountDur(d);

    public override void _Input(InputEvent @event)
    {
        var menu = _overlay != Overlay.None ? _overlayMenu : (_screen is Screen.Title or Screen.Hangar or Screen.Debrief ? _menu : null);
        if (menu == null) return;
        var mouse = GetLocalMousePosition();
        if (@event is InputEventMouseMotion) menu.Hover(mouse);
        else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) menu.Click(mouse, _c.Sfx);
    }

    // ------------------------------------------------------------------ drawing
    public override void _Draw()
    {
        var size = _c.Size;
        if (_show == Showcase.Art) { DrawArt(); return; }
        DrawRect(new Rect2(Vector2.Zero, size), Bg);
        switch (_screen)
        {
            case Screen.Title: if (_card.Length > 0) DrawShowcaseCard(); else DrawTitle(); break;
            case Screen.Hangar: HangarScene.Draw(this, _c, PanelW); _c.Fx.DrawWorld(this); DrawHangarPanel(); break;
            case Screen.Fly: _fly.Draw(this); DrawCommonHud(); _fly.DrawHud(this); break;
            case Screen.Station: _station.Draw(this); DrawCommonHud(); _station.DrawHud(this, false); break;
            case Screen.Outro: _outro.Draw(this); break;
            case Screen.Debrief: HangarScene.Draw(this, _c, PanelW); DrawDebriefPanel(); break;
        }
        if (_screen is Screen.Fly or Screen.Station or Screen.Outro or Screen.Hangar) _c.Fx.DrawScreen(this, size);
        _c.Vega.Draw(this, size, _c.Clock);
        switch (_overlay)
        {
            case Overlay.Scores: DrawScores(); break;
            case Overlay.Trophies: DrawTrophies(); break;
            case Overlay.Pause: DrawOverlayList("PAUSED", "Take a breath. The station will wait."); break;
            case Overlay.Settings: DrawOverlayList("SETTINGS", "Left / right to change values"); break;
            case Overlay.Relay: DrawRelay(); break;
        }
        _c.Ach.Draw(this, size);
    }

    void DrawPanel(Rect2 r, Color accent)
    {
        DrawRect(r, new Color(0.016f, 0.03f, 0.063f, 0.9f));
        DrawRect(r, Neon(accent, 1.3f), false, 1.5f);
    }

    void DrawCommonHud()
    {
        var run = _c.Run;
        for (int i = 0; i < run.MaxShield; i++)
        {
            var at = new Vector2(26f + i * 22f, 26f);
            bool full = i < run.Shield;
            ReactorRun.Draw.Poly(this, at, 6, 8f, Mathf.Pi / 6f, full ? Neon(Line, 1.6f) : Alpha(Line, 0.3f), 1.5f);
            if (full) DrawCircle(at, 5f, Alpha(Line, 0.35f));
        }
        ReactorRun.Draw.Text(this, $"SALVAGE {run.Salvage}{(run.Mult > 1 ? " x2" : "")}", new Vector2(16f, 70f), 14, Loot);
        ReactorRun.Draw.Text(this, run.Daily ? $"DAILY RUN · {Today}" : $"STATION {run.Number}", new Vector2(_c.Size.X - 16f, 30f), 14, run.Daily ? Hot : Dim, HorizontalAlignment.Right);
    }

    void DrawTitle()
    {
        var size = _c.Size;
        _c.Stars.Draw(this, size, _c.Clock * 30f, 0f);
        float k = MathF.Min(size.X, size.Y) / 220f;
        DrawSetTransform(size / 2f, 0f, new Vector2(k, k));
        StationPhase.DrawReactor(this, Vector2.Zero, false, _c.Clock);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.016f, 0.02f, 0.043f, 0.6f));

        float w = MathF.Min(620f, size.X - 32f), x = (size.X - w) / 2f, y = MathF.Max(24f, size.Y / 2f - 280f);
        DrawPanel(new Rect2(x, y, w, 540f), Line);
        float lx = x + 28f;
        ReactorRun.Draw.Text(this, "GODOT BUILD 0.6", new Vector2(lx, y + 40f), 12, Dim);
        ReactorRun.Draw.Text(this, "REACTOR RUN", new Vector2(lx, y + 92f), 48, Neon(Line, 1.8f));
        ReactorRun.Draw.Text(this, "Fly in. Blow the core. Get out alive.", new Vector2(lx, y + 124f), 17, Core);
        string[] controls =
        {
            "Move ............ Arrows / WASD / left stick",
            "Thrust .......... Up / W / A button",
            "Fire ............ Space / J / X / right trigger",
            "Arm reactor ..... just touch the core",
            "Overclock ....... hold Q / Y / left trigger as you touch it",
            "Pause ........... P / Esc / Start      Music ... M / Back",
        };
        for (int i = 0; i < controls.Length; i++) ReactorRun.Draw.Text(this, controls[i], new Vector2(lx, y + 168f + i * 24f), 14, Ink);
        for (int i = 0; i < _menu.Items.Count; i++)
        {
            _menu.Items[i].Rect = new Rect2(lx, y + 330f + i * 62f, w - 56f, 50f);
            _menu.DrawItem(this, _menu.Items[i], i, _c.Clock);
        }
    }

    void DrawHangarPanel()
    {
        var size = _c.Size; var save = _c.Save;
        float w = MathF.Min(PanelW, size.X - 32f), x = size.X >= 980f ? size.X - w - size.X * 0.04f : (size.X - w) / 2f;
        float y = 16f, h = size.Y - 32f;
        DrawPanel(new Rect2(x, y, w, h), Line);
        float lx = x + 20f;
        ReactorRun.Draw.Text(this, "HANGAR", new Vector2(lx, y + 30f), 12, Dim);
        ReactorRun.Draw.Text(this, save.Salvage.ToString(), new Vector2(lx, y + 70f), 34, Neon(Loot, 1.5f));
        ReactorRun.Draw.Text(this, "salvage banked", new Vector2(lx + ReactorRun.Draw.TextWidth(save.Salvage.ToString(), 34) + 12f, y + 70f), 13, Dim);
        float rowH = MathF.Min(58f, (h - 300f) / 6f);
        int n = Upgrades.Length;
        for (int i = 0; i < n; i++)
        {
            _menu.Items[i].Rect = new Rect2(lx, y + 88f + i * rowH, w - 40f, rowH - 6f);
            _menu.DrawItem(this, _menu.Items[i], i, _c.Clock);
        }
        float by = y + 88f + n * rowH + 8f;
        var (cols, rows) = StationSize(Tier(save));
        ReactorRun.Draw.Text(this, $"Runs {save.Runs} · Escapes {save.Escapes} · Best haul {save.Best} · Next station {cols}x{rows}", new Vector2(lx, by + 12f), 13, Dim);
        float bw = (w - 52f) / 3f;
        for (int i = n; i < _menu.Items.Count; i++)
        {
            int k = i - n;
            _menu.Items[i].Rect = new Rect2(lx + k % 3 * (bw + 6f), by + 26f + k / 3 * 52f, bw, 46f);
            _menu.DrawItem(this, _menu.Items[i], i, _c.Clock);
        }
    }

    void DrawDebriefPanel()
    {
        var size = _c.Size; var d = _debrief;
        float w = MathF.Min(PanelW, size.X - 32f), x = size.X >= 980f ? size.X - w - size.X * 0.04f : (size.X - w) / 2f;
        float h = 170f + d.Rows.Count * 26f + 110f + (d.Extra.Length > 0 ? 26f : 0f) + d.NewAchievements.Count * 20f, y = MathF.Max(16f, (size.Y - h) / 2f);
        DrawPanel(new Rect2(x, y, w, h), d.Won ? Loot : Hot);
        float lx = x + 24f, rx = x + w - 24f;
        ReactorRun.Draw.Text(this, $"DEBRIEF · STATION {_c.Run.Number}", new Vector2(lx, y + 32f), 12, Dim);
        ReactorRun.Draw.Text(this, d.Head, new Vector2(lx, y + 72f), 30, Neon(d.Won ? Loot : Hot, 1.5f));
        if (d.StampPlayed)
        {
            float sw = ReactorRun.Draw.TextWidth(d.Stamp, 14) + 20f;
            var sr = new Rect2(rx - sw, y + 50f, sw, 28f);
            DrawRect(sr, Neon(Core, 1.6f), false, 2f);
            ReactorRun.Draw.Text(this, d.Stamp, new Vector2(sr.GetCenter().X, y + 69f), 14, Neon(Core, 1.6f), HorizontalAlignment.Center);
        }
        var reason = ReactorRun.Draw.Wrap(d.Reason, w - 48f, 13);
        for (int i = 0; i < reason.Count; i++) ReactorRun.Draw.Text(this, reason[i], new Vector2(lx, y + 100f + i * 17f), 13, Dim);
        float ry = y + 110f + reason.Count * 17f;
        for (int i = 0; i < d.Shown; i++)
        {
            bool last = i == d.Rows.Count - 1;
            var (label, value) = d.Rows[i];
            if (last)
            {
                DrawLine(new Vector2(lx, ry + i * 26f - 16f), new Vector2(rx, ry + i * 26f - 16f), Alpha(Line, 0.3f), 1f);
                float k = MathF.Min(1f, MathF.Max(0f, (d.T - RevealEnd(d)) / CountDur(d)));
                value = Mathf.RoundToInt(d.Total * (1f - MathF.Pow(1f - k, 3f))).ToString();
            }
            var vlines = ReactorRun.Draw.Wrap(value, w * 0.45f, 14);
            ReactorRun.Draw.Text(this, label, new Vector2(lx, ry + i * 26f), 14, last ? Loot : Dim);
            ReactorRun.Draw.Text(this, vlines.Count > 0 ? vlines[0] : value, new Vector2(rx, ry + i * 26f), 14, last ? Neon(Loot, 1.4f) : Ink, HorizontalAlignment.Right);
        }
        float vy = ry + d.Rows.Count * 26f + 6f;
        if (CountUpDone(d) && d.Extra.Length > 0)
        {
            ReactorRun.Draw.Text(this, d.Extra, new Vector2(lx, vy + 8f), 15, Neon(Core, 1.5f));
            vy += 26f;
        }
        if (CountUpDone(d))
            for (int i = 0; i < d.NewAchievements.Count; i++)
            {
                var def = System.Array.Find(Achievements.All, a => a.Id == d.NewAchievements[i]);
                ReactorRun.Draw.Text(this, "UNLOCKED: " + (def?.Name ?? d.NewAchievements[i]), new Vector2(lx, vy + 8f + i * 20f), 13, Core);
            }
        vy += d.NewAchievements.Count * 20f;
        if (CountUpDone(d))
        {
            DrawRect(new Rect2(lx, vy, 2f, 36f), Neon(Line, 1.4f));
            ReactorRun.Draw.Text(this, "VEGA · SHIP AI", new Vector2(lx + 12f, vy + 12f), 11, Line);
            ReactorRun.Draw.Text(this, d.VegaLine, new Vector2(lx + 12f, vy + 30f), 14, Ink);
        }
        _menu.Items[0].Rect = new Rect2(lx, vy + 50f, 220f, 44f);
        _menu.DrawItem(this, _menu.Items[0], 0, _c.Clock);
    }

    void DrawOverlayList(string title, string sub)
    {
        var size = _c.Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.016f, 0.02f, 0.043f, 0.7f));
        float w = MathF.Min(520f, size.X - 32f), h = 120f + _overlayMenu.Items.Count * 58f, x = (size.X - w) / 2f, y = (size.Y - h) / 2f;
        DrawPanel(new Rect2(x, y, w, h), Line);
        ReactorRun.Draw.Text(this, title, new Vector2(x + 24f, y + 44f), 26, Neon(Line, 1.6f));
        ReactorRun.Draw.Text(this, sub, new Vector2(x + 24f, y + 70f), 13, Dim);
        for (int i = 0; i < _overlayMenu.Items.Count; i++)
        {
            _overlayMenu.Items[i].Rect = new Rect2(x + 24f, y + 92f + i * 58f, w - 48f, 50f);
            _overlayMenu.DrawItem(this, _overlayMenu.Items[i], i, _c.Clock);
        }
    }

    void DrawScores()
    {
        var size = _c.Size; var save = _c.Save;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.016f, 0.02f, 0.043f, 0.75f));
        float w = MathF.Min(720f, size.X - 32f), h = MathF.Min(size.Y - 32f, 600f), x = (size.X - w) / 2f, y = (size.Y - h) / 2f;
        DrawPanel(new Rect2(x, y, w, h), Loot);
        float lx = x + 24f;
        ReactorRun.Draw.Text(this, "HALL OF FAME", new Vector2(lx, y + 44f), 26, Neon(Loot, 1.5f));
        ReactorRun.Draw.Text(this, "Your 10 biggest hauls", new Vector2(lx, y + 68f), 13, Dim);
        float[] cols = { 0f, 50f, 170f, 290f, 400f, 490f };
        string[] heads = { "#", "BANKED", "RUN", "RESULT", "CREW", "DATE" };
        for (int c = 0; c < heads.Length; c++) ReactorRun.Draw.Text(this, heads[c], new Vector2(lx + cols[c], y + 100f), 11, Dim);
        if (save.Scores.Count == 0) ReactorRun.Draw.Text(this, "No runs yet. Go blow something up.", new Vector2(lx, y + 130f), 15, Ink);
        for (int i = 0; i < save.Scores.Count; i++)
        {
            var e = save.Scores[i]; float ry = y + 128f + i * 28f;
            var col = i == 0 ? Neon(Core, 1.5f) : Ink;
            ReactorRun.Draw.Text(this, (i + 1).ToString(), new Vector2(lx + cols[0], ry), 15, col);
            ReactorRun.Draw.Text(this, e.Banked.ToString(), new Vector2(lx + cols[1], ry), 15, i == 0 ? col : Loot);
            ReactorRun.Draw.Text(this, e.Daily ? "Daily" : $"Station {e.Station}", new Vector2(lx + cols[2], ry), 14, e.Daily ? Hot : Ink);
            ReactorRun.Draw.Text(this, e.Won ? $"Escaped {e.TimeLeft:0.00}s" : "Lost", new Vector2(lx + cols[3], ry), 14, e.Won ? Ink : Hot);
            ReactorRun.Draw.Text(this, e.Crew.ToString(), new Vector2(lx + cols[4], ry), 14, Ink);
            ReactorRun.Draw.Text(this, e.Date, new Vector2(lx + cols[5], ry), 13, Dim);
        }
        float sy = y + h - 116f;
        DrawLine(new Vector2(lx, sy - 14f), new Vector2(x + w - 24f, sy - 14f), Alpha(Line, 0.3f), 1f);
        ReactorRun.Draw.Text(this, $"Escapes {save.Escapes}   Runs {save.Runs}   Closest call {(save.BestLeft.HasValue ? save.BestLeft.Value.ToString("0.00") + "s" : "-")}   Crew rescued {save.CrewTotal}", new Vector2(lx, sy + 4f), 13, Ink);
        ReactorRun.Draw.Text(this, $"Fighters {save.Stat("kills")}   Turrets {save.Stat("turrets")}   Drones {save.Stat("drones")}   Lifetime salvage {save.Stat("banked")}", new Vector2(lx, sy + 24f), 13, Dim);
        _overlayMenu.Items[0].Rect = new Rect2(lx, y + h - 66f, 180f, 44f);
        _overlayMenu.DrawItem(this, _overlayMenu.Items[0], 0, _c.Clock);
    }

    void DrawTrophies()
    {
        var size = _c.Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.016f, 0.02f, 0.043f, 0.75f));
        var all = Achievements.All;
        int perCol = (all.Length + 1) / 2;
        float w = MathF.Min(900f, size.X - 32f), rowH = 44f, h = MathF.Min(size.Y - 24f, 150f + perCol * rowH), x = (size.X - w) / 2f, y = (size.Y - h) / 2f;
        rowH = MathF.Min(44f, (h - 150f) / perCol);
        DrawPanel(new Rect2(x, y, w, h), Core);
        float lx = x + 24f, colW = (w - 48f) / 2f;
        ReactorRun.Draw.Text(this, "ACHIEVEMENTS", new Vector2(lx, y + 42f), 26, Neon(Core, 1.5f));
        ReactorRun.Draw.Text(this, $"{_c.Ach.Count} of {all.Length} unlocked", new Vector2(lx, y + 64f), 13, Dim);
        for (int i = 0; i < all.Length; i++)
        {
            var a = all[i]; bool got = _c.Ach.Has(a.Id);
            float cx = lx + i / perCol * colW, cy = y + 84f + i % perCol * rowH;
            var c = got ? Neon(Core, 1.6f) : Alpha(Dim, 0.6f);
            ReactorRun.Draw.Poly(this, new Vector2(cx + 12f, cy + 14f), 6, 10f, Mathf.Pi / 6f, c, 1.5f);
            if (got) DrawCircle(new Vector2(cx + 12f, cy + 14f), 5f, Alpha(Core, 0.5f));
            ReactorRun.Draw.Text(this, a.Name, new Vector2(cx + 32f, cy + 12f), 15, got ? Ink : Dim);
            ReactorRun.Draw.Text(this, a.Desc, new Vector2(cx + 32f, cy + 29f), 12, got ? Dim : Alpha(Dim, 0.7f));
        }
        _overlayMenu.Items[0].Rect = new Rect2(lx, y + h - 58f, 180f, 42f);
        _overlayMenu.DrawItem(this, _overlayMenu.Items[0], 0, _c.Clock);
    }

    void DrawRelay()
    {
        var size = _c.Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.016f, 0.02f, 0.043f, 0.6f));
        int n = _overlayMenu.Items.Count;
        float w = MathF.Min(780f, size.X - 32f), cardW = (w - 48f - (n - 1) * 12f) / Math.Max(1, n), h = 330f;
        float x = (size.X - w) / 2f, y = (size.Y - h) / 2f;
        DrawPanel(new Rect2(x, y, w, h), Loot);
        ReactorRun.Draw.Text(this, "RELAY TERMINAL", new Vector2(x + 24f, y + 32f), 12, Dim);
        ReactorRun.Draw.Text(this, "Choose a power-up", new Vector2(x + 24f, y + 68f), 26, Neon(Loot, 1.5f));
        ReactorRun.Draw.Text(this, _relayHint, new Vector2(x + 24f, y + 92f), 13, Dim);
        for (int i = 0; i < n; i++)
        {
            var it = _overlayMenu.Items[i];
            it.Rect = new Rect2(x + 24f + i * (cardW + 12f), y + 112f, cardW, 190f);
            bool sel = i == _overlayMenu.Selected;
            var col = it.Accent;
            DrawRect(it.Rect, Alpha(col, sel ? 0.16f : 0.06f));
            DrawRect(it.Rect, sel ? Neon(col, 1.7f) : Alpha(col, 0.6f), false, sel ? 2f : 1f);
            ReactorRun.Draw.Text(this, (i + 1).ToString(), it.Rect.Position + new Vector2(14f, 22f), 12, Dim);
            ReactorRun.Draw.Text(this, it.Label, it.Rect.Position + new Vector2(14f, 48f), 16, Neon(col, 1.4f));
            var lines = ReactorRun.Draw.Wrap((col == Hot ? "Cursed · " : "") + it.Sub, cardW - 28f, 14);
            for (int l = 0; l < lines.Count; l++) ReactorRun.Draw.Text(this, lines[l], it.Rect.Position + new Vector2(14f, 76f + l * 19f), 14, Ink);
        }
    }
}
