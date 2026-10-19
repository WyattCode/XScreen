param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.3.29')
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$FilePath) {
    $stream = [IO.File]::OpenRead($FilePath); $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $hash.Dispose() }
}
$projectRoot = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$oldRelease = Join-Path $projectRoot "artifacts/releases/v$Version"
$source = Join-Path $oldRelease 'stage/XScreen'
$manifest = Get-Content -LiteralPath (Join-Path $oldRelease "XScreen-$Version-files.json") -Raw | ConvertFrom-Json
$metadata = Get-Content -LiteralPath (Join-Path $projectRoot 'package.json') -Raw | ConvertFrom-Json
if ($metadata.version -ne $Version -or $manifest.version -ne $Version) { throw 'Version mismatch.' }
$output = Join-Path $projectRoot 'release'
$prepackaged = Join-Path $output 'win-unpacked'
$setup = Join-Path $output "XScreen Setup $Version.exe"
if ((Test-Path -LiteralPath $prepackaged) -or (Test-Path -LiteralPath $setup)) { throw 'Release output already exists; archive it before a new build.' }
$null = New-Item -ItemType Directory -Path $prepackaged
$remove = @(); $directories = @()
foreach ($file in $manifest.files) {
    if ($file.path -notmatch '^XScreen/[A-Za-z0-9_. /()-]+$' -or $file.path.Contains('..')) { throw 'Unexpected payload path.' }
    $relative = $file.path.Substring(8).Replace('/', '\')
    $original = Join-Path $source $relative
    if ((Get-Item -LiteralPath $original).Length -ne $file.length -or (Get-Sha256 $original) -ne $file.sha256) { throw "Verified payload mismatch: $relative" }
    $destination = Join-Path $prepackaged $relative
    $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force
    Copy-Item -LiteralPath $original -Destination $destination
    $remove += 'Delete "$INSTDIR\' + $relative + '"'
    $directory = [IO.Path]::GetDirectoryName($relative)
    while ($directory) { $directories += $directory; $directory = [IO.Path]::GetDirectoryName($directory) }
}
foreach ($directory in $directories | Select-Object -Unique | Sort-Object Length -Descending) { $remove += 'RMDir "$INSTDIR\' + $directory + '"' }
$remove += 'Delete "$INSTDIR\Uninstall XScreen.exe"'
$remove += 'Delete "$DESKTOP\XScreen.lnk"'
$remove += 'SetOutPath "$TEMP"'
$remove += 'RMDir "$INSTDIR"'
$remove | Set-Content -LiteralPath (Join-Path $projectRoot 'packaging/generated-uninstall.nsh') -Encoding UTF8
# --prepackaged skips Electron application creation and preserves native DLL/EXE bytes.
$cli = Join-Path $projectRoot 'node_modules/electron-builder/cli.js'
& node $cli --projectDir $projectRoot --config (Join-Path $projectRoot 'electron-builder.yml') --prepackaged $prepackaged --win nsis --x64 --publish never
if ($LASTEXITCODE -ne 0) { throw 'electron-builder failed.' }
foreach ($file in $manifest.files) {
    $relative = $file.path.Substring(8)
    if ((Get-Sha256 (Join-Path $prepackaged $relative)) -ne $file.sha256) { throw 'Builder modified the native payload.' }
}
if ((Get-ChildItem -LiteralPath $prepackaged -Recurse -File).Count -ne $manifest.files.Count) { throw 'Builder added unexpected application files.' }
$report = [ordered]@{version=$Version;packager='electron-builder';packagerVersion='26.15.3';mode='prepackaged native WinUI';payloadFiles=$manifest.files.Count;payloadUnchanged=$true;setup=[IO.Path]::GetFileName($setup);sha256=(Get-Sha256 $setup);finalInstallerExecuted=$false;applicationExecuted=$false}
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'verification.json') -Encoding UTF8
Write-Output "Built native WinUI installer using electron-builder: $setup"
