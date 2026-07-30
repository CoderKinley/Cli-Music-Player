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

## Requirements

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

## License

License information will be added before the first packaged release.
