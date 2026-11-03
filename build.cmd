@echo off
rem Builds MathSnipBoard.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:icon.ico /resource:icon.ico,app.ico ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /out:MathSnipBoard.exe src\*.cs
if errorlevel 1 exit /b 1
echo Built MathSnipBoard.exe
