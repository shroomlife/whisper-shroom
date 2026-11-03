# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

WhisperShroom is a native Windows 11 voice-to-text desktop app built on OpenAI's audio transcription API (`POST /v1/audio/transcriptions`). Built with **WinUI 3** (Windows App SDK) and **.NET 10** in C#. It runs as a system tray application, records audio via a configurable global hotkey, transcribes it, and shows the result in a native Fluent Design UI. Transcription model and language are both user-configurable in Settings.

## Build & Run

```bash
# Restore NuGet packages
dotnet restore src/WhisperShroom/WhisperShroom/WhisperShroom.csproj

# Build (Debug, unsigned for local dev)
dotnet build src/WhisperShroom/WhisperShroom/WhisperShroom.csproj -p:Platform=x64 -p:AppxPackageSigningEnabled=false

# Build MSIX (Release, signed with the dev certificate)
dotnet build src/WhisperShroom/WhisperShroom/WhisperShroom.csproj -c Release -p:Platform=x64

# MSIX output: dist/WhisperShroom_<version>_x64_Test/  (also copied flat into dist/)
```

`AppxPackageDir` in the csproj redirects packaging output to `dist/` at the repo root, and a `CopyMsixToDistRoot` target copies the `.msix` up into `dist/` itself. Nothing lands in `bin/**/AppPackages/`.

Install a local build with `Add-AppxPackage -Path dist/WhisperShroom_<version>_x64.msix`.

There are no tests, no linting and no solution file configured. The only quality gate is the compiler.

## Architecture

The app follows **MVVM** (CommunityToolkit.Mvvm) with clearly separated layers:

```
src/WhisperShroom/WhisperShroom/
  Program.cs              # Custom entry point (DISABLE_XAML_GENERATED_MAIN): single-instance
                          #   via AppLifecycle, global crash handlers -> %APPDATA%\WhisperShroom\crash.log
  App.xaml.cs             # Application init, static service singletons, setup-wizard bootstrap
  Models/
    AppState.cs           # Ready / Recording / Loading / Result / Error
    AppConfig.cs          # Config POCO, snake_case JSON keys
    AudioDevice.cs        # record (Id, Name)
    HistoryPeriodSummary.cs  # SQL-aggregated totals for one month or day
    TranscriptionResult.cs   # API result + usage (tokens or duration)
    TranscriptionEntry.cs    # One history row
    WizardStep.cs         # ApiKey / Hotkey / AudioDevice / Features
  Services/
    ConfigService.cs      # JSON config at %APPDATA%\WhisperShroom\config.json
    AudioService.cs       # NAudio WasapiCapture @ 16 kHz mono 16-bit streamed to a WAV file,
                          #   RMS silence detection, plus a monitor capture for the wizard's level test
    TranscriptionService.cs  # HttpClient -> OpenAI audio API; splits >24 MB WAVs into chunks
    HistoryService.cs     # SQLite persistence at %APPDATA%\WhisperShroom\history.db; month/day
                          #   aggregates, per-day entries, search (contains_ci UDF), recovery
    HotkeyService.cs      # Win32 RegisterHotKey via CsWin32 P/Invoke
    NotificationService.cs   # AppNotificationManager toasts on completed transcription
    HallucinationFilter.cs   # Known German Whisper hallucination strings (Amara/ZDF/SWR subtitles etc.)
  ViewModels/
    MainViewModel.cs      # State machine, recording lifecycle, pending-entry tracking
    SettingsViewModel.cs  # Settings logic, fetches available models from the API
    SetupWizardViewModel.cs  # First-run wizard, API key validation
    HistoryViewModel.cs   # Async (thread pool) loads for the History window, delete, retry
    MonthGroup.cs         # Month header (label + summary) from a HistoryPeriodSummary
    DayGroup.cs           # Day header (label + summary) from a HistoryPeriodSummary
  Views/
    MainWindow.xaml       # Window shell (always-on-top, 600x500), tray icon, native context menu
    MainPage.xaml         # 5 state panels, visibility-bound to CurrentState
    SettingsWindow.xaml   # Settings (Window, not a ContentDialog)
    SetupWizardWindow.xaml   # First-run wizard
    HistoryWindow.xaml    # History: months load first, days/entries load on expand, search
    TextEditorWindow.xaml # Small reusable text editor, used for the prefix/suffix fields
  Helpers/
    TranscriptionModelHelper.cs  # Model whitelist, display names, default model
    LanguageHelper.cs     # "German (de)" <-> "de", "Auto-detect" <-> null
    CostCalculator.cs     # Per-model USD pricing -> EUR, formatting
    TranscriptionWorkflow.cs  # Shared transcribe-and-resolve core for the main flow and History retry
    HotkeyParser.cs       # "ctrl+shift+e" -> Win32 modifier flags + VK code
    NativeMenu.cs         # Manual P/Invoke for Win32 popup menus (CreatePopupMenu, TrackPopupMenuEx)
    ClipboardHelper.cs    # Win32 clipboard P/Invoke
    WavFileRepair.cs      # Fixes RIFF/data sizes of WAVs left behind by an interrupted recording
    AppInfo.cs            # Installed MSIX package version
```

### Key Technology Choices

| Area | Technology | Why |
|------|-----------|-----|
| UI Framework | WinUI 3 (Windows App SDK 2.6) | Native Win11 Fluent Design |
| MVVM | CommunityToolkit.Mvvm 8 | Source-generated, AOT-compatible partial properties |
| Audio | NAudio 2 (WasapiCapture) | Direct WASAPI device enumeration, matches Win11 Sound Settings |
| Global Hotkey | CsWin32 P/Invoke (RegisterHotKey) | Type-safe Win32 interop |
| Tray Icon | H.NotifyIcon.WinUI 2.5 (prerelease) | WinUI 3-compatible system tray (icon + click detection only) |
| Tray Context Menu | Win32 TrackPopupMenuEx (manual P/Invoke) | H.NotifyIcon's menu modes have positioning/sizing bugs |
| OpenAI API | HttpClient (no SDK) | Single endpoint, minimal dependencies |
| History DB | Microsoft.Data.Sqlite 10 | MS-recommended for WinUI 3, uses Windows-bundled SQLite |
| Deployment | MSIX packaged (sideloaded) | App identity for clipboard, proper install/update |

Target framework is `net10.0-windows10.0.26100.0` with `TargetPlatformMinVersion` 10.0.17763.0. Platforms: x86, x64, ARM64.

### State Machine

`AppState` drives UI visibility via `x:Bind` across the five panels in `MainPage.xaml`:
`Ready` -> `Recording` (timer, pulsing dot, silence warning) -> `Loading` (ProgressRing) -> `Result` (text + copy) | `Error`

`MainViewModel.OnCurrentStateChanged` is the single place that mirrors the state outside the view model: it sets the tray icon (`MainWindow.SetTrayState`: red badge recording, blue badge transcribing) and shows the window on every `Error`. By default (`show_window_while_recording: false`) recording runs in **tray mode** and never opens the window on its own. Reason: the UI Automation fail-fast crash microsoft/microsoft-ui-xaml#11139 only hits visible WinUI windows; the tray icon is plain Win32.

### Threading Model

- **UI thread**: WinUI 3 DispatcherQueue (main thread)
- **Hotkey listener**: dedicated background thread (`HotkeyListener`) with a Win32 `GetMessage` loop; `Register` waits until its message queue exists (documented `PostThreadMessage` pattern)
- **Audio capture**: NAudio capture thread is the only thread that touches the `WaveFileWriter` while recording. `WasapiCapture.StopRecording()` only signals; `AudioService.StopRecordingAsync` awaits `RecordingStopped` before finalizing the file
- **Process priority**: the tray icon is created with `ForceCreate(enablesEfficiencyMode: false)`; H.NotifyIcon's default would pin the process to `IDLE_PRIORITY_CLASS` + EcoQoS
- **Transcription**: `Task.Run` in `TranscriptionWorkflow` -> HttpClient async -> result dispatched to UI

### Transcription Flow

Recordings stream into `%APPDATA%\WhisperShroom\audio\` while recording (header refreshed every 2 s) and are written to the DB as a **pending** entry *before* the API call, so nothing is lost on crash. On start, `HistoryService.RecoverOrphanedRecordings` turns WAVs no entry references into pending entries (header repaired via `WavFileRepair`) and deletes empty ones. While `Loading`, the hotkey toggle is ignored. On success the entry is completed and the WAV deleted; on failure the audio is kept and the entry stays retryable from the error panel or History. Silent recordings are never sent to the API. `TranscriptionWorkflow` is the single implementation shared by the main flow and the History retry.

## Transcription Models

The Settings dropdown is populated by intersecting `GET /v1/models` with a hardcoded whitelist in `TranscriptionModelHelper`. New models must be added there or they will never appear.

| Model | Billing | Notes |
|---|---|---|
| `gpt-transcribe` | $0.0045 / min | Default. OpenAI's recommended file-transcription model, returns `usage.type = "duration"` |
| `whisper-1` | $0.006 / min | Needs `response_format=verbose_json` to report duration |
| `gpt-4o-transcribe` | $6.00 / 1M audio-in, $10.00 / 1M text-out | Returns `usage.type = "tokens"` |
| `gpt-4o-mini-transcribe` | $3.00 / 1M audio-in, $5.00 / 1M text-out | Returns `usage.type = "tokens"` |

Pricing lives in `CostCalculator`, split into `MinutePricing` (duration-billed) and `TokenPricing` (token-billed), converted to EUR by a fixed `UsdToEur` constant.

**`gpt-transcribe` takes the language hint as `languages[]`, every other model as `language`.** Sending both is rejected by the API, so `TranscriptionModelHelper.UsesLanguagesArray` decides which form `TranscriptionService` writes into the multipart body.

## Key Conventions

- UI language is English (labels, error messages, button text).
- Transcription language is configurable and defaults to **Auto-detect** (`language: null`), which sends no language field at all. `LanguageHelper` maps between the display name and the ISO 639-1 code.
- Main window is always-on-top via `OverlappedPresenter.IsAlwaysOnTop`, fixed at 600x500, and starts off-screen to avoid a visible flash.
- Closing the window hides it to the tray (in window mode this also cancels a running recording, in tray mode the recording continues); the tray menu's "Quit" exits the app. Tray menu: Toggle Recording, hotkey display (grayed), Microphone submenu, Settings, History, Quit.
- The hotkey format in config uses `+`-separated lowercase tokens (e.g., `ctrl+shift+e`).
- Config path: `%APPDATA%\WhisperShroom\config.json`. The path and the snake_case keys (`api_key`, `device_name`, `auto_copy`) are inherited from the pre-WinUI Python version and kept compatible.
- Optional prefix/suffix text is stored in the config and wrapped around the transcript when copying; each can be toggled off per result.
- ViewModels use the `[ObservableProperty]` + `partial property` pattern (not field-backed `[ObservableProperty]`) for WinRT AOT compatibility.
- Uploads larger than `MaxUploadBytes` (24,000,000 bytes, safely under OpenAI's 25 MB limit) are split into size-based WAV chunks, transcribed sequentially, then stitched with summed usage.

## Versioning

- Version is in `Package.appxmanifest` (`<Identity Version="..."/>`).
- Format: `Major.Minor.Patch.0` (MSIX requires 4-part, last segment reserved by store = always 0).
- Current version: **1.13.0.0**
- Bump the version with every release build / bug fix round, add a `CHANGELOG.md` entry, and tag the commit `v<version>` to trigger `.github/workflows/release.yml`.
