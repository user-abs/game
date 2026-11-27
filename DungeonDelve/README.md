# Dungeon Delve

Top-down dungeon crawler. C# / .NET 10 / Windows Forms / GDI+. No engine.

## Run (Windows)
    dotnet run -c Release

## Build the exe (works on Linux/Codespaces too)
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./DungeonDelve-Windows

## Controls
WASD / Arrows  move          Mouse  aim
Left click  sword swing      Right click  fireball (mana)
Space  dash (i-frames)       Q / F  health / mana potion
E  take stairs down          Esc / P  pause          F11  fullscreen
Level-up: press 1, 2 or 3 to pick a perk
