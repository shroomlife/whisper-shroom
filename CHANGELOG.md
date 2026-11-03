# Changelog

All notable changes to WhisperShroom will be documented in this file.

## [1.13.3.0] - 2026-11-03

1.13.0.0 to 1.13.2.0 were local test builds and were not released; this version supersedes them.

### Fixed
- **Tray icon crash after the first transcription**: The tray state icons are loaded once and reused, but assigning `TaskbarIcon.Icon` makes H.NotifyIcon dispose the previous icon (`OnIconChanged` calls `oldValue?.Dispose()`). Switching back to an already used icon then failed with "Cannot access a disposed object. Object name: 'Icon'", which also skipped the clipboard copy and the notification after the transcription had been saved. The state icons are now applied with H.NotifyIcon's `UpdateIcon`, which changes the shell icon without replacing (and disposing) the property value.

### Added
- **Outcome feedback in tray mode**: The tray icon now works like a traffic light: red while recording, yellow while transcribing, then green for five seconds after a successful transcription or red for five seconds after an error, before the normal icon returns. The tooltip says what happened ("Done, copied to clipboard" or "Failed, see window"). This gives visible feedback in tray mode even with notifications turned off. Errors still open the window with Retry.

### Changed
- **Standard error overlay**: Errors use a red badge with a white "x", the overlay the Windows notification area guidelines prescribe for errors, so an error no longer looks like a running recording.
- **Sharp tray icons on scaled displays**: `Icon(Stream)` returns the smallest image in an .ico (16 px), which the shell upscaled at 125 % and above. Tray icons are now loaded at the notification area's size (`GetSystemMetricsForDpi(SM_CXSMICON)` for the system DPI), and every tray icon ships 16, 20, 24, 32 and 40 px images as the guidelines ask for.
- **SQLite write-ahead logging**: The history database switches to WAL, which Microsoft.Data.Sqlite recommends for concurrent access. Since the History window reads on the thread pool, a transcription finishing while it loads no longer has to wait for the reader or vice versa.

## [1.13.0.0] - 2026-11-03

### Added
- **Tray mode (default)**: Recording no longer opens the window. The tray icon shows the state instead: a red badge while recording, a yellow badge while transcribing, and the tooltip says what is happening and which hotkey stops the recording. The result lands on the clipboard and in the notification as before. The window opens automatically on any error (with Retry), and clicking the tray icon still works as before.
- **Setting "Show window while recording"**: Restores the previous behavior where the window opens for every recording. Stored as `show_window_while_recording`, off by default.

### Changed
- **Closing the window in tray mode keeps the recording**: With the window as the recording UI, closing it still cancels the recording. In tray mode the window is optional, so hiding it no longer discards a running recording.
- **Recording animation only when visible**: The pulsing dot runs only while the window is shown.

### Why
The UI Automation fail-fast crash (microsoft/microsoft-ui-xaml#11139) only affects visible WinUI windows; a cross-process UI Automation client cannot reach the XAML tree of a hidden window. The tray icon is a plain Win32 notification icon, so tray mode keeps the window hidden during everyday use and out of reach of that framework bug.

## [1.12.0.0] - 2026-11-03

### Changed
- **History opens instantly**: The window used to load all entries twice (once in the view model, once in the window) and build a card for every single entry, including all collapsed ones, before it appeared. With about 3,000 entries that meant tens of thousands of UI elements. The window now opens immediately with a progress ring, month headers come from SQL aggregates, a month's days load when it is expanded and a day's entries when the day is expanded. Everything is released when the window closes.
- **Totals computed in SQL**: Month and day headers (entries, days, pending, tokens, audio seconds, cost) are aggregated by SQLite per model and usage type, and `CostCalculator` stays the only place that knows prices. Verified against the previous per-entry calculation on a real history: identical entries, days and cost for every month.
- **Search runs in the database**: Matching and the 100-result cap happen inside the query instead of loading every entry. A registered `contains_ci` function keeps the exact case-insensitive comparison the in-memory search used (SQLite's own `LIKE` only folds ASCII, so "Ä" would not match "ä").
- **Targeted refresh**: A new transcription, retry or delete refreshes the headers and reloads only the sections that are expanded, keeping them open. Previously every change rebuilt the whole history, and delete handlers rebuilt it a second time on top of the change notification.

### Fixed
- **Search debounce leaked handlers**: Every keystroke attached another `Tick` handler to the debounce timer, so a long query rebuilt the results once per typed character. The handler is now attached once.
- **Late results**: A slow load that finished after a newer refresh, a new search or closing the window could render stale data. Each refresh carries a generation number and outdated results are discarded.

## [1.11.0.0] - 2026-11-03

### Fixed
- **Last moment of a recording could be lost**: Stopping read the WAV out of memory right after asking NAudio to stop, while its capture thread could still be delivering the final buffer, and disposed the writer underneath it. `WasapiCapture.StopRecording()` only signals the capture thread; the recording is now finalized after NAudio raises `RecordingStopped`, so the final buffer is always included and no two threads touch the writer.
- **Hotkey during transcription started a second recording**: The toggle only checked for the recording state, so pressing the hotkey while a transcription was running started a new recording, broke the UI state when the first one finished and could make Retry target the wrong entry. The toggle is now ignored while transcribing.
- **Hotkey registration could silently fail**: The listener thread published its thread ID only after it had started, so a fast re-registration (opening and saving Settings) could miss it, leave the old thread holding the hotkey and make the new registration fail without notice. Registration now follows the documented `PostThreadMessage` pattern (message queue created via `PeekMessage`, readiness signalled before returning), and Settings report when another application already owns the chosen combination.
- **Clipboard copy ignored Win32 failures**: `GlobalAlloc`, `GlobalLock` and `SetClipboardData` results are checked, and the memory is freed when ownership was not transferred to the system.

### Changed
- **Normal process priority**: The tray icon was created with H.NotifyIcon's default `enablesEfficiencyMode: true`, which put the whole app into `IDLE_PRIORITY_CLASS` plus EcoQoS for its entire lifetime, including recording and upload. Microsoft's guidance is that EcoQoS must not be used for foreground or performance-critical work. Windows now derives the quality of service from window visibility on its own.
- **Recordings stream straight to disk**: Audio is written into `%APPDATA%\WhisperShroom\audio\` while recording instead of being buffered in memory and copied three times on stop. The WAV header is refreshed every two seconds, so a crash or forced exit leaves a valid file behind.
- **Interrupted recordings are recovered**: On start, WAV files in the audio folder that no history entry references are repaired and listed in History as pending ("Recording was interrupted. Retry to transcribe."). Files without audio data are removed.
- **Capture device unplugged mid-recording**: The error is reported instead of being swallowed, and the audio captured up to that point stays retryable.
- **Symbol package in releases**: The MSIX tooling that ships with Windows App SDK 2.x writes the symbol package as `.appxsym` instead of `.msixsym`, so 1.10.0.0 was released without one. The release workflow accepts both.

## [1.10.0.0] - 2026-11-03

### Known issues
- **Crash when UI Automation clients query the window (not fixed)**: The app can be terminated by a fail-fast (`0xc000027b`, stowed `RPC_E_WRONG_THREAD`) without any user action. The symbolized crash dump shows a cross-process UI Automation client asking for the window's bounding rectangle on the UI Automation I/O thread; `CUIAWindow::get_BoundingRectangle` reaches `CXamlIslandRoot::GetScreenOffset`, which fails fast when called off the UI thread. No app code is involved, so the managed crash handlers cannot see it and `crash.log` stays empty. This is a framework bug tracked as microsoft/microsoft-ui-xaml#11139 and still reproduces with Windows App SDK 2.6.5.

### Changed
- **Windows App SDK 1.7 to 2.6.5**: 1.7 has been out of support since 2026-03-18 and no longer receives fixes. 2.6.5 is the current stable release. Installing the app now requires the Windows App Runtime 2.x framework package (`Microsoft.WindowsAppRuntime.2`).
- **Component packages instead of the metapackage**: Since 2.x the `Microsoft.WindowsAppSDK` metapackage also pulls Windows ML, Windows AI, Search and Widgets, which put `onnxruntime.dll` and `DirectML.dll` for every architecture into the MSIX and grew it from 29 MB to 82 MB. The app references only `Microsoft.WindowsAppSDK.WinUI`, `.Foundation` and `.Runtime` now, and the package is back at 29 MB.
- **H.NotifyIcon.WinUI 2.5.0-beta.3**: The stable 2.4.x line depends on the 1.6 metapackage, which collides with the 2.x component packages. 2.5 depends on `Microsoft.WindowsAppSDK.WinUI` only.
- **Dependency refresh**: Changing the SDK reference invalidates the lock file, so NuGet resolved the full graph again within the declared ranges: `Microsoft.Data.Sqlite` 10.0.14, `NAudio` 2.4.0, `Microsoft.Windows.CsWin32` 0.3.367, `Microsoft.Windows.SDK.BuildTools` 10.0.28000.2957.
- **SQLitePCLRaw pin removed**: `Microsoft.Data.Sqlite` 10.0.14 now requires `SQLitePCLRaw` 2.1.13 itself, which bundles the same patched SQLite 3.53.3 as the former 2.1.12 pin (CVE-2025-6965 stays fixed). The explicit pin turned into a downgrade error and is gone.

## [1.9.6.0] - 2026-08-03

### Added
- **Symbol package in releases**: The `.msixsym` produced since 1.9.5.0 is now attached to the GitHub release and described in the notes. It is not needed to run the app; it resolves stack traces from crash reports and is used for Store submissions. Like the certificate, it is optional: if a build produces none, the release is created without it and the workflow logs a warning.

## [1.9.5.0] - 2026-08-03

### Changed
- **Symbol package**: Every Release build warned that `mspdbcmf.exe` could not be found and skipped the symbol package. The tool was installed all along, but the Windows App SDK locates it through `VsInstallRoot`, which `dotnet build` does not set outside a Developer Command Prompt. The install root is now resolved through `vswhere`, which the VS Installer places at a fixed path, so it works on a developer machine and on the CI runner. Builds are warning-free and a `.msixsym` is produced again.
- **CI runtime**: `actions/upload-artifact` and `softprops/action-gh-release` still targeted the deprecated Node.js 20 and were being force-run on Node.js 24. Both moved to their current major versions, which target Node.js 24 natively. The remaining actions already did.

## [1.9.4.0] - 2026-08-03

### Security
- **CVE-2025-6965 (high, CVSS 7.2)**: The bundled SQLite engine was 3.49.1, which can corrupt memory when the number of aggregate terms exceeds the available columns. `SQLitePCLRaw` is pinned to 2.1.12, raising the engine to 3.53.3. `Microsoft.Data.Sqlite` 10.x still resolves the affected 2.1.11 by default; the pin can be removed once it ships a patched dependency itself. The existing history database was verified to open unchanged under the new engine (`PRAGMA integrity_check` passes).

## [1.9.3.0] - 2026-08-03

### Fixed
- **Dependency drift**: All package references float (`0.3.*`, `1.7.*`, ...), so CI and local builds resolved different versions. The shipped 1.9.2.0 was generated by CsWin32 0.3.298 while local builds had been tested against 0.3.275; `Microsoft.Data.Sqlite` and `Microsoft.Windows.SDK.BuildTools` had drifted too. A `packages.lock.json` now pins the full graph including transitive packages, and CI restores in locked mode so a drifting version fails the build instead of shipping untested code.

## [1.9.2.0] - 2026-08-03

### Changed
- **Reproducible builds**: The compiler no longer embeds the absolute build directory in the assembly, so the same commit produces the same binary on a developer machine and on CI, and the local directory layout no longer ships inside the package. Stack traces now report paths as `/_/Views/MainWindow.xaml.cs`; file names and line numbers are unchanged.

## [1.9.1.0] - 2026-08-03

### Fixed
- **Release build toolchain**: CI resolved a release-candidate .NET SDK while local builds used the GA SDK, so the published MSIX was never compiled by the toolchain the changes had been tested with. The SDK is now pinned in `global.json` for both.
- **Sideload instructions**: The certificate filename in the release notes is taken from the built artifact instead of being assembled from the tag, which had produced a five-part name (`1.9.0.0.0`) that matched no file. Affected v1.8.1.0 through v1.9.0.0; those release notes were corrected retroactively.

## [1.9.0.0] - 2026-08-03

### Added
- **GPT Transcribe**: Support for OpenAI's `gpt-transcribe` model, the currently recommended model for file transcription. It is more accurate than Whisper and cheaper at $0.0045 per minute versus $0.006. It is now the default for new installations; existing installations keep their configured model until changed in Settings.

### Changed
- Transcription requests now send the language hint as `languages[]` for `gpt-transcribe` and as `language` for all other models. The API rejects requests that send both.

### Fixed
- **Cost calculation**: Corrected the output token rate for `gpt-4o-transcribe` ($10.00 per 1M instead of $6.00) and `gpt-4o-mini-transcribe` ($5.00 per 1M instead of $3.00). Costs shown in History for those models were previously too low.

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
