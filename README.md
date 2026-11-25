# Monsterpatch Bottle House Modifiers v0.2.0

BepInEx 5 preloader patcher for the November 24, 2026 Monsterpatch beta dump. This is a patcher, not a plugin.

## Capture rules

Beta base catch chances: EASY 90%, MEDIUM 70%, HARD 40%, VERY HARD 15%. The game applies its cafe bonus and rounding first. Matching one active house adds 15 percentage points; matching two adds 25. The bottle bonus is capped at 95%, but never reduces the vanilla chance. Unmatched bottles retain vanilla odds. Shiny houses are used when present.

| Bottle | Houses |
| --- | --- |
| Diamond (109) | ATLANTIAN, IRONCLAD |
| Heart (110) | WHIMSICAL, MYSTIC |
| Guild (111) | FIREBORN, BRAWLER |
| Void (112) | NIGHTWATCH, DRAGOON |
| Spice (113) | OVERGROWTH, FIREBORN |

Basic bottle 108 and IDs 114–125 receive no added affinity; no new mappings have been verified. The preview uses beta effective difficulty and cafe rounding, with tier thresholds of 90/70/40. Tier text need not change for every bonus within a tier.

## Build and install

Run `build.bat` from the repository. Requires .NET SDK and Mono.Cecil.dll from BepInEx 5, either in sibling `lib` or the default Steam game's BepInEx/core directory. Optional arguments: `build.bat "path to lib" "path to Monsterpatch"`.

Output: `Release/BepInEx/patchers/Monsterpatch_CatchBottleRates_Patcher.dll`.

Drag the BepInEx folder into the game directory. Remove older copies first. Do not place the DLL in plugins.

The patcher requires all four hooks and logs their installation in the preloader console. This version targets the beta, not older game builds. Static comparison against the beta C# and IL is complete; compilation and gameplay testing remain to be performed locally.
