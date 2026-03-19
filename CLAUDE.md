# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

WhisperShroom is a native Windows 11 voice-to-text desktop app using OpenAI's Whisper API. Built with **WinUI 3** (Windows App SDK) and **.NET 10** in C#. It runs as a system tray application, records audio via a configurable global hotkey, transcribes to German text, and displays results in a native Fluent Design UI.

## Build & Run

```bash
# Restore NuGet packages
dotnet restore src/WhisperShroom/WhisperShroom/WhisperShroom.csproj

# Build (Debug, unsigned for local dev)
dotnet build src/WhisperShroom/WhisperShroom/WhisperShroom.csproj -p:Platform=x64 -p:AppxPackageSigningEnabled=false

# Build MSIX (Release, signed with dev certificate)
dotnet build src/WhisperShroom/WhisperShroom/WhisperShroom.csproj -c Release -p:Platform=x64

# MSIX output: src/WhisperShroom/WhisperShroom/bin/x64/Release/.../AppPackages/
```

There are no tests or linting configured.

## Architecture

The app follows **MVVM** (CommunityToolkit.Mvvm) with clearly separated layers:

```
src/WhisperShroom/WhisperShroom/
  Program.cs              # Entry point, single-instance (AppLifecycle API)
  App.xaml.cs             # Application init, static service references
  Models/                 # AppState enum, AppConfig POCO, AudioDevice record
  Services/
    ConfigService.cs      # JSON config at %APPDATA%\WhisperShroom\config.json
    AudioService.cs       # NAudio WasapiCapture, silence detection (RMS)
    TranscriptionService.cs  # HttpClient -> OpenAI Whisper API
    HotkeyService.cs      # Win32 RegisterHotKey via CsWin32 P/Invoke
    HallucinationFilter.cs   # Known German Whisper hallucination strings
  ViewModels/
    MainViewModel.cs      # State machine (Ready/Recording/Loading/Result/Error)
    SettingsViewModel.cs  # Settings dialog logic
  Views/
    MainWindow.xaml       # Window shell (always-on-top, 560x420)
    MainPage.xaml         # 5 state panels, visibility-bound to CurrentState
    SettingsDialog.xaml   # ContentDialog for API key, mic, hotkey
  Helpers/
    HotkeyParser.cs       # "ctrl+shift+e" -> Win32 modifier flags + VK code
```

### Key Technology Choices

| Area | Technology | Why |
|------|-----------|-----|
| UI Framework | WinUI 3 (Windows App SDK) | Native Win11 Fluent Design |
| MVVM | CommunityToolkit.Mvvm | Source-generated, AOT-compatible partial properties |
| Audio | NAudio (WasapiCapture) | Direct WASAPI device enumeration, matches Win11 Sound Settings |
| Global Hotkey | CsWin32 P/Invoke (RegisterHotKey) | Type-safe Win32 interop |
| Tray Icon | H.NotifyIcon.WinUI | WinUI 3-compatible system tray |
| OpenAI API | HttpClient (no SDK) | Single endpoint, minimal dependencies |
| Deployment | MSIX packaged (sideloaded) | App identity for clipboard, proper install/update |

### State Machine

`AppState` enum drives UI visibility via `x:Bind`:
`Ready` -> `Recording` (timer, pulsing dot, silence warning) -> `Loading` (ProgressRing) -> `Result` (text + copy) | `Error`

### Threading Model

- **UI thread**: WinUI 3 DispatcherQueue (main thread)
- **Hotkey listener**: Dedicated background thread with Win32 GetMessage loop
- **Audio capture**: NAudio callback thread, RMS computed per buffer
- **Transcription**: `Task.Run` -> HttpClient async -> result dispatched to UI

## Key Conventions

- UI language is German (labels, error messages, button text).
- Transcription is hardcoded to German (`language="de"`).
- Main window is always-on-top via `OverlappedPresenter.IsAlwaysOnTop`.
- Window close hides to tray; "Beenden" exits the app.
- The hotkey format in config uses `+`-separated lowercase tokens (e.g., `ctrl+shift+e`).
- Config path: `%APPDATA%\WhisperShroom\config.json` (compatible with legacy Python version).
- ViewModels use `partial property` pattern (not field-backed `[ObservableProperty]`) for WinRT AOT compatibility.

## Versioning

- Version is in `Package.appxmanifest` (`<Identity Version="..."/>`).
- Format: `Major.Minor.Patch.0` (MSIX requires 4-part, last segment reserved by store = always 0).
- Current version: **1.0.5.0**
- Bump the version with every release build / bug fix round.
