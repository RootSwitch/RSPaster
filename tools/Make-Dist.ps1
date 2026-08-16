# Builds dist\RSPaster.zip: the files someone needs to run or rebuild RSPaster,
# and nothing else.
#
# The exe is rebuilt first rather than zipped as found, because a distribution
# carrying a binary that does not match the sources beside it is worse than one
# carrying no binary at all.
#
# Left out deliberately: the repo plumbing (.git, .gitignore, .gitattributes),
# CHANGELOG.md, and tools\ itself. None of it is needed to run or rebuild, and
# a dev script in a distribution just raises questions.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\Make-Dist.ps1

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist '_stage'
$payload = Join-Path $stage 'RSPaster'
$zip = Join-Path $dist 'RSPaster.zip'

# Everything the zip ships, relative to the repo root. docs\screenshot.png is
# here only because README.md renders it; drop one and drop both.
$manifest = @(
    'RSPaster.exe'
    'RSPaster.cs'
    'KeySender.cs'
    'Themes.cs'
    'Controls.cs'
    'Settings.cs'
    'RSPaster.ps1'
    'RSPaster.cmd'
    'Build-RSPaster.cmd'
    'README.md'
    'LICENSE'
    'docs\screenshot.png'
)

Write-Host 'Rebuilding the exe so it matches the sources shipped beside it...'
& (Join-Path $root 'Build-RSPaster.cmd')
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Not packaging a stale binary.' }

# A missing file must stop the run. A zip that is quietly short of a source
# file still unzips and still looks fine.
$missing = @()
foreach ($rel in $manifest) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $rel))) { $missing += $rel }
}
if ($missing.Count -gt 0) {
    throw ("Missing from the working tree: {0}" -f ($missing -join ', '))
}

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $payload -Force | Out-Null

foreach ($rel in $manifest) {
    $dest = Join-Path $payload $rel
    $destDir = Split-Path -Parent $dest
    if (-not (Test-Path -LiteralPath $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $root $rel) -Destination $dest
}

if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $payload -DestinationPath $zip -CompressionLevel Optimal
Remove-Item -LiteralPath $stage -Recurse -Force

Write-Host ''
Write-Host 'Packaged:'
foreach ($rel in $manifest) {
    $size = (Get-Item -LiteralPath (Join-Path $root $rel)).Length
    Write-Host ("  {0,-22} {1,8:N0} bytes" -f $rel, $size)
}
$zipSize = (Get-Item -LiteralPath $zip).Length
Write-Host ''
Write-Host ("{0}  ({1:N0} bytes, {2} files)" -f $zip, $zipSize, $manifest.Count) -ForegroundColor Green
