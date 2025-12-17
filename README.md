# WhisperShroom 🎤

Ein simples Windows 11 Tool für Voice-to-Text mit OpenAI Whisper API.

## Features

- **System Tray Icon** - Läuft unauffällig im Hintergrund
- **Hotkey** - `Ctrl+Shift+R` zum Starten/Stoppen der Aufnahme
- **Echtzeit-Timer** - Zeigt die Aufnahmedauer an
- **Whisper API** - Nutzt OpenAIs modernste Spracherkennung
- **Deutschsprachig** - Optimiert für deutsche Transkription
- **Ein-Klick Kopieren** - Text direkt in die Zwischenablage

## Benutzung

1. **Starten** - Doppelklick auf `WhisperShroom.exe`
2. **API-Key** - Beim ersten Start OpenAI API-Key eingeben
3. **Aufnehmen** - `Ctrl+Shift+R` drücken oder Tray-Icon klicken
4. **Stoppen** - Nochmal `Ctrl+Shift+R` oder Stop-Button
5. **Kopieren** - Text aus dem Ergebnis-Fenster kopieren

## Build (selbst kompilieren)

### Voraussetzungen

- Windows 10/11 (x64)
- Python 3.10 oder höher
- OpenAI API Key

### Build-Schritte

```bash
# 1. Abhängigkeiten installieren
pip install -r requirements.txt

# 2. Build ausführen
python -m PyInstaller --clean --noconfirm whisper_voice.spec
```

Die fertige `WhisperShroom.exe` liegt dann in `dist/`.

## Konfiguration

Der API-Key wird gespeichert in:
```
%APPDATA%\WhisperShroom\config.json
```

Kann jederzeit über das Tray-Menü geändert werden.

## Tastenkombination

| Hotkey | Aktion |
|--------|--------|
| `Ctrl+Shift+R` | Aufnahme starten/stoppen |

## Hinweise

- Das Tool nutzt das Standard-Mikrofon von Windows
- Die Audioqualität ist auf 16kHz optimiert für Whisper
- Die Transkription erfolgt auf Deutsch (`language="de"`)

## Troubleshooting

**"API-Key ungültig"**  
→ Prüfe deinen OpenAI API-Key unter https://platform.openai.com/api-keys

**"Kein Mikrofon gefunden"**  
→ Stelle sicher, dass ein Mikrofon als Standard-Eingabegerät gesetzt ist

**Antivirus blockiert**  
→ PyInstaller-EXEs werden manchmal fälschlich erkannt, Ausnahme hinzufügen

---

Made for Robin 🍄
