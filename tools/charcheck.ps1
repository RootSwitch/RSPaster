# Fails if any tracked text file contains an em-dash or en-dash.
#
# House style is " - ". These characters arrive invisibly through pasted text
# and editor autocorrect, and a mixed file is not something review catches.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\charcheck.ps1

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$banned = @{
    [char]0x2014 = 'em-dash (U+2014)'
    [char]0x2013 = 'en-dash (U+2013)'
    [char]0x2015 = 'horizontal bar (U+2015)'
}

$extensions = '*.cs', '*.ps1', '*.cmd', '*.md', '*.txt', '*.json', '*.yml'
$skipDirs = '\\\.git\\', '\\bin\\', '\\obj\\'

$failures = 0
foreach ($ext in $extensions) {
    foreach ($file in Get-ChildItem -Path $root -Filter $ext -Recurse -File) {
        $skip = $false
        foreach ($d in $skipDirs) { if ($file.FullName -match $d) { $skip = $true } }
        if ($skip) { continue }

        $lineNo = 0
        foreach ($line in (Get-Content -LiteralPath $file.FullName -Encoding UTF8)) {
            $lineNo++
            foreach ($ch in $banned.Keys) {
                if ($line.IndexOf($ch) -ge 0) {
                    $rel = $file.FullName.Substring($root.Length + 1)
                    Write-Host ("{0}:{1}: {2}" -f $rel, $lineNo, $banned[$ch]) -ForegroundColor Red
                    Write-Host ("    {0}" -f $line.Trim())
                    $failures++
                }
            }
        }
    }
}

if ($failures -gt 0) {
    Write-Host ""
    Write-Host ("charcheck FAILED: {0} occurrence(s). Use ' - ' instead." -f $failures) -ForegroundColor Red
    exit 1
}
Write-Host "charcheck passed." -ForegroundColor Green
exit 0
