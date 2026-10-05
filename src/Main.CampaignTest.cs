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
