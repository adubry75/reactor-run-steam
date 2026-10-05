# Reactor Run (Godot 4.7 .NET)

Port of the browser version, now at build 0.7 (24-station campaign, total score, 3 ships, game over, top 10s). Tuning numbers match the "Build 0.5 spec" tab of the pitch doc.

## Requirements
- Godot 4.7.x **.NET** edition
- .NET 10 SDK

## Layout
- `src/Main.cs`: state machine, screens, overlays, glow and CRT setup
- `src/Core`: config/tuning, save data (user://save.json), input map (keyboard + gamepad), music and SFX, FX and VEGA
- `src/Game`: dogfight, station generator, station gameplay, escape cinematic
- `src/UI`: canvas menus and the hangar scene
- `shaders/crt.gdshader`, `music/` (Suno, free plan, NOT commercial), `sfx/` (synthesized WAVs)

## Notes
- Everything is drawn in code (`_Draw`), with HDR 2D on, so colors above 1.0 feed the glow.
- Input actions are registered at startup in `GameInput.Register()`, not in project.godot.
- If your Godot version differs, change the SDK version on line 1 of `ReactorRun.csproj` to match.

## Self-test
Run `godot --path . -- --autotest` to play a scripted run (hangar, dogfight, station, relay, reactor, escape, debrief).
It saves a screenshot of each stage to the user data folder as `autotest_*.png`, then wipes its test progress and quits.

## Campaign (build 0.7)
- 24 stations; station 24 is the FINAL STATION (score x3). 3 ships; lose them all and it's game over.
- Every escape adds `banked salvage x (1 + 0.1 x escapes)` to the total score (shown top-left).
- Top 10s: campaign total and best single station, on this PC and online (shared with the web version via
  `https://dubry.com/games/ReactorRun/scores.php`; set `RR_SCORES_URL` to point elsewhere).
- `godot --path . -- --campaigntest` plays through ship lost, game over + initials, final station and victory.
