param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.3.30',
    [string]$EvidenceDirectory = "$PSScriptRoot/../analysis/evidence/local-review"
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$published = Join-Path $projectRoot "artifacts/XScreen-$Version-win-x64"
$hostDirectory = Join-Path $projectRoot "artifacts/XScreen-regression-$Version"
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$summary = Get-Content -LiteralPath (Join-Path $evidence 'summary.json') -Raw | ConvertFrom-Json
if ($summary.Count -lt 23 -or @($summary | Where-Object { $_.exitCode -ne 0 -or $_.failures -ne 0 -or $_.nativeReads -ne 0 -or $_.nativeWrites -ne 0 }).Count -gt 0) { throw 'Complete passing fake-only UI evidence is required before packaging.' }
foreach ($assembly in @('XScreen.dll', 'XScreen.Core.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $published $assembly)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $hostDirectory $assembly)).Hash) { throw "Tested and published assembly mismatch: $assembly" }
}
if (([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $published 'XScreen.dll'))).FileVersion -ne "$Version.0") { throw 'Assembly version does not match the requested release.' }
$releaseRoot = Join-Path $projectRoot "artifacts/releases/v$Version"
if (Test-Path -LiteralPath $releaseRoot) { throw 'Release output already exists. Do not overwrite a package or its evidence.' }
$stage = Join-Path $releaseRoot 'stage'
$appStage = Join-Path $stage 'XScreen'
$null = New-Item -ItemType Directory -Path $appStage
foreach ($file in Get-ChildItem -LiteralPath $published -Recurse -File) {
    $relative = $file.FullName.Substring($published.Length + 1)
    if ($relative -match '(^|[\\/])(runtime-data|sessions|bin|obj)([\\/]|$)' -or $relative -match '\.(pdb|log|jsonl)$') { continue }
    $target = Join-Path $appStage $relative
    $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $appStage 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot "docs/RELEASE-$Version.md") -Destination (Join-Path $appStage 'RELEASE-NOTES.md')
$files = @(Get-ChildItem -LiteralPath $appStage -Recurse -File | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ path = 'XScreen/' + $_.FullName.Substring($appStage.Length + 1).Replace('\', '/'); length = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$manifest = Join-Path $releaseRoot "XScreen-$Version-files.json"
[pscustomobject]@{ version = $Version; platform = 'win-x64'; files = $files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $releaseRoot "XScreen-$Version-win-x64.zip"
$writer = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        # .NET Framework CreateFromDirectory emits backslash entry names on Windows.
        # Explicit portable entry names keep the archive compatible with other unzip tools.
        $entry = $writer.CreateEntry($file.path, [IO.Compression.CompressionLevel]::Optimal)
        $inputStream = [IO.File]::OpenRead((Join-Path $stage $file.path))
        $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) }
        finally { $inputStream.Dispose(); $outputStream.Dispose() }
    }
}
finally { $writer.Dispose() }
# Verify every ZIP member against staging without launching any executable.
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $members = @($archive.Entries | Where-Object { !$_.FullName.EndsWith('/') })
    if ($members.Count -ne $files.Count) { throw 'ZIP member count mismatch.' }
    foreach ($entry in $members) {
        $expected = $files | Where-Object { $_.path -ceq $entry.FullName }
        if (@($expected).Count -ne 1 -or $entry.Length -ne $expected.length) { throw "Unexpected ZIP member: $($entry.FullName)" }
        $stream = $entry.Open(); $hash = [Security.Cryptography.SHA256]::Create()
        try { $actual = ([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $hash.Dispose() }
        if ($actual -ne $expected.sha256) { throw "ZIP hash mismatch: $($entry.FullName)" }
    }
}
finally { $archive.Dispose() }
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ASCII
Write-Output "Verified $($files.Count) files: $zip"
Write-Output "SHA256: $zipHash"
