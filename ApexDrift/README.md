# Apex Drift

Top-down arcade racing game. C# / .NET 10 / Windows Forms / GDI+. No engine.

## Run (Windows)
    dotnet run -c Release

## Build the exe (works on Linux/Codespaces too)
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./ApexDrift-Windows

## Controls
W/S or Up/Down  throttle / brake+reverse
A/D or Left/Right  steer
SPACE  handbrake (drift)
SHIFT  nitro
R  respawn on track      C  toggle rotating camera
P  pause                 F11  fullscreen
Menu: Left/Right track, Up/Down laps, Tab difficulty, Enter start
