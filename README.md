# Reactor Run (Godot 4.7 .NET)

Port of browser prototype build 0.5. Tuning numbers match the "Build 0.5 spec" tab of the pitch doc.

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
