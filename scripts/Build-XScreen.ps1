param([string]$OutputDirectory = "$PSScriptRoot/../artifacts/XScreen-0.3.31-win-x64")
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath("$PSScriptRoot/..")
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (!$output.StartsWith((Join-Path $projectRoot 'artifacts') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish output must remain inside workspace artifacts.' }
$running = @(Get-Process XScreen -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $output 'XScreen.exe') })
if ($running.Count -gt 0) { throw 'Exit the XScreen instance running from this output folder before rebuilding.' }
dotnet publish "$projectRoot/src/XScreen.App/XScreen.App.csproj" -c Release -o $output --ignore-failed-sources -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'XScreen publish failed.' }
Write-Output "Built: $output/XScreen.exe"
