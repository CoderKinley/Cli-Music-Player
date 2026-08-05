# Cli Music Player

A keyboard-driven YouTube music player for the Windows terminal.

## Features

- Search and play music from YouTube
- Load and play complete YouTube playlists from a pasted link
- Save, revisit, create, and delete playlists; add online or local tracks with `A`
- Keyboard-first terminal interface
- Persistent favorites and recently played tracks
- Automatic queues, looping, shuffle, next, and previous controls
- Pause, resume, seek, stop, and live volume control
- Automatic lyrics lookup with locally saved manual lyrics fallback
- Download selected audio tracks to your Music folder
- Offline local-music library with a configurable folder
- Export and import all application settings as one JSON backup
- Previous-session playback restoration
- Spotify and SoundCloud terminal themes
- Responsive, low-flicker terminal layout

## Install

Download `Musik-Setup.exe` from the
[latest GitHub release](https://github.com/CoderKinley/Cli-Music-Player/releases/latest).
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
| L | Open lyrics for the selected or currently playing track |
| D | Download the selected or currently playing track |
| A | Add the selected track to a saved playlist |
| R | Toggle shuffle |
| N / P | Next / previous track |
| `,` / `.` | Rewind / fast-forward 10 seconds |
| `-` / `+` | Lower / raise volume |
| S | Stop |
| Q / Escape | Return Home or exit |

Lyrics are fetched from LRCLIB when available and cached in
`%LOCALAPPDATA%\KMusicPlayer\lyrics.json`. If no result is found, press `E`
on the lyrics page, paste the lyrics, and enter `.save` on a new line.

Downloads are saved in `%USERPROFILE%\Music\Musik Downloads` using the source
audio format. Only download content you have permission to save.

Open **Local Music** from the Home screen to play downloaded files offline. The
default folder is `Music\Musik Downloads`; choose **Change music folder** to use
another directory. The selection is saved in
`%LOCALAPPDATA%\KMusicPlayer\local-library.json`.

Use **Backup & Restore** on the Home screen to export favorites, recent tracks,
volume, shuffle, theme, playback session, saved lyrics, and the local-library
folder. Backups are written to `Documents\Musik Backups`. Importing first creates
a safety backup, restores the selected JSON file, and closes Musik so the restored
state can load cleanly on the next launch.

The **Playlists** Home entry is a persistent playlist library. Use `I`
to import and save a YouTube playlist, `N` to create an empty playlist, Enter to
open one, and Delete/Backspace to remove one. Saved playlists are stored in
`%LOCALAPPDATA%\KMusicPlayer\playlists.json` and are included in settings backups.

## Build

```powershell
dotnet build .\KMusicPlayer.slnx -c Release
```

## Publishing a release

For the complete maintainer checklist, commands, versioning rules, verification,
and failure recovery steps, see [RELEASE-GUIDE.md](RELEASE-GUIDE.md).

Installed copies use Velopack and the public GitHub Releases feed. To publish a
new version, choose a version higher than the current release. For example, to
upgrade users from `1.1.0` to `1.2.0`:

1. Create `releases\v1.2.0.md` containing the release notes.
2. Commit and push all changes to the main branch.
3. Create and push the matching version tag:

```powershell
git tag v1.2.0
git push origin v1.2.0
```

The tag starts the GitHub Actions release workflow. It builds the application,
creates the Velopack installer and update packages, and publishes them to a
GitHub Release. Do not reuse or move an existing release tag; always publish a
higher semantic version.

Users who already installed Musik can then update with:

```powershell
musik update
```

The updater downloads the new package, applies it, and restarts Musik. User data
under `%LOCALAPPDATA%\KMusicPlayer` is outside the installation directory and is
preserved during application updates.

To build the same release artifacts locally without publishing a GitHub Release:

```powershell
choco install mpvio.install --version 0.41.0 -y
.\build\release.ps1 -Version 1.2.0
```

## Working Images
<img width="803" height="443" alt="image" src="https://github.com/user-attachments/assets/ad3a5365-23c0-40e8-8db2-7a86cf6125ed" />

<img width="788" height="440" alt="image" src="https://github.com/user-attachments/assets/bd49658b-0689-47d9-b786-b3e76a8b2d71" />

<img width="787" height="440" alt="image" src="https://github.com/user-attachments/assets/2972d51b-a945-4260-84be-781fb8d90f31" />

<img width="807" height="445" alt="image" src="https://github.com/user-attachments/assets/abd59153-bff3-4f00-bf73-edc4aafff3b9" />

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for bundled component
licenses and source information.

## License

Copyright (c) 2026 CoderKiLe. All rights reserved.
