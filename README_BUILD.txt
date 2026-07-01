Monsterpatch Catch Bottle Rates Patcher Source v0.1.0 Final

This is a BepInEx 5 preloader patcher source package.
Install the built DLL in:
  BepInEx\patchers\Monsterpatch_CatchBottleRates_Patcher.dll

Do not install it in BepInEx\plugins.

Build layout expected by default:
  C:\Monsterpatch_Mods\lib
  C:\Monsterpatch_Mods\Monsterpatch_CatchBottleRates_Patcher_Source_v0.1.0_Final

Required shared lib file:
  C:\Monsterpatch_Mods\lib\Mono.Cecil.dll

You can copy Mono.Cecil.dll from:
  Monsterpatch\BepInEx\core\Mono.Cecil.dll

Build:
  build.bat

or:
  dotnet build src\Monsterpatch_CatchBottleRates_Patcher.csproj -c Release

Output:
  dist\BepInEx\patchers\Monsterpatch_CatchBottleRates_Patcher.dll

Final v0.1.0 behavior:
- Bottle multipliers are removed/reverted. Every bottle is 1.00x.
- Catch chance now only changes from house affinity bonuses.
- One matching active house adds +15 percentage points.
- Two matching active houses adds +25 percentage points.
- Final chance is capped at 95%.
- The preview EASY / MEDIUM / HARD / VERY HARD text is recalculated from the selected bottle and active mon houses.
- The real capture roll uses the same final chance as the preview.
- Shiny mons use housesShiny when present, matching the game's active-house behavior.

Bottle affinity mapping:
  Diamond Bottle = ATLANTIAN, IRONCLAD
  Heart Bottle   = WHIMSICAL, MYSTIC
  Guild Bottle   = FIREBORN, BRAWLER
  Void Bottle    = NIGHTWATCH, DRAGOON
  Spice Bottle   = OVERGROWTH, FIREBORN
  Basic Bottle   = no house bonus
