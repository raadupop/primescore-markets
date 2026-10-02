#Requires -Version 5.1
<#
.SYNOPSIS
Start the local engine, classifier and Markets presentation website; Ctrl+C stops all.
.PARAMETER CheckOnly
Validate prerequisites and ports without building or starting services.
.PARAMETER Database
Serve another engine database, for example the demo copy from scripts/build-demo-ledger.ps1.
.PARAMETER PullSources
With -Database only: switch on the Cboe and calendar sources for this run (docs/ENGINE.md).
#>
[CmdletBinding()]
param([switch]$CheckOnly, [string]$Database, [switch]$PullSources)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$pythonPath = Join-Path $repoRoot 'apps/classification/.venv/Scripts/python.exe'
$hostProject = Join-Path $repoRoot 'apps/engine/src/Host/PrimeScore.Engine.Host'
$classifier = $null
$websites = $null
$previousBootstrap = $env:BOOTSTRAP_MODE
$previousRegistry = $env:PRIMESCORE_REGISTRY_PATH
$sourceSections = @('Cboe', 'FedCalendar', 'BlsCalendar', 'BeaCalendar', 'EiaCalendar', 'ClaimsCalendar', 'OpecCalendar')
$engineVariables = @{}
$launchProfile = (Get-Content -Raw -LiteralPath (Join-Path $hostProject 'Properties/launchSettings.json') | ConvertFrom-Json).profiles.engine
$engineVariables['ASPNETCORE_URLS'] = $launchProfile.applicationUrl
foreach ($variable in $launchProfile.environmentVariables.PSObject.Properties) { $engineVariables[$variable.Name] = $variable.Value }
if ($PullSources -and -not $Database) {
    throw '-PullSources needs -Database: sources stay off on the live ledger until TODO-016 is settled.'
}
if ($Database) {
    $databasePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Database)
    if (-not (Test-Path -LiteralPath $databasePath -PathType Leaf)) { throw "No engine database at $databasePath." }
    $engineVariables['Engine__DatabasePath'] = $databasePath
}
if ($PullSources) {
    foreach ($section in $sourceSections) {
        $engineVariables["Sources__${section}__Enabled"] = 'true'
        $engineVariables["Sources__${section}__RunOnStartup"] = 'true'
    }
}
$previousEngine = @{}
foreach ($name in $engineVariables.Keys) { $previousEngine[$name] = [Environment]::GetEnvironmentVariable($name) }

function Assert-FreePort([int]$Port) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    $listener.Server.ExclusiveAddressUse = $true
    try { $listener.Start() }
    catch { throw "Port $Port is already in use. Stop the existing service before launching Markets." }
    finally { $listener.Stop() }
}

function Wait-ServicePort([Diagnostics.Process]$Process, [int]$Port, [string]$LogPath) {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) { throw "Service for port $Port exited. See $LogPath" }
        $connection = [Net.Sockets.TcpClient]::new()
        try {
            $connection.Connect('127.0.0.1', $Port)
            return
        } catch { Start-Sleep -Milliseconds 250 }
        finally { $connection.Dispose() }
    }
    throw "Service for port $Port timed out. See $LogPath"
}

Push-Location $repoRoot
try {
    if (-not (Test-Path -LiteralPath $pythonPath)) {
        throw 'Python virtual environment missing. Follow the install steps in docs/ENGINE.md.'
    }
    $dotnetPath = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    $sdks = & $dotnetPath --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($sdks -match '^10\.')) {
        throw '.NET 10 SDK is required.'
    }
    $env:BOOTSTRAP_MODE = 'disabled'
    $env:PRIMESCORE_REGISTRY_PATH = (Resolve-Path 'infra/registry.yaml').Path
    & $pythonPath -c "import sys; sys.path.insert(0, 'apps/classification'); import uvicorn; import main"
    if ($LASTEXITCODE -ne 0) {
        throw 'Classifier import failed. Install apps/classification/requirements-dev.txt into its virtual environment.'
    }
    Assert-FreePort 8000
    Assert-FreePort 5080
    Assert-FreePort 8091
    if ($CheckOnly) {
        Write-Host 'Prerequisites verified; ports 5080, 8000 and 8091 are free.'
        return
    }

    & $dotnetPath build $hostProject --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Engine build failed; no services were started.' }

    $logDirectory = Join-Path $repoRoot 'apps/engine/var/local-run'
    $null = New-Item -ItemType Directory -Path $logDirectory -Force
    # The engine runs from a copy, so a running dashboard does not lock the files the next build replaces.
    $engineDirectory = Join-Path $logDirectory 'engine'
    if (Test-Path -LiteralPath $engineDirectory) { Remove-Item -LiteralPath $engineDirectory -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $hostProject 'bin/Debug/net10.0') -Destination $engineDirectory -Recurse
    $websiteErrorPath = Join-Path $logDirectory 'websites.stderr.log'
    $websites = Start-Process -FilePath $pythonPath -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru `
        -ArgumentList @('scripts/websites.py') `
        -RedirectStandardOutput (Join-Path $logDirectory 'websites.stdout.log') -RedirectStandardError $websiteErrorPath
    Wait-ServicePort $websites 8091 $websiteErrorPath
    $stdoutPath = Join-Path $logDirectory 'classifier.stdout.log'
    $stderrPath = Join-Path $logDirectory 'classifier.stderr.log'
    $classifier = Start-Process -FilePath $pythonPath -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru `
        -ArgumentList @('-m', 'uvicorn', 'main:app', '--app-dir', 'apps/classification', '--host', '127.0.0.1', '--port', '8000') `
        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

    Wait-ServicePort $classifier 8000 $stderrPath

    Write-Host 'Dashboard: http://127.0.0.1:5080/ (once the engine starts). Ctrl+C stops all services.'
    Write-Host 'Classifier: http://127.0.0.1:8000'
    Write-Host 'PrimeScore Markets: http://127.0.0.1:8091'
    Write-Host "Service logs: $logDirectory"
    if ($Database) { Write-Host "Database: $databasePath" }
    foreach ($name in $engineVariables.Keys) { [Environment]::SetEnvironmentVariable($name, $engineVariables[$name]) }
    # From the project folder, as dotnet run starts it: the content root and relative paths stay the same.
    Push-Location $hostProject
    try { & $dotnetPath (Join-Path $engineDirectory 'PrimeScore.Engine.Host.dll') }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "Engine exited with code $LASTEXITCODE." }
} finally {
    foreach ($service in @($classifier, $websites)) {
        if ($null -ne $service -and -not $service.HasExited) {
            $service.Kill()
            $service.WaitForExit()
        }
    }
    $env:BOOTSTRAP_MODE = $previousBootstrap
    $env:PRIMESCORE_REGISTRY_PATH = $previousRegistry
    foreach ($name in $previousEngine.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEngine[$name]) }
    Pop-Location
}
