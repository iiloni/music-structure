[CmdletBinding()]
param(
    [ValidateRange(2, 15)]
    [int]$Seconds = 5
)
$ErrorActionPreference = 'Stop'
# Generate a PCM tone in this process, independently of Spotify and the helper.
$rate = 44100
$count = $rate * $Seconds
$bytes = 2 * $count
$stream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($stream)
$ascii = [System.Text.Encoding]::ASCII
$player = $null

try {
    $writer.Write($ascii.GetBytes('RIFF'))
    $writer.Write([int](36 + $bytes))
    $writer.Write($ascii.GetBytes('WAVEfmt '))
    $writer.Write([int]16)
    $writer.Write([short]1)
    $writer.Write([short]1)
    $writer.Write([int]$rate)
    $writer.Write([int](2 * $rate))
    $writer.Write([short]2)
    $writer.Write([short]16)
    $writer.Write($ascii.GetBytes('data'))
    $writer.Write([int]$bytes)
    for ($i = 0; $i -lt $count; $i++) {
        $pulse = if (($i % $rate) -lt ($rate * 0.7)) { 1 } else { 0 }
        $sample = 10000 * $pulse * [Math]::Sin(2 * [Math]::PI * 880 * $i / $rate)
        $writer.Write([short]$sample)
    }
    $writer.Flush()
    $stream.Position = 0
    $player = [System.Media.SoundPlayer]::new($stream)
    $player.Load()
    Write-Host "Playing $Seconds seconds of 880 Hz pulses from PowerShell PID $PID at $(Get-Date -Format o)"
    $player.PlaySync()
} finally {
    if ($null -ne $player) { $player.Dispose() }
    $writer.Dispose()
    $stream.Dispose()
}
