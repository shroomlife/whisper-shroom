@echo off
chcp 65001 >nul
title WhisperVoice Builder

echo.
echo  ========================================
echo       WhisperVoice Build Script
echo  ========================================
echo.

REM Check Python
where python >nul 2>&1
if errorlevel 1 (
    echo [FEHLER] Python nicht gefunden!
    echo Bitte Python installieren mit "Add to PATH" Option.
    echo.
    pause
    exit /b 1
)

echo [OK] Python gefunden
python --version
echo.

echo [1/4] Upgrade pip...
python -m pip install --upgrade pip --quiet

echo [2/4] Installiere Abhaengigkeiten...
python -m pip install openai sounddevice numpy pystray Pillow keyboard pyinstaller --quiet

echo [3/4] Baue WhisperVoice.exe...
echo      (Das dauert 1-2 Minuten)
echo.
python -m PyInstaller --clean --noconfirm whisper_voice.spec 2>nul

if exist "dist\WhisperVoice.exe" (
    echo.
    echo  ========================================
    echo    BUILD ERFOLGREICH!
    echo  ========================================
    echo.
    echo  Deine App: dist\WhisperVoice.exe
    echo.
    echo [4/4] Oeffne Output-Ordner...
    explorer dist
) else (
    echo.
    echo [FEHLER] Build fehlgeschlagen!
    echo.
)

pause
