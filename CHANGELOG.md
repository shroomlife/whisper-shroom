# Changelog

All notable changes to WhisperShroom will be documented in this file.

## [1.8.3.0] - 2026-06-01

### Added
- **Large recording support**: Recordings whose audio file exceeds OpenAI's 25 MB upload limit are now automatically split into multiple WAV chunks (by file size), transcribed sequentially, and stitched back together. Cost/usage is summed across chunks.

### Changed
- **Longer transcription timeout**: The request timeout was raised from the 100 s default to 5 minutes, so long recordings no longer fail with a spurious timeout.

## [1.8.2.0] - 2026-06-01

### Fixed
- **Retry button**: The "Retry" button in the main window error panel now actually re-transcribes the failed recording instead of just resetting to the ready state.

### Changed
- **Crash-safe audio**: Recordings are now persisted to disk immediately when you stop, before transcription. The audio file is only deleted after a successful transcription, so nothing is lost if the app is closed or crashes mid-transcription.

### Added
- **Silent recording detection**: Recordings with no detectable audio are no longer sent to the API (saving cost). They are still kept as a retryable "No speech detected" entry so you can force a transcription attempt if needed.
- **Version display**: The installed app version is now shown at the bottom of the Settings window.

## [1.8.0.0] - 2026-03-27

### Added
- **Persistent audio retry**: When transcription fails (e.g. no internet, API error), the audio recording is now saved persistently. Failed transcriptions appear in History as "Not yet transcribed" with a retry button. Audio files are kept until successfully transcribed.
- **Delete history**: "Clear all history" button at the bottom of the History window, and "Delete this day" button for each day group. Both require confirmation.
- **Transcription history**: Full history view with SQLite persistence, grouped by month and day, with collapsible sections.
- **Cost tracking**: Per-transcription cost calculation and display in EUR, with daily and monthly totals in group summaries.
- **Month grouping**: History entries are now grouped by month, then by day, with summary statistics.

### Changed
- Cost display now always uses EUR symbol instead of cent notation.
- History window defaults to 1/4 of screen area (width/2 x height/2).
- All history expanders start collapsed by default.

### Fixed
- Scroll wheel handling in History window (DirectManipulation capture, ManipulationMode.System on child controls).
- Card spacing and padding in History view.

## [1.6.0.0] - 2025-12-14

### Added
- Transcription model selector (whisper-1, gpt-4o-transcribe, gpt-4o-mini-transcribe).
- Prompt prefix/suffix support for wrapping transcription output.

## [1.5.2] - 2025-11-23

### Fixed
- CI: Release even if Store build fails; fix msixupload path issues.

## [1.5.1] - 2025-11-23

### Fixed
- CI: Fix certificate signing and msixupload path issues.

## [1.5.0] - 2025-11-23

### Changed
- Updated package identity for Store and thumbprint-based signing.
- Simplified CI to sideload-only release.

## [1.4.2] - 2025-11-22

### Added
- Language selection (multiple languages beyond German).
- Audio level monitoring in setup wizard.
- API key validation and connection testing in setup wizard.
- Notification service and clipboard auto-copy.

### Changed
- UI language switched to English.
- Native Win32 context menu for system tray.
