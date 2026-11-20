[CmdletBinding()]
param(
    [switch]$Demo,
    [switch]$NoOpen
)

$ErrorActionPreference = 'Stop'
$mvp = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent $mvp

$branch = & git -C $repo branch --show-current
if ($LASTEXITCODE -ne 0) { throw 'Could not read the current Git branch.' }
if (-not $branch) { throw 'Check out the MVP branch before running the updater.' }

$changes = & git -C $repo status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0) { throw 'Could not check the working tree.' }
if ($changes) { throw 'The working tree has local changes. Commit or stash them before updating.' }

$upstream = & git -C $repo for-each-ref '--format=%(upstream)' "refs/heads/$branch"
if ($LASTEXITCODE -ne 0) { throw "Could not check the upstream for $branch." }
if ($upstream) {
    & git -C $repo pull --ff-only --no-rebase
} else {
    & git -C $repo pull --ff-only --no-rebase -- origin $branch
}
if ($LASTEXITCODE -ne 0) { throw "Could not fast-forward $branch. Check the remote branch and Git output above." }

& (Join-Path $PSScriptRoot 'Build-Windows.ps1')
if ($LASTEXITCODE -ne 0) { throw 'The build failed. Check the build output above.' }

$helperArgs = @()
if ($Demo) { $helperArgs += '--demo' }
if ($NoOpen) { $helperArgs += '--no-open' }
& (Join-Path $mvp 'out/win-x64/MusicStructure.exe') @helperArgs
exit $LASTEXITCODE
