@echo off
rem Builds RSPaster.exe with the C# compiler that ships inside Windows
rem (.NET Framework 4.x). No SDK, no download, no NuGet.
setlocal

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: could not find the in-box csc.exe under %WINDIR%\Microsoft.NET
    exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /out:"%~dp0RSPaster.exe" ^
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
    "%~dp0Themes.cs" "%~dp0KeySender.cs" "%~dp0Settings.cs" ^
    "%~dp0Controls.cs" "%~dp0RSPaster.cs"
if errorlevel 1 (
    echo Build FAILED.
    exit /b 1
)
echo Built: %~dp0RSPaster.exe
