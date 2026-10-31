# Create a Colonist

Create a Colonist turns RimWorld's character screen into a proper character creator, inspired by Create a Sim from The Sims 4. It's built natively for RimWorld 1.7.

Players should get it from the [Steam Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=3826119441), which is where updates arrive. This repository is the source, for anyone who wants to read it, build it or make their mod work alongside it.

## Building

You need RimWorld 1.7, the Harmony mod from the Workshop and the .NET 10 SDK. The compiled DLL isn't kept here, so build it from `Source`:

```
powershell -ExecutionPolicy Bypass -File Source/build.ps1
```

That puts `CreateAColonist.dll` in `1.7/Assemblies`. If your game or Harmony is somewhere else, set `RimWorldDir` and `HarmonyDir`, which are in `Source/CreateAColonist/CreateAColonist.csproj`. `Source/README.md` covers the build and the test tooling in more detail.

## Integrating with it

The creator is `Page_CreateAColonist`, which subclasses the game's `Page_ConfigureStartingPawns`. Every change it makes to a colonist goes through `PawnEditor`, and saved colonists are `ColonistPreset`. Colonists are made with the game's own generator, so mods that hook pawn generation already apply.

## Licence

MIT. See [LICENSE](LICENSE).
