@echo off
rem ============================================================
rem  Build SeewoPageButtonsPatch.exe
rem  Uses only the C# compiler that ships with Windows
rem  (.NET Framework 4.x, no Visual Studio / SDK required).
rem
rem  Two build flavors:
rem   1) With built-in restore data (recommended for personal use):
rem      put your own ORIGINAL (unpatched) Sia.PptAssistant.dll at
rem      embedded\Sia.PptAssistant.dll.orig  before building.
rem      Note: this file is NOT in the git repo (proprietary binary);
rem      copy it from your local SeewoIwbAssistant installation.
rem   2) Without it: still builds fine. "Restore" then relies on the
rem      .bak backup that the tool creates automatically at patch time.
rem
rem  Output: ..\SeewoPageButtonsPatch.exe  (one level up)
rem ============================================================
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. Please install .NET Framework 4.x.
    exit /b 1
)

set SRC="%~dp0Program.cs" "%~dp0MainForm.cs" "%~dp0PatchEngine.cs"
set OUT=%~dp0..\SeewoPageButtonsPatch.exe
set REF=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll

if exist "%~dp0embedded\Sia.PptAssistant.dll.orig" (
    "%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
      /out:"%OUT%" /win32manifest:"%~dp0app.manifest" %REF% ^
      /resource:"%~dp0embedded\Sia.PptAssistant.dll.orig",SeewoPatchTool.OriginalDll ^
      %SRC%
) else (
    echo [NOTE] embedded\Sia.PptAssistant.dll.orig not found.
    echo        Building WITHOUT built-in restore data. Restore will rely on the .bak backup.
    echo        To embed restore data, copy the original Sia.PptAssistant.dll from your
    echo        SeewoIwbAssistant install into source\embedded\Sia.PptAssistant.dll.orig
    echo        and rebuild.
    "%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
      /out:"%OUT%" /win32manifest:"%~dp0app.manifest" %REF% ^
      %SRC%
)

if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)
echo [OK] Built %OUT%
