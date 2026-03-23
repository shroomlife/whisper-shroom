# 🍄 WhisperShroom

**Voice-to-Text for Windows 11 — powered by OpenAI Whisper**

---

Most AI tools still don't offer voice input. WhisperShroom fills that gap. It's a lightweight system tray app that records your voice via a global hotkey, sends it to OpenAI's Whisper API, and puts the transcribed text on your clipboard — ready to paste anywhere.

Whisper's German speech recognition is exceptionally accurate, making WhisperShroom the perfect companion for dictating into any app — chat windows, editors, browser forms, you name it.

## ✨ Features

- 🎤 **Global Hotkey** — Start/stop recording from anywhere (default: `Ctrl+Shift+E`, fully customizable)
- 🖱️ **Tray Icon Control** — Left-click to toggle recording, right-click for the context menu
- ⏱️ **Live Recording Timer** — See exactly how long you've been recording
- 🔇 **Silence Detection** — Get a warning if no audio signal is detected
- 📋 **One-Click Copy** — Transcribed text goes to your clipboard instantly
- ✏️ **Editable Results** — Review and edit the transcription before copying
- 🎛️ **Microphone Selection** — Pick any input device, right from the tray menu
- 🧠 **Hallucination Filter** — Automatically rejects known Whisper artifacts (subtitle credits, etc.)
- 🪟 **Native Windows 11 UI** — Built with WinUI 3 and Fluent Design
- 🔒 **Runs in the Tray** — Stays out of your way, always one hotkey press away

## 🚀 Quick Start

### Prerequisites

- **Windows 10** (1809+) or **Windows 11**
- An **OpenAI API key** ([get one here](https://platform.openai.com/api-keys))

### Install via Microsoft Store (recommended)

Search for **WhisperShroom** in the Microsoft Store or visit the [Store page](https://apps.microsoft.com/detail/9MX4RZGVCZMJ).

### Install via Sideload

1. Download the `.cer` and `.msix` files from the latest [Release](https://github.com/shroomlife/whisper-shroom/releases)
2. Install the certificate (one-time setup) — open an **Admin PowerShell** and run:
   ```powershell
   Import-Certificate -FilePath "$HOME\Downloads\WhisperShroom_*.cer" -CertStoreLocation "Cert:\LocalMachine\TrustedPeople"
   ```
3. Double-click the `.msix` to install

### Setup

1. Launch **WhisperShroom** from the Start Menu
2. Enter your OpenAI API key in the Settings dialog
3. Done — press your hotkey or click the tray icon to start recording! 🎉

## 🎯 Usage

| Action | What happens |
|--------|-------------|
| **Press hotkey** (`Ctrl+Shift+E`) | Start/stop recording |
| **Left-click** tray icon | Start/stop recording |
| **Double-click** tray icon | Show the main window |
| **Right-click** tray icon | Open context menu (microphone selection, settings, quit) |

### Recording Flow

1. Press your hotkey or left-click the tray icon
2. Speak — the app shows a live timer and a pulsing red dot 🔴
3. Press the hotkey again (or click Stop) to finish
4. Whisper transcribes your audio in seconds
5. Click **Copy Text** to paste it anywhere ✨

## 🛠️ Build from Source

```bash
# Restore dependencies
dotnet restore src/WhisperShroom/WhisperShroom/WhisperShroom.csproj

# Debug build (unsigned, for local development)
dotnet build src/WhisperShroom/WhisperShroom/WhisperShroom.csproj -p:Platform=x64 -p:AppxPackageSigningEnabled=false

# Release build (MSIX package)
dotnet build src/WhisperShroom/WhisperShroom/WhisperShroom.csproj -c Release -p:Platform=x64
```

Requires **.NET 10 SDK** and the **Windows App SDK**.

## 🏗️ Tech Stack

| Component | Technology |
|-----------|-----------|
| UI Framework | WinUI 3 (Windows App SDK 1.7) |
| Runtime | .NET 10 (C#) |
| MVVM | CommunityToolkit.Mvvm |
| Audio Capture | NAudio (WASAPI) |
| Global Hotkey | Win32 `RegisterHotKey` via CsWin32 |
| System Tray | H.NotifyIcon.WinUI |
| Speech-to-Text | OpenAI Whisper API |
| Packaging | MSIX (sideloaded) |

## 👨‍💻 Developed by

**[shroomlife](https://github.com/shroomlife)**

---

<sub>Built with 🍄 and a lot of voice recordings.</sub>
