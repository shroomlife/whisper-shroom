# WhisperShroom 🎤

Ein natives Windows 11 Tool für Voice-to-Text mit OpenAI Whisper API. Gebaut mit WinUI 3 und .NET 10.

## Features

- **System Tray Icon** - Läuft unauffällig im Hintergrund
- **Globaler Hotkey** - Konfigurierbarer Hotkey (Standard: `Ctrl+Shift+E`)
- **Echtzeit-Timer** - Zeigt die Aufnahmedauer an
- **Silence Detection** - Warnung wenn kein Audiosignal erkannt wird
- **Whisper API** - Nutzt OpenAIs modernste Spracherkennung
- **Deutschsprachig** - Optimiert für deutsche Transkription
- **Ein-Klick Kopieren** - Text direkt in die Zwischenablage
- **Fluent Design** - Native Windows 11 UI mit Mica/Acrylic

## Installation

1. **Zertifikat installieren** (einmalig): Rechtsklick auf `WhisperShroom_Dev.cer` → "Zertifikat installieren" → "Lokaler Computer" → "Vertrauenswürdige Personen"
2. **App installieren**: Doppelklick auf `WhisperShroom_x.x.x.x_x64.msix`
3. **Update**: Einfach neue `.msix` doppelklicken - Windows updated automatisch

## Benutzung

1. **Starten** - App aus dem Startmenü starten
2. **API-Key** - Beim ersten Start OpenAI API-Key eingeben
3. **Aufnehmen** - Hotkey drücken oder Tray-Icon klicken
4. **Stoppen** - Nochmal Hotkey oder Stop-Button
5. **Kopieren** - "Text kopieren" Button oder Ctrl+C

## Build

### Voraussetzungen

- Windows 10/11 (x64)
- .NET 10 SDK
- Windows App SDK

### Build-Schritte

```bash
# Debug (unsigned)
dotnet build src/WhisperShroom/WhisperShroom/WhisperShroom.csproj -p:Platform=x64 -p:AppxPackageSigningEnabled=false

# Release MSIX
dotnet build src/WhisperShroom/WhisperShroom/WhisperShroom.csproj -c Release -p:Platform=x64
```

Die `.msix` liegt dann in `dist/`.

## Konfiguration

```
%APPDATA%\WhisperShroom\config.json
```

Kann jederzeit über das Tray-Menü → Einstellungen geändert werden.

## Technologie

| Bereich | Technologie |
|---------|------------|
| UI Framework | WinUI 3 (Windows App SDK) |
| Sprache | C# / .NET 10 |
| Audio | NAudio (WASAPI) |
| API | OpenAI Whisper |
| Deployment | MSIX (Sideload) |

---

Made for Robin 🍄
