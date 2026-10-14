@echo off
setlocal
rem Builds DualPulse.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem No SDK, Visual Studio or NuGet needed.

cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.x.
  exit /b 1
)

if not exist bin mkdir bin
set SOURCES=src\Hid.cs src\Engine.cs src\Program.cs src\Devices\Device.cs src\Devices\LampArray.cs src\Devices\Logitech.cs src\Devices\Corsair.cs src\UI\Controls.cs src\UI\Preview.cs src\UI\MainForm.cs
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ %REFS% /win32icon:assets\DualPulse.ico /out:bin\DualPulse.exe %SOURCES%
if errorlevel 1 exit /b 1
echo.
echo Built bin\DualPulse.exe
