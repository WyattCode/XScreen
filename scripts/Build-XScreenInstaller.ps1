param(
    [string]$Compiler = "$PSScriptRoot/../.runtime/installer-tools/nsis-complete/Bin/makensis.exe",
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.3.29',
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$RunName = ('installer-build-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff'))
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$release = Join-Path $projectRoot "artifacts/releases/v$Version"
$payload = Join-Path $release 'stage/XScreen'
$manifest = Get-Content -LiteralPath (Join-Path $release "XScreen-$Version-files.json") -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version) { throw 'Payload manifest version mismatch.' }
$setup = Join-Path $release "XScreen-$Version-Setup-x64.exe"
if (Test-Path -LiteralPath $setup) { throw 'Installer already exists; preserve prior delivery.' }
$generated = Join-Path $projectRoot "artifacts/$RunName"
if (Test-Path -LiteralPath $generated) { throw 'Installer build evidence already exists; preserve it.' }
$null = New-Item -ItemType Directory -Path $generated
$installLines = @(); $uninstallLines = @(); $directories = @()
foreach ($file in $manifest.files) {
    if ($file.path -notmatch '^XScreen/[A-Za-z0-9_. /()-]+$' -or $file.path.Contains('..')) { throw "Unexpected payload path: $($file.path)" }
    $relative = $file.path.Substring(8).Replace('/', '\')
    $source = Join-Path $payload $relative
    if ((Get-Item -LiteralPath $source).Length -ne $file.length -or (Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Payload differs from verified ZIP: $relative" }
    $directory = [IO.Path]::GetDirectoryName($relative)
    $installLines += 'SetOutPath "$INSTDIR' + $(if ($directory) { '\' + $directory }) + '"'
    $installLines += 'File "' + $source + '"'
    $uninstallLines += 'Delete "$INSTDIR\' + $relative + '"'
    while ($directory) { $directories += $directory; $directory = [IO.Path]::GetDirectoryName($directory) }
}
foreach ($directory in $directories | Select-Object -Unique | Sort-Object Length -Descending) { $uninstallLines += 'RMDir "$INSTDIR\' + $directory + '"' }
$installFile = Join-Path $generated 'install-files.nsh'
$uninstallFile = Join-Path $generated 'uninstall-files.nsh'
$installLines | Set-Content -LiteralPath $installFile -Encoding UTF8
$uninstallLines | Set-Content -LiteralPath $uninstallFile -Encoding UTF8
$arguments = @('/V3', "/DAPP_VERSION=$Version", "/DSETUP_FILE=$setup", "/DAPP_ICON=$(Join-Path $payload 'Assets/XScreen.ico')", "/DINSTALL_FILES=$installFile", "/DUNINSTALL_FILES=$uninstallFile", (Join-Path $projectRoot 'packaging/XScreen.nsi'))
& $Compiler @arguments 2>&1 | Tee-Object -FilePath (Join-Path $generated 'compiler.log')
if ($LASTEXITCODE -ne 0) { throw 'NSIS compilation failed.' }
if (!(Test-Path -LiteralPath $setup)) { throw 'Compiler did not produce an installer.' }
$result = [ordered]@{ version=$Version; installer=[IO.Path]::GetFileName($setup); payloadFiles=$manifest.files.Count; payloadMatchesVerifiedZip=$true;
    sha256=(Get-FileHash -LiteralPath $setup).Hash.ToLowerInvariant(); installerExecuted=$false; nativeMonitorReads=0; nativeMonitorWrites=0 }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $generated 'build-report.json') -Encoding UTF8
Write-Output "Built without executing installer: $setup"
