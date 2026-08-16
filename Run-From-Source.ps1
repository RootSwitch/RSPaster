# Runs RSPaster from the .cs sources, compiling them in memory with Add-Type.
# Needs only Windows PowerShell 5.1 / .NET Framework 4.x, both inbox on Win 10/11.
#
# This is the no-binary path: same app as RSPaster.exe, but slower to start and
# heavier in memory because it compiles on every launch. Build-RSPaster.cmd
# produces the exe, which is what to use day to day.
#
# Normally launched by Run-From-Source.cmd, since Windows opens a .ps1 in an
# editor on double-click rather than running it.

$scriptPath = $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptPath

# WinForms needs an STA thread. powershell.exe defaults to STA, but relaunch
# defensively if this was started MTA (-MTA, or an unusual host).
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    Start-Process powershell.exe -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-STA',
        '-WindowStyle', 'Hidden', '-File', "`"$scriptPath`""
    )
    return
}

$sources = @('Themes.cs', 'KeySender.cs', 'Settings.cs', 'Controls.cs', 'RSPaster.cs') |
    ForEach-Object { Join-Path $root $_ }

Add-Type -Path $sources -ReferencedAssemblies 'System.Windows.Forms', 'System.Drawing'

# Teach "Restart as Admin" how to relaunch this script, rather than trying to
# relaunch powershell.exe with no arguments.
[RSPaster.Program]::RelaunchFile = (Get-Command powershell.exe).Source
[RSPaster.Program]::RelaunchArgs = "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File `"$scriptPath`""

[RSPaster.Program]::Run()
