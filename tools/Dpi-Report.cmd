@echo off
rem Reports RSPaster's real layout geometry at the display's actual scaling.
rem
rem A 100% display cannot simulate 150%: text metrics come from the real DPI,
rem so the layout has to be measured on the scaled display itself. Run this on
rem the machine and monitor in question and read the RESULT line.
setlocal

set "ROOT=%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: could not find the in-box csc.exe under %WINDIR%\Microsoft.NET
    exit /b 1
)

rem /main: picks the entry point, since RSPaster.cs carries its own Main.
"%CSC%" /nologo /target:exe /main:RSPaster.DpiReport /out:"%TEMP%\RSPasterDpiReport.exe" ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    "%ROOT%\Themes.cs" "%ROOT%\KeySender.cs" "%ROOT%\Settings.cs" ^
    "%ROOT%\Controls.cs" "%ROOT%\RSPaster.cs" "%~dp0DpiReport.cs"
if errorlevel 1 (
    echo Build of the report tool FAILED.
    exit /b 1
)

"%TEMP%\RSPasterDpiReport.exe"
del "%TEMP%\RSPasterDpiReport.exe" >nul 2>&1
