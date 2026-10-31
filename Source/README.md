# Create a Colonist: developer notes

## Layout
- `About/`: mod info shown in RimWorld's mod list.
- `1.7/Assemblies/CreateAColonist.dll`: the compiled code (built from `Source/`).
- `Defs/`, `Patches/`, `Languages/`, `Textures/`: XML, translations and icons.
- `Source/CreateAColonist/`: C# source. Native RimWorld 1.7 build (.NET 10, compiled against the game's
  `Assembly-CSharp.dll`, `UnityStubs.dll` and `Waylith.dll`).
- `Source/Art/make_icons.py`: draws every icon in `Textures/UI/CreateAColonist` (needs Python + Pillow).

The game sees this folder through a link: `RimWorld/Mods/CreateAColonist` points here.

## Build
```
powershell -ExecutionPolicy Bypass -File build.ps1        # release build: the one to publish
powershell -ExecutionPolicy Bypass -File build.ps1 -Dev   # adds the in-game test tooling (DevTest.cs)
```
Only publish a release build. `run-test.ps1` with a test flag switches to the dev build automatically, so run
`build.ps1` again before uploading.
Uses the .NET 10 SDK in `%LOCALAPPDATA%\Microsoft\dotnet10`. Build leftovers go to
`%LOCALAPPDATA%\CreateAColonistBuild` (not the mod folder, because RimWorld's Workshop uploader copies every
file in the mod folder). Debug info is embedded in the DLL with source paths rewritten, so no local paths ship.

## Test
Adding `-Headless` to the game's command line alongside `-cacTest -cacExplore -cacTour` runs the behaviour scenes and a colony start with no window, so it can run beside a game in progress. Nothing draws, so looks and layout still need a normal run.

```
powershell -ExecutionPolicy Bypass -File run-test.ps1          # normal start, test profile
powershell -ExecutionPolicy Bypass -File run-test.ps1 -Quick   # straight into the creator
powershell -ExecutionPolicy Bypass -File run-test.ps1 -Tour    # automatic click-through with screenshots
powershell -ExecutionPolicy Bypass -File run-test.ps1 -Explore # every feature's edge cases + layout lint (~15 s), then starts the colony
powershell -ExecutionPolicy Bypass -File run-test.ps1 -Menu    # New Colony popup, then the Normal path
```
Add `-Full` to any of these to use the second test profile, which has a copy of the full everyday mod list.
The test profile lives in `%USERPROFILE%\AppData\LocalLow\CreateAColonist-TestProfile` (its own mod list,
settings and saves; the full one is `...TestProfileFull`). Dev builds also log `[CAC perf]` draw times and memory per tour step and `[CAC bench]` one-off costs (pickers, snapshots, engine calls), and the tour ends by leaving the colony to check the game leaves memory. Tour screenshots go to its `Screenshots` folder. The game log is always
`...\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`.

## How it hooks in
- `Patches/CreatorPage.xml` points the scenario's "starting people" part at `Page_CreateAColonist`.
  That page is a subclass of the vanilla page and behaves exactly like it unless Character Creation was chosen.
- One Harmony patch asks "Normal or Character Creation?" the first time the scenario page draws.
- On Play, the creator copies the scenario with the edited supplies and pets (`CreatorState.ApplyToScenario`).
  The copy carries one extra part, `ScenPart_CreatorPets`, which does the rest only once the colony is really
  being generated (after the game's own "nobody can do X" warning): it settles the colony's ideoligions, delivers
  the pets, and drops anything over a colonist's carry limit next to them. It removes itself after the game
  starts, so saves don't depend on this mod.
- Gallery presets are saved in `<save data>/CreateAColonist/*.xml` as defs and values, so they load into any colony.
  Saved colonies go in `<save data>/CreateAColonist/Colonies/*.xml`: every colonist, the family tree around them
  (`FamilyTree`: relatives outside the colony are rebuilt as world pawns), pets with their bonds and coats, and the
  colony supplies. A dev-build tour step saves a colony, scrambles the group and loads it back, comparing everything.
