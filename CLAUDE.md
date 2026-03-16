# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

WhisperShroom is a single-file Windows 11 voice-to-text app using OpenAI's Whisper API. It runs as a system tray application, records audio via a global hotkey, transcribes to German text, and displays results in a tkinter GUI.

## Build & Run

```bash
# Install dependencies
pip install -r requirements.txt

# Run directly
python whisper_voice.py

# Build standalone EXE (output in dist/)
python -m PyInstaller --clean --noconfirm whisper_voice.spec
```

There are no tests or linting configured.

## Architecture

The entire app lives in **`whisper_voice.py`** (~600 lines). Key sections:

- **`NativeHotkey` class** — Registers global hotkeys via the Windows `RegisterHotKey` API (ctypes). Runs its own message loop in a daemon thread. This replaced the `keyboard` library for stability on recent Windows versions.
- **`parse_hotkey()`** — Converts hotkey strings like `"ctrl+shift+r"` into Windows modifier flags + virtual key codes.
- **`WhisperShroom` class** — The main application:
  - **State machine UI**: `show_ready_state()` → `show_recording_state()` → `show_loading_state()` → `show_result_state()` / `show_error_state()`. All states render into a shared `self.container` frame.
  - **Audio pipeline**: `sounddevice.InputStream` callback → numpy buffer → WAV temp file → OpenAI Whisper API → display result.
  - **Threading**: Tray icon (`pystray`) runs in a daemon thread. Hotkey listener runs in its own thread. Transcription runs in a daemon thread. All UI updates marshal back to tkinter's main thread via `self.root.after()`.
- **Config** is stored at `%APPDATA%\WhisperShroom\config.json` (API key + hotkey string).
- **`whisper_voice.spec`** — PyInstaller spec for single-EXE build. Bundles `icon.ico` as a data file.
- **`resource_path()`** — Resolves bundled resources in both dev mode and PyInstaller `_MEIPASS` mode.

## Key Conventions

- UI language is German (labels, error messages, button text).
- Transcription is hardcoded to German (`language="de"`).
- All windows use `attributes('-topmost', True)` to stay on top.
- The hotkey format in config uses `+`-separated lowercase tokens (e.g., `ctrl+shift+e`).
