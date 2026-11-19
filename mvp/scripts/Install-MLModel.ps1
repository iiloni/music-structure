[CmdletBinding()]
param([string]$Destination)
$ErrorActionPreference = 'Stop'
if (-not $Destination) {
    # $PSScriptRoot can be empty in param defaults on Windows PowerShell 5.1.
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $Destination = Join-Path $scriptDir '..\out\win-x64'
}
$name = 'msd-musicnn-1.onnx'
$hash = '49668ffec47e52e94b96f45930bb46a28a1368d4bdfb5c05378fa834aca616e1'
[void](New-Item -ItemType Directory -Force -Path $Destination)
$path = Join-Path $Destination $name
$temp = "$path.$PID.download"
try {
    Invoke-WebRequest -UseBasicParsing -Uri "https://essentia.upf.edu/models/autotagging/msd/$name" -OutFile $temp
    if ((Get-FileHash -Path $temp -Algorithm SHA256).Hash -ne $hash) { throw 'Downloaded model hash did not match.' }
    Move-Item -LiteralPath $temp -Destination $path -Force
    Write-Host "Installed $path"
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
}
