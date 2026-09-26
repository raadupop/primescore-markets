#Requires -Version 5.1
# Preview the Markets presentation website. Ctrl+C stops it.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$pythonPath = Join-Path $repoRoot 'apps/classification/.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $pythonPath)) {
    $pythonPath = (Get-Command python -CommandType Application -ErrorAction Stop).Source
}
& $pythonPath (Join-Path $PSScriptRoot 'websites.py')
if ($LASTEXITCODE -ne 0) { throw "Website preview exited with code $LASTEXITCODE." }
