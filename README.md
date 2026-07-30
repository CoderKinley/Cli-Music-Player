# KMusiKPlayer CLI

A keyboard-driven YouTube music player for the Windows terminal.

## Features

- Search and play music from YouTube
- Keyboard-first terminal interface
- Persistent favorites and recently played tracks
- Automatic queues, looping, shuffle, next, and previous controls
- Pause, resume, seek, stop, and live volume control
- Previous-session playback restoration
- Spotify and SoundCloud terminal themes
- Responsive, low-flicker terminal layout

## Install

Download `Musik-Setup.exe` from the
[latest GitHub release](https://github.com/CoderKiLe/KMusiKPlayer-Cli/releases/latest).
After installation, open a new terminal and run:

```powershell
musik
```

Check for and install updates with:

```powershell
musik update
```

## Source requirements

- Windows
- .NET 10 SDK when running from source
- `mpv` available on `PATH`

## Run from source

```powershell
dotnet run --project .\KMusicPlayer
```

## Keyboard controls

| Key | Action |
| --- | --- |
| Arrow keys | Navigate |
| Enter | Select or play |
| Space | Pause, resume, or restore the previous session |
| F | Add or remove a favorite |
| R | Toggle shuffle |
| N / P | Next / previous track |
| `,` / `.` | Rewind / fast-forward 10 seconds |
| `-` / `+` | Lower / raise volume |
| S | Stop |
| Q / Escape | Return Home or exit |

## Build

```powershell
dotnet build .\KMusicPlayer.slnx -c Release
```

## Release build

```powershell
choco install mpvio.install --version 0.41.0 -y
.\build\release.ps1 -Version 1.0.0
```

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for bundled component
licenses and source information.

## License

Copyright (c) 2026 CoderKiLe. All rights reserved.
