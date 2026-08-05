param(
    [string]$Version = "1.1.0",
    [string]$Runtime = "win-x64",
    [string]$MpvSourceDirectory = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot "KMusicPlayer\KMusicPlayer.csproj"
$artifacts = Join-Path $repositoryRoot "artifacts"
$publishDirectory = Join-Path $artifacts "publish"
$releaseDirectory = Join-Path $artifacts "releases"
$numericVersion = ($Version -split '-')[0]
$assemblyVersion = if (($numericVersion -split '\.').Count -eq 3) {
    "$numericVersion.0"
} else {
    $numericVersion
}

Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $releaseDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $publishDirectory, $releaseDirectory -Force | Out-Null

dotnet tool restore
dotnet restore $project
dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:Version=$Version `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -o $publishDirectory

if ([string]::IsNullOrWhiteSpace($MpvSourceDirectory)) {
    $chocolateyMpv = Join-Path $env:ChocolateyInstall "lib\mpvio.install\tools"
    if (-not (Test-Path -LiteralPath (Join-Path $chocolateyMpv "mpv.exe"))) {
        throw "mpv 0.41.0 is required. Run 'choco install mpvio.install --version 0.41.0 -y' or pass -MpvSourceDirectory."
    }
    $MpvSourceDirectory = $chocolateyMpv
}

$toolsDirectory = Join-Path $publishDirectory "tools"
$licensesDirectory = Join-Path $publishDirectory "licenses\mpv"
New-Item -ItemType Directory -Path $toolsDirectory, $licensesDirectory -Force | Out-Null

$mpvExecutable = Get-ChildItem -Path $MpvSourceDirectory -Filter "mpv.exe" -Recurse | Select-Object -First 1
if ($null -eq $mpvExecutable) {
    throw "mpv.exe was not found below $MpvSourceDirectory."
}
Copy-Item -LiteralPath $mpvExecutable.FullName -Destination (Join-Path $toolsDirectory "mpv.exe")

$compilerDll = Get-ChildItem -Path $MpvSourceDirectory -Filter "d3dcompiler_43.dll" -Recurse | Select-Object -First 1
if ($null -ne $compilerDll) {
    Copy-Item -LiteralPath $compilerDll.FullName -Destination $toolsDirectory
}

$mpvLicense = Get-ChildItem -Path $MpvSourceDirectory -Filter "LICENSE.txt" -Recurse | Select-Object -First 1
if ($null -eq $mpvLicense) {
    throw "The mpv LICENSE.txt file was not found."
}
Copy-Item -LiteralPath $mpvLicense.FullName -Destination $licensesDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "README.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.md") -Destination $publishDirectory

dotnet tool run vpk pack `
    --packId "CoderKiLe.Musik" `
    --packVersion $Version `
    --packDir $publishDirectory `
    --mainExe "musik.exe" `
    --packTitle "Musik" `
    --packAuthors "CoderKiLe" `
    --releaseNotes (Join-Path $repositoryRoot "releases\v$Version.md") `
    --shortcuts "StartMenuRoot" `
    --outputDir $releaseDirectory

$setup = Get-ChildItem -Path $releaseDirectory -Filter "*-Setup.exe" | Select-Object -First 1
if ($null -ne $setup) {
    Copy-Item -LiteralPath $setup.FullName -Destination (Join-Path $releaseDirectory "Musik-Setup.exe")
}

Get-ChildItem -Path $releaseDirectory -File |
    Get-FileHash -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } |
    Set-Content -Path (Join-Path $releaseDirectory "SHA256SUMS.txt")

Write-Host "Release artifacts created in $releaseDirectory"
