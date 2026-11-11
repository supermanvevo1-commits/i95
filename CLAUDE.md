# Survive the I-95: working rules

An arcade survival driving game along I-95, from Miami to Maine. Unity 6 (6000.6.8f1), URP. The creator is often away from the PC, so Claude builds, tests in Play mode, and fixes on its own.

## Read first
- `Docs/ROADMAP.md`: phases and milestones, and what's done. The first unchecked step in the current milestone is next.
- `Docs/VISION.md`: the creator's design, decisions and research numbers.
- `Docs/STATES.md`: speed limits, weather, police, tolls and character for each state.
- `Docs/npc_behaviors.md`: the NPC wishlist.
- The shareable game design doc for the creator and Noah is the Claude Doc "Survive the I-95: Game Design" (https://claude.ai/artifact/JUggd3xGSXasPELeM9vHZv). Refresh it at the end of each milestone.

## How each step is done
1. Build the step, test it live in Play mode, and measure it with numbers where possible.
2. Give it a second look before checking it off. Does it behave like real driving and real drivers on that road? Does it fit with the steps before it? Rework if not. Write the findings under the step in ROADMAP.md.
3. Visual steps get before-and-after screenshots from the same spot. Describe what each screenshot shows, what looks wrong, and what to fix.
4. Report in plain language. The creator is not a programmer.
5. Ground behavior in real data (surveys, crash statistics, laws). Rare events stay rare.

## Git
- Commit only when the creator says "commit". Never commit on your own: the permission check blocks it.
- Branch `m3-traffic`. Remote: `github.com/supermanvevo1-commits/i95`.
- git on the command line has no saved login. Push through GitHub Desktop with `Tools/desktop_push.ps1 -Send`, which brings its window forward and presses Ctrl+P. Then confirm with `git ls-remote --heads origin`.
- PowerShell 5.1 can't pass a here-string to `git commit -F -`. Write the message to a file and use `-F <file>`.

## Unity live bridge
- `Tools/live.ps1 <command> [args]` sends commands to the open editor through `.livebridge/`. Commands are in `Assets/Editor/LiveBridge.cs` (`Run`): play, stop, refresh, rebuild, launch, drive, cruise, place, shot, status, traffic, cars, speeds, profiles, tempers, regulars, notices, trafficbench and more.
- Screenshots land in `.livebridge/shots/`.
- `trafficbench <seconds> <cars> <anchor m/s> <seed> [season]` runs traffic without rendering, much faster than real time, and reports lane speeds, age bands, plates and driver profiles. Unprovoked and chain crashes must stay at 0.
- `Tools/compile.ps1` stops Play mode, refreshes, and waits for the compile.
- Compile errors do NOT appear in `.livebridge/console.log`. After a refresh, compare the timestamps of `Library/ScriptAssemblies/Assembly-CSharp.dll` and `Assembly-CSharp-Editor.dll` with your edit. A stale dll means the compile failed: Unity keeps running old code and Play mode silently won't start. The errors are in `Library/Bee/tundra.log.json` (search for `error CS`).
- `LiveBridge.Run` is one big switch. Unbraced cases declare locals (`r`, `s`) for the whole switch, so don't reuse those names.
- Lighting comes from `Assets/Resources/Data/looks.json`, applied by `Assets/Editor/LookBuilder.cs` (sky, sun, haze, shade light, grade, bloom). After editing the file, `relight` re-applies it to the open scene without a rebuild (saved in edit mode; in Play mode it lasts until stop, and the first frame after it can be garbage). `sunshot <name>` takes a screenshot aimed at the sun, which must be in the center. `ambient` prints the shade light and haze as the renderer has them.
- Unity treats `RenderSettings` ambient and fog colors as screen (gamma) values. Light measured in linear units needs `.gamma` first, or shade comes out navy and far too dark.
- Shadow distance, shadow map size, edge smoothing and ambient occlusion are in `Assets/Settings/PC_RPAsset.asset` and `PC_Renderer.asset`.
- `rebuildpart signs` or `rebuildpart barriers` rebuilds just that part of the open scene in seconds, for trying a look. It does not rewrite what you can hit, so finish with a full `rebuild`. `signcheck` counts sign text that sticks out of its panel and must report 0.
- `steerbench [car]` compares keyboard steering laws without rendering (dodge time, tap strength, spin). `kbswerve <mph> <secRight> <secLeft>` then `swerveresult` runs an exactly timed dodge in the running game.
- Test bots stay attached to the player's car after a test. Always finish with `release` (or `stop`), or the creator finds the car driving itself with a jittering wheel.
- After bumping `SliceAutoRebuild.Version`, the automatic rebuild does not always start. Check `Assets/Generated/build_version.txt`; if it is stale, run `rebuild`.
- `rebuild` regenerates the Miami scene (`Assets/Generated/FloridaSlice.unity`) and can take minutes. The bridge may time out while it keeps working.

## Code map
- `Assets/Scripts/Sim/`: plain C# simulation with no scene objects (server-friendly for co-op later).
  - `DriveSim`: the player's car.
  - `TrafficSim`: traffic. Partial class with `TrafficRegulars` (grudges, road rage), `TrafficSpeed` (speed habits), `TrafficDrivers` (driver profiles).
- `Assets/Scripts/World/RoutePath.cs`: the road, lanes, exits and speed limits.
- `Assets/Scripts/Game/`: Unity side: player car, traffic views and models, mirrors.
- `Assets/Resources/Data/`: vehicles, driver profiles, state traffic mixes, routes.
- `Tools/*.js`: map data importers (OpenStreetMap, USGS). Run with node.

## Don'ts
- Don't copy code or files from other games, or from GPL projects.
- Don't print the `SKETCHFAB_TOKEN` environment variable.
- Ask before installing new software on the creator's PC.
