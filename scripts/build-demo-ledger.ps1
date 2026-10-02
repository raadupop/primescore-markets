#Requires -Version 5.1
<#
.SYNOPSIS
Copy the live ledger to apps/engine/var/demo/engine.db and record a state-gate replay on the copy.
.DESCRIPTION
The live ledger is append-only, so slice click-throughs (doc/slices/) run on this copy. The live
file is only read. Serve the copy with: .\scripts\start-markets.ps1 -Database apps/engine/var/demo/engine.db
.PARAMETER Force
Replace an existing demo copy.
#>
[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$live = Join-Path $repoRoot 'apps/engine/var/engine.db'
$demoDirectory = Join-Path $repoRoot 'apps/engine/var/demo'
$demo = Join-Path $demoDirectory 'engine.db'
$hostProject = Join-Path $repoRoot 'apps/engine/src/Host/PrimeScore.Engine.Host'

if (-not (Test-Path -LiteralPath $live)) { throw "No live ledger at $live." }
$wal = "$live-wal"
if ((Test-Path -LiteralPath $wal) -and (Get-Item -LiteralPath $wal).Length -gt 0) {
    throw 'The live ledger has unflushed writes (engine.db-wal is not empty). Stop the engine first.'
}
if ((Test-Path -LiteralPath $demo) -and -not $Force) { throw "$demo exists. Pass -Force to replace it." }

$null = New-Item -ItemType Directory -Path $demoDirectory -Force
Remove-Item -LiteralPath "$demo-wal", "$demo-shm" -ErrorAction SilentlyContinue
Copy-Item -LiteralPath $live -Destination $demo -Force
Write-Host "Copied $live to $demo"

$dotnet = (Get-Command dotnet -CommandType Application).Source
& $dotnet build $hostProject --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Engine build failed.' }

$newYork = [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time')
$to = [TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $newYork).Date.AddDays(-1).ToString('yyyy-MM-dd')
$label = "State gate, 2016 to $to (demo)"
Write-Host "Recording replay '$label' (about three minutes)..."
$replayId = & $dotnet run --project $hostProject --no-build --no-launch-profile -- replay --from 2016-01-01 --to $to --label $label --database $demo
if ($LASTEXITCODE -ne 0) { throw 'Replay failed; see the output above.' }
$replayId = ($replayId | Select-Object -Last 1).Trim()

Write-Host "Replay id: $replayId"
Write-Host 'Serve the copy:   .\scripts\start-markets.ps1 -Database apps/engine/var/demo/engine.db'
Write-Host 'With live feeds:  add -PullSources'
Write-Host "Outcomes record:  http://127.0.0.1:5080/outcomes?context=equity&replay=$replayId"
