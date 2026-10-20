param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.3.30',
    [ValidatePattern('^[A-Za-z0-9_-]+$')][string]$RunName = ('offline-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$hostDirectory = Join-Path $projectRoot "artifacts/XScreen-regression-$Version"
$hostExe = Join-Path $hostDirectory 'XScreen.Review.exe'
if (!(Test-Path -LiteralPath $hostExe -PathType Leaf)) { throw 'Publish the offline ReviewChecks host first.' }
foreach ($assembly in @('XScreen.dll', 'XScreen.Core.dll')) {
    $publishedDll = Join-Path $projectRoot "artifacts/XScreen-$Version-win-x64/$assembly"
    if ((Get-FileHash -LiteralPath $publishedDll).Hash -ne (Get-FileHash -LiteralPath (Join-Path $hostDirectory $assembly)).Hash) {
        throw "The offline host must test the exact published assembly: $assembly"
    }
}
$outputDirectory = Join-Path $projectRoot "analysis/evidence/$RunName"
if (Test-Path -LiteralPath $outputDirectory) { throw 'Choose a fresh run name; PNG evidence must never be overwritten.' }
$null = New-Item -ItemType Directory -Path $outputDirectory
$scenarios = [ordered]@{
    'hotkey-audit' = '--hotkey-audit'
    hotkeys = '--hotkeys'
    'ui-alignment' = '--ui-alignment'
    'window-state' = '--window-state'
    'reset-settings' = '--reset-settings'
    'exit-reset' = '--exit-reset'
    'exit-reset-review' = '--exit-reset-review'
    'osd-synchronization' = '--osd-synchronization'
    'osd-fault-name' = '--osd-fault-name'
    'osd-controls' = '--osd-controls'
    'exit-osd-apply' = '--exit-osd-apply'
    'osd-comparison' = '--osd-comparison'
    'tray-steps' = '--tray-steps'
    'appearance-recovery' = '--appearance-recovery'
    lifecycle = '--tray-lifecycle'
    audit = '--tray-audit'
    menu = '--tray-menu'
    core = ''
    ui = '--ui-details'
    'close-startup' = '--close-during-startup'
    'close-write' = '--close-during-write'
    'exit-editor' = '--exit-with-editor'
    'exit-startup' = '--exit-during-startup'
}
$summary = @()
# Native UI tests share keyboard focus, so run them sequentially in their fake-only host.
foreach ($scenario in $scenarios.GetEnumerator()) {
    $report = Join-Path $outputDirectory ($scenario.Key + '.json')
    $arguments = @('"' + $report + '"')
    if ($scenario.Value.Length -gt 0) { $arguments += $scenario.Value }
    $testProcess = Start-Process -FilePath $hostExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$testProcess.WaitForExit(25000)) { throw "Offline scenario timed out: $($scenario.Key), process $($testProcess.Id)." }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    $entry = [pscustomobject]@{ scenario = $scenario.Key; exitCode = $testProcess.ExitCode; checks = @($result.results).Count; failures = $result.failures; nativeReads = $result.nativeReadAttempts; nativeWrites = $result.nativeWriteAttempts; report = $report }
    $summary += $entry
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDirectory 'summary.json') -Encoding UTF8
    Write-Output "$($entry.scenario): $($entry.checks) checks, $($entry.failures) failures, native $($entry.nativeReads)/$($entry.nativeWrites), exit $($entry.exitCode)"
    if ($testProcess.ExitCode -ne 0 -or !$result.offline -or $result.failures -ne 0 -or $result.nativeReadAttempts -ne 0 -or $result.nativeWriteAttempts -ne 0) {
        throw "Offline scenario failed: $($scenario.Key); inspect $report."
    }
}
Write-Output "Offline UI evidence: $outputDirectory"
