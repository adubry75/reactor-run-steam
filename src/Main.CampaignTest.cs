using Godot;

namespace ReactorRun;

/// <summary>godot -- --campaigntest : scripted pass over the campaign flow (ship lost, game over + initials, final station, victory).</summary>
public partial class Main
{
    bool _ctest;
    float _ctT;
    int _ctStep;

    void CampaignTest(float dt)
    {
        _ctT += dt;
        void Lose() { _c.Run.Salvage = 300; _c.Run.Over = true; _c.Run.Won = false; _c.Run.Reason = "Hull breached."; EndRun(); }
        void Log(string tag) => GD.Print($"[ctest] {tag}: screen={_screen} head={_debrief?.Head} escapes={_c.Save.Escapes} total={_c.Save.TotalScore} lives={_c.Save.Lives} initials={_debrief?.Initials} msg={_debrief?.SubmitMsg} campaignTop={_c.Save.CampaignScores.Count} stationTop={_c.Save.StationScores.Count}");
        (float at, System.Action act)[] steps =
        {
            (0.2f, () => { _c.Save.NoPersist = true; _c.Save.ResetProgress(); _c.Save.Escapes = 14; _c.Save.TotalScore = 5000; foreach (var u in Config.Upgrades) _c.Save.Up[u.Key] = u.Cost.Length; ShowTitle(); }),
            (1.0f, () => Shot("c_title")),
            (1.2f, ShowHangar),
            (2.0f, () => Shot("c_hangar")),
            (2.2f, () => StartRun(false)),
            (3.4f, () => Shot("c_fly_hud")),
            (3.6f, () => { _station = new StationPhase(_c); _screen = Screen.Station; }),
            (4.6f, () => Shot("c_station_hud")),
            (4.8f, Lose),
            (7.4f, () => { Shot("c_shiplost"); Log("shiplost"); }),
            (7.6f, () => { ShowHangar(); _c.Save.Lives = 1; StartRun(false); }),
            (8.0f, Lose),
            (10.8f, () => { Shot("c_gameover"); Log("gameover"); TypeInitial('c'); TypeInitial('l'); TypeInitial('e'); }),
            (11.0f, () => { Shot("c_initials"); SubmitInitials(); }),
            (14.0f, () => { Shot("c_gameover_sent"); Log("sent"); }),
            (14.2f, () => _menu.Items[0].Activate()),
            (15.0f, () => { Shot("c_title_after"); Log("after-gameover"); }),
            (15.2f, () => { _c.Save.Escapes = 23; _c.Save.TotalScore = 40000; ShowHangar(); }),
            (16.0f, () => Shot("c_hangar_final")),
            (16.2f, () => StartRun(false)),
            (17.4f, () => { Shot("c_final_fly"); Log($"final={_c.Run.Final}"); }),
            (17.6f, () => { _station = new StationPhase(_c); _screen = Screen.Station; }),
            (18.6f, () => Shot("c_final_station")),
            (18.8f, () => { _c.Run.Won = true; _c.Run.TimeLeft = 2f; _c.Run.Salvage = 900; _c.Run.CrewCarried = 4; _c.Run.Over = true; EndRun(); }),
            (22.0f, () => { Shot("c_victory"); Log("victory"); }),
            (22.2f, () => { FinishInitials("skipped"); _menu.Items[0].Activate(); OpenScores(); }),
            (23.0f, () => { Shot("c_halloffame"); Log("end"); }),
            (23.2f, () => GetTree().Quit()),
        };
        while (_ctStep < steps.Length && _ctT >= steps[_ctStep].at) { steps[_ctStep].act(); _ctStep++; }
    }
}

public partial class Main
{
    bool _chtest; float _chT; int _chStep;
    void SendKey(Godot.Key k, char ch = (char)0) => HandleCheatKey(new InputEventKey { Keycode = k, Pressed = true, Unicode = ch });
    void CheatTest(float dt)
    {
        _chT += dt;
        void Log(string tag) => GD.Print($"[cheat] {tag}: screen={_screen} unlocked={_c.Save.CheatsUnlocked} cheated={_c.Save.Cheated} god={_c.God} escapes={_c.Save.Escapes} salvage={_c.Save.Salvage} lives={_c.Save.Lives} total={_c.Save.TotalScore} head={_debrief?.Head} extra={_debrief?.Extra} initials={_debrief?.Initials} stationTop={_c.Save.StationScores.Count}");
        (float at, System.Action act)[] steps =
        {
            (0.2f, () => { _c.Save.NoPersist = true; _c.Save.ResetProgress(); ShowHangar(); }),
            (0.5f, () => { foreach (var ch in "cheat") SendKey(Godot.Key.A, ch); Log("typed CHEAT"); }),
            (0.8f, () => Shot("ch_unlocked")),
            (1.0f, () => { SendKey(Godot.Key.F3); SendKey(Godot.Key.F4); SendKey(Godot.Key.F4); Log("F3 F4 F4"); }),
            (1.2f, () => { StartRun(false); SendKey(Godot.Key.F1); }),
            (1.6f, () => { _c.Hurt(Vector2.Zero); _c.Hurt(Vector2.Zero); Log($"god hurt shield={_c.Run.Shield}"); SendKey(Godot.Key.F2); Log("F2 in fly"); }),
            (2.6f, () => { Shot("ch_station"); SendKey(Godot.Key.F2); }),
            (7.0f, () => { Log("after escape"); Shot("ch_debrief"); }),
            (7.2f, () => { ShowHangar(); SendKey(Godot.Key.F5); SendKey(Godot.Key.F6); Log("F5 F6"); }),
            (7.4f, () => GetTree().Quit()),
        };
        while (_chStep < steps.Length && _chT >= steps[_chStep].at) { steps[_chStep].act(); _chStep++; }
    }
}
