# Monsterpatch Catch Bottle Rates Patcher

Monsterpatch Catch Bottle Rates Patcher is a BepInEx 5 preloader patcher for Monsterpatch. It makes capture bottles matter by giving special bottles house-affinity catch bonuses and by updating the visible catch difficulty text as soon as the selected bottle changes.

This is a patcher, not a plugin. Install the built DLL in `BepInEx\patchers`.

## Version

`v0.1.0`

## What it does

Vanilla Monsterpatch uses the mon's base catch difficulty only:

| Catch text | Base chance |
|---|---:|
| EASY | 90% |
| MEDIUM | 60% |
| HARD | 30% |
| VERY HARD | 10% |

This patcher keeps those base rates, then applies a flat bonus when the selected bottle matches one or both of the mon's active houses.

| Match type | Bonus |
|---|---:|
| No matching house | +0% |
| One matching house | +15% |
| Two matching houses | +25% |

Final catch chance is capped at `95%`.

The patcher also recalculates the preview text from the final catch chance, so switching bottles immediately updates the displayed `EASY`, `MEDIUM`, `HARD`, or `VERY HARD` text before attempting the catch.

## Bottle affinity mapping

| Bottle | Matching houses |
|---|---|
| Bottle | None |
| Diamond Bottle | ATLANTIAN, IRONCLAD |
| Heart Bottle | WHIMSICAL, MYSTIC |
| Guild Bottle | FIREBORN, BRAWLER |
| Void Bottle | NIGHTWATCH, DRAGOON |
| Spice Bottle | OVERGROWTH, FIREBORN |

Bottle multipliers are intentionally not used in this release. All bottles stay at the normal `1.00x` base rate, with only the house-affinity bonus changing the final catch chance.

## Shiny house support

The patcher follows the game's active-house behavior:

- normal mons use `MonScriptableObject.houses`
- shiny mons use `MonScriptableObject.housesShiny` when present

That means a shiny mon can have different bottle affinities than its normal version.

## Install

Build the project, then copy:

```text
Monsterpatch_CatchBottleRates_Patcher.dll
```

into:

```text
Monsterpatch\BepInEx\patchers\
```

Do not install it in `BepInEx\plugins`.

Remove any old test versions of this patcher before installing the final build, so only one catch bottle patcher is active.

## Build

Default source layout expected by the build scripts:

```text
C:\Monsterpatch_Mods\lib
C:\Monsterpatch_Mods\Monsterpatch_CatchBottleRates_Patcher_Source_v0.1.0_Final
```

Required shared library file:

```text
C:\Monsterpatch_Mods\lib\Mono.Cecil.dll
```

You can copy `Mono.Cecil.dll` from:

```text
Monsterpatch\BepInEx\core\Mono.Cecil.dll
```

Build with:

```bat
build.bat
```

or:

```powershell
.\build.ps1
```

or directly:

```bat
dotnet build src\Monsterpatch_CatchBottleRates_Patcher.csproj -c Release
```

Build output is copied to:

```text
dist\BepInEx\patchers\Monsterpatch_CatchBottleRates_Patcher.dll
```

## Notes for publishing

This repository is intended to contain source code, build scripts, README, and license only. Do not commit extracted Monsterpatch assets, decompiled game code, or built release DLLs directly into the source repository.
