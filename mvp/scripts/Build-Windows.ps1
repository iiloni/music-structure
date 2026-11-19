[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param([string]$Program, [string[]]$Arguments)
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program exited with code $LASTEXITCODE." }
}

$root = Split-Path -Parent $PSScriptRoot
$viewer = Join-Path $root 'viewer'
$out = Join-Path $root 'out/win-x64'

Push-Location $viewer
try {
    Invoke-Checked npm @('ci')
    Invoke-Checked npm @('run', 'check')
    Invoke-Checked npm @('run', 'build')
} finally { Pop-Location }

Invoke-Checked dotnet @('publish', (Join-Path $root 'helper/MusicStructure.csproj'), '-c', 'Release',
    '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-o', $out)

Write-Host "Built $(Join-Path $out 'MusicStructure.exe')"
