# Musik Release Guide

This guide explains how to publish a new Musik release through GitHub Actions
and Velopack. Run commands from the repository root in PowerShell.

## 1. Choose the version

Musik uses semantic versions in the form `MAJOR.MINOR.PATCH`.

- Patch release, such as `1.2.0` to `1.2.1`: bug fixes and small improvements
  that do not change expected behavior.
- Minor release, such as `1.2.0` to `1.3.0`: backward-compatible features or
  substantial improvements.
- Major release, such as `1.2.0` to `2.0.0`: breaking changes or a major product
  redesign.

Examples:

| Situation | Suggested version |
| --- | --- |
| Fix a playback or UI bug | `1.2.1` |
| Add a new page or feature | `1.3.0` |
| Make incompatible settings or CLI changes | `2.0.0` |

Every new version must be higher than every previously published version. Never
reuse an existing version or move an existing release tag.

The examples below use `1.3.0`. Replace it everywhere with the version being
published.

## 2. Synchronize the local `main` branch

Confirm that all intended feature branches or pull requests have already been
merged on GitHub. Then update the local repository:

```powershell
git switch main
git fetch origin --tags --prune
git pull --ff-only origin main
git status
```

`git status` should report that `main` is up to date and that there are no
uncommitted changes. Do not publish from a feature branch or a dirty worktree.

Review the commits that will be included since the previous release:

```powershell
git tag --list --sort=-version:refname
git log v1.2.0..HEAD --oneline
```

Replace `v1.2.0` with the actual previous release tag.

## 3. Update the project version

Open `KMusicPlayer\KMusicPlayer.csproj` and update all three version fields:

```xml
<Version>1.3.0</Version>
<AssemblyVersion>1.3.0.0</AssemblyVersion>
<FileVersion>1.3.0.0</FileVersion>
```

`Version` is the application/package version. `AssemblyVersion` and
`FileVersion` use four numeric parts and control the version shown by the built
Windows executable. The release script also passes these values during
publishing, but keeping the project file current makes source builds report the
correct version too.

## 4. Write the release notes

Create a Markdown file whose name exactly matches the future Git tag:

```text
releases\v1.3.0.md
```

Suggested structure:

```markdown
# Musik v1.3.0

One or two sentences describing the release.

## Highlights

- Added ...
- Improved ...

## Fixes

- Fixed ...
- Fixed ...

## Updating

Existing installed copies can update with:

    musik update

New users can download and run `Musik-Setup.exe` from this release.

## Windows notice

The installer is not currently code-signed. Windows SmartScreen may show an
unknown-publisher warning. SHA-256 checksums are included with the release.
```

The workflow reads this exact file when creating the GitHub Release. If it is
missing or named incorrectly, the publishing job will fail.

## 5. Build and verify

Restore dependencies and build the release configuration:

```powershell
dotnet restore .\KMusicPlayer.slnx
dotnet build .\KMusicPlayer.slnx -c Release --no-restore
```

Confirm the version from a source build:

```powershell
dotnet run --project .\KMusicPlayer -- --version
```

Expected output:

```text
Musik 1.3.0
```

Also inspect the pending release changes:

```powershell
git diff --check
git diff -- KMusicPlayer\KMusicPlayer.csproj releases\v1.3.0.md
git status --short
```

`git diff --check` should produce no output. Run any relevant manual tests before
publishing, especially playback, navigation, updates, and newly changed features.

## 6. Optional: build the installer locally

GitHub Actions performs the official release build, so this step is optional.
For a full local packaging test, install the pinned mpv package and run:

```powershell
choco install mpvio.install --version 0.41.0 -y
.\build\release.ps1 -Version 1.3.0
```

Artifacts are written to:

```text
artifacts\releases
```

The folder should contain the setup executable, portable ZIP, full Velopack
package, update-feed JSON files, and `SHA256SUMS.txt`. A local build does not
publish anything to GitHub.

## 7. Commit the release preparation

Stage only the intended files, inspect them, and commit:

```powershell
git add KMusicPlayer/KMusicPlayer.csproj releases/v1.3.0.md
git diff --cached
git commit -m "release: prepare Musik v1.3.0"
git push origin main
```

Wait for the `main` push to succeed before creating the tag. Verify the commit is
visible on GitHub if necessary.

## 8. Create and push the release tag

Create an annotated tag on the release commit:

```powershell
git tag -a v1.3.0 -m "Musik v1.3.0"
git show v1.3.0 --no-patch
git push origin v1.3.0
```

Pushing a tag matching `v*` triggers `.github\workflows\release.yml`. The
workflow:

1. Checks out the tagged commit.
2. Installs .NET 10 and the pinned mpv runtime.
3. Runs `build\release.ps1` using the tag version.
4. Builds the Velopack installer and update packages.
5. Creates a public GitHub Release and uploads all artifacts.

## 9. Monitor and verify the GitHub release

Open the repository's **Actions** page and select the Release workflow. Do not
announce the release until every step is green.

Then open the GitHub Releases page and verify that `v1.3.0` is published—not a
draft or prerelease—and contains at least:

- `Musik-Setup.exe`
- `CoderKiLe.Musik-1.3.0-full.nupkg`
- `CoderKiLe.Musik-win-Portable.zip`
- `releases.win.json`
- `assets.win.json`
- `RELEASES`
- `SHA256SUMS.txt`

Download `Musik-Setup.exe` and perform a clean-install smoke test when practical.

## 10. Test an update from an installed older version

On a machine with an older installer-based version, run:

```powershell
musik --version
musik update
```

The updater should find `1.3.0`, download it, apply it, and restart Musik. After
the restart, verify:

```powershell
musik --version
```

User data is stored under `%LOCALAPPDATA%\KMusicPlayer`, outside Velopack's
installation directory, so favorites, history, lyrics, playlists, and settings
should survive an application update.

Portable or source builds cannot use `musik update`; they must install
`Musik-Setup.exe` first.

## Patch-release example

For a small fix after `1.3.0`, use `1.3.1`:

```powershell
git switch main
git pull --ff-only origin main

# Update the project to 1.3.1 and create releases\v1.3.1.md first.

dotnet build .\KMusicPlayer.slnx -c Release
git add KMusicPlayer/KMusicPlayer.csproj releases/v1.3.1.md
git commit -m "release: prepare Musik v1.3.1"
git push origin main
git tag -a v1.3.1 -m "Musik v1.3.1"
git push origin v1.3.1
```

## If the workflow fails

1. Open the failed workflow run and read the first failing step.
2. Fix the problem on `main` and verify it locally.
3. Delete the failed remote tag only if no usable public release was published:

```powershell
git tag -d v1.3.0
git push origin :refs/tags/v1.3.0
```

4. Commit and push the fix.
5. Recreate the same tag only when the failed release was never successfully
   published and no users could have installed it.

If a release was successfully published or downloaded by users, do not replace
its tag or artifacts. Publish a higher patch version instead—for example,
`1.3.1`—so Velopack and installed clients see an unambiguous upgrade path.

## Release checklist

- [ ] Features and fixes are merged into `main`.
- [ ] The worktree is clean before release preparation.
- [ ] `Version`, `AssemblyVersion`, and `FileVersion` are updated.
- [ ] `releases\vX.Y.Z.md` exists and matches the intended tag.
- [ ] Release build succeeds with no errors.
- [ ] Important features have been manually smoke-tested.
- [ ] Release preparation is committed and pushed to `main`.
- [ ] Annotated `vX.Y.Z` tag is pushed.
- [ ] GitHub Actions completes successfully.
- [ ] Installer, packages, feeds, and checksums are present.
- [ ] Updating from an older installed version succeeds.
