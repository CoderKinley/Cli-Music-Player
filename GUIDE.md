# Musik keyboard and usage guide

Open **Keyboard Guide** from Home, or press **F1** on a browsing screen or text
prompt. **?** also opens the guide on browsing screens. In the guide, use
Left/Right or Tab/Shift+Tab to change category, `/` to find a shortcut by key or
keyword, Backspace to clear the filter, and Q/Escape to return to your screen.

Keys depend on the screen: `N` means **next song** in a song list and **new
playlist** in the playlist library. `I` means **song information** in a song list
and **import** in the playlist library. Single letters do not require Ctrl.

## Navigation

| Key | Action | Where |
| --- | --- | --- |
| Up / Down | Move the selection | Home, lists, playlists, and themes |
| Tab / Shift+Tab | Focus the next / previous panel | Home |
| Left / Right | Focus the previous / next panel | Home; changes category in the guide |
| Enter | Open a page or play a song | Menus and song lists |
| Page Up / Page Down | Move by one page | Song lists, playlist library, guide, details, and lyrics |
| Home / End | Jump to the beginning / end | Song lists, playlist library, guide, details, and lyrics |
| F1 / ? | Open the keyboard guide | Browsing screens; F1 also works in search and playlist text fields |
| I | Read the full title, artist, duration, and source | Home and song lists |
| Q / Escape | Go back; exit when on Home | Browsing screens |

In a text field, Escape cancels and all letters, including Q, become part of
your text. Long queries scroll within the field. Backspace removes the
previous character, including a complete emoji or combining character.

## Playback

These controls work on Home and in song lists.

| Key | Action |
| --- | --- |
| Enter | Play the selected song and start its queue |
| Space | Pause, resume, or restore the saved previous session |
| N / P | Next / previous song |
| R | Toggle saved shuffle mode |
| S | Stop playback |
| `,` / `.` | Seek backward / forward 10 seconds |
| `-` / `+` | Lower / raise volume by 5%; numeric keypad keys also work |
| F | Add or remove the selected song as a favorite |
| A | Add the selected song to a saved playlist |
| L | Open lyrics for the selected song |
| D | Download the selected online song |

On Home, song actions use the selected song in the focused panel. When Quick
Links has focus, they use the currently playing song. The player border shows
playback state, volume, and shuffle. A separate status line shows action results
even while music is playing.

## Search

1. Select Search on Home or press `/` on Home or in a song list.
2. Type a song, artist, or album and press Enter.
3. Navigate the first five results. Press `E` to expand or collapse the list.
4. Press Enter to play, `I` to inspect a long name, `F` to favorite, or `A` to
   save a song to a playlist.

Press `/` in search results to enter another query. Q/Escape leaves the results
and returns to the search prompt; Escape there returns to the previous page.

## Playlists

| Key | In the playlist library | Inside an open playlist |
| --- | --- | --- |
| I | Import a public or unlisted YouTube playlist URL | Inspect the selected song |
| N | Create an empty playlist | Play the next song |
| Enter | Open the selected playlist | Play the selected song |
| Delete / Backspace | Delete the selected playlist | Remove only the selected song |
| A | — | Add the selected song to another saved playlist |

To build a playlist, open Playlists, press `N`, enter its name, then search for
songs and press `A` on each song you want to add. Choose the destination and
press Enter. Duplicate songs are not added again.

Song removal saves immediately and updates the active queue. Removing a song
does not remove its favorite or delete its audio file. An empty playlist remains
available for adding songs later. In Favorites, Delete/Backspace removes the
selected favorite.

## Lyrics and offline listening

- Press `L` on a song to open cached or online lyrics. Use arrows, Page Up/Page
  Down, and Home/End to scroll. `L`, Q, or Escape returns to the song list.
- Press `E` on the lyrics page to paste or edit lyrics. Enter `.save` on its own
  line to save, or `.cancel` on its own line to discard the edit.
- Press `D` on an online song to download audio. Open **Local Music → Browse
  library** to play supported files offline.
- **Local Music → Change music folder** changes the folder scanned for audio.

## Themes, backups, and terminal commands

Open **Spectrum** from Quick Links to preview Cat Outline, Cat Halo, Aurora Bars,
Aurora Wave (upward and downward), Orbit, or Silk Wave against the playing music. Use Up/Down to select and Enter to
save. **Off** hides the visualization. **Crossfade** cycles through Off, 1.5, 3,
and 5 seconds; **Trim silence** toggles trimming at track edges. Choices persist
between sessions and are included in settings backups. The default is Cat Outline
with a 3-second crossfade and silence trimming enabled.

Manual song changes start the next song at the selected volume while the
previous song fades out. Highlighted songs preload in the background separately
from the queued next track; no incoming fade is applied to manual changes. Automatic transitions keep the balanced crossfade.
On Windows 11 / builds 20348+, the live spectrum uses 48 FFT bands from Musik's
mixed audio output, with fast attack/release and headroom for individual beats.
Older Windows builds use the filter-bank fallback.

Home shows recent favorites above an untitled spectrum panel. Tab switches between
Quick Links and favorites; **Recently Played** opens from Quick Links. Queues keep
advancing while other screens are open. Internal pauses are preserved; trailing
silence detection scans the final 30 seconds of finite, seekable tracks. When a
scan fails, the original ending is used. Live streams transition when they end.

In **Themes**, use Up/Down to select Blue, Spotify, or SoundCloud and Enter to
apply and save the theme. **Backup & Restore** exports or imports saved data.
Import creates a safety backup and closes Musik after restoring settings.

Run these commands outside the app in PowerShell or Windows Terminal:

| Command | Action |
| --- | --- |
| `musik` | Launch Musik |
| `musik --version` | Print the application version |
| `musik update` | Update an installed copy to the latest GitHub release |
| `winget install --id CoderKinley.Musik --exact --source winget` | Install the WinGet package |

WinGet may list an older version than GitHub. After installation, `musik update`
checks GitHub for the latest release. Portable and source builds use a new
download or source build instead of the installed-copy updater.

For the complete layout, use a terminal at least 61 columns wide and 20 rows
high. Short terminals scroll the Home menu; wider terminals show more song text.
