@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set SRC=%~dp0src

"%CSC%" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ ^
  /out:"%~dp0Battery24GTray.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  "%SRC%\Program.cs" ^
  "%SRC%\BatteryModel.cs" ^
  "%SRC%\HidNative.cs" ^
  "%SRC%\Protocols.cs" ^
  "%SRC%\Hid24GProvider.cs" ^
  "%SRC%\BatteryIconRenderer.cs" ^
  "%SRC%\AutoStartManager.cs" ^
  "%SRC%\TrayApplicationContext.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK: %~dp0Battery24GTray.exe
