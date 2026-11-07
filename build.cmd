@echo off
rem Builds DFGTControl.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem If assets\app.ico exists it becomes the app icon (exe, window and tray); otherwise a simple drawn icon is used.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OPTS=/nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll
set SRC=src\Core.cs src\Engine.cs src\Ds4.cs src\Program.cs src\MainForm.cs src\BindingDialog.cs src\Theme.cs src\DInput.cs
set ICON=
if exist assets\app.ico set ICON=/win32icon:assets\app.ico /resource:assets\app.ico,app.ico
"%CSC%" %OPTS% %ICON% /out:DFGTControl.exe %SRC% || exit /b 1
