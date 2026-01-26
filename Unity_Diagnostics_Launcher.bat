@echo off
chcp 65001 >nul
echo ===============================================
echo Unity Deep Diagnostics Launcher
echo Root Cause Analysis Mode
echo ===============================================

echo.
echo 1. Unity Normal Mode
echo 2. Unity Memory Diagnostics Mode
echo 3. Unity Full Debug Mode
echo 4. Open Unity Log File
echo.

set /p choice="Select option (1-4): "

if "%choice%"=="1" (
    echo Starting Unity in normal mode...
    "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" -projectPath "c:\Users\kumod\My project"
) else if "%choice%"=="2" (
    echo Starting Unity with memory diagnostics...
    echo This will capture detailed memory leak stack traces
    "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" -projectPath "c:\Users\kumod\My project" -diag-temp-memory-leak-validation
) else if "%choice%"=="3" (
    echo Starting Unity in full debug mode...
    echo Recording all detailed logs and profiling information
    "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe" -projectPath "c:\Users\kumod\My project" -diag-temp-memory-leak-validation -enableCodeCoverage -logFile "C:\Users\kumod\Desktop\Unity_Deep_Debug.log"
) else if "%choice%"=="4" (
    echo Opening Unity log file...
    notepad "%LOCALAPPDATA%\Unity\Editor\Editor.log"
) else (
    echo Invalid selection.
    pause
    goto :eof
)

echo.
echo Startup complete!
echo If problems occur, check:
echo - Unity Console logs
echo - DebugLogger panel (bottom right)
echo - Editor log file: %LOCALAPPDATA%\Unity\Editor\Editor.log
echo.

pause