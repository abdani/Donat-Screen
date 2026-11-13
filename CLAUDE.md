# Donat Screen

Portable Windows screenshot tray app. Hotkeys: area (default Ctrl+Shift+2), full screen of all monitors (Ctrl+Shift+1).

## Stack

- C# WinForms on **.NET Framework 4.8** (built into Windows 10/11 → one ~50 KB exe, nothing to install). Built with the .NET SDK.
- No NuGet packages. Win32 via P/Invoke (`Native` in Program.cs).
- Per-monitor DPI v2 via `app.manifest` — coordinates are physical pixels everywhere. Window layout is scaled by the
  *system* DPI (`ThemedForm.K()`), the same DPI GDI sizes fonts with; `DeviceDpi` stays 96 on .NET Framework.

## Commands

- Build: `dotnet build -c Release -o build` → `build\DonatScreen.exe` (overwrites the previous build).
- Test: `.\test.ps1` — first checks that every UI text has an English translation, then end-to-end on the real exe (hotkeys, overlay, clipboard paste into Word + Edge via Python Playwright, shortcut recording, autostart). **Takes over keyboard/mouse ~40 s**; mouse input is only sent while a Donat Screen window owns the foreground. Restores the user's Run entry.

## Layout

- `Program.cs` — entry/single instance, tray `App`, capture (raw `BitBlt` + `CAPTUREBLT`), `Settings`, `Hotkeys`, `Theme`, menu renderer.
- `RegionOverlay.cs` — frozen-screen region picker.
- `SettingsForm.cs` — settings window (rebuilt by `Build()` when the folder list changes), `HotkeyBox`.
- `SaveDialog.cs` — after-capture popup: preview, rename, a `FolderCheck` per offered folder (default pre-ticked, several allowed).
  Simpan/Enter saves into every ticked folder, then closes; Esc/close = default folder + auto name.
- Icons come from Segoe Fluent Icons / Segoe MDL2 Assets (`Theme.Symbols`, glyph constants in `Theme`).
- `Ui.cs` — `ThemedForm` base (layout in 96-DPI units scaled by `K()`), `Toggle`, modern `FolderPicker` (COM `IFileOpenDialog`).
- `Lang.cs` — `L.T("Indonesian key", args)`; `En` maps each key to English (`{0}` slots). English is the default.
  Switching in Settings is live: `App.ApplyLanguage()` relabels the tray menu, the window rebuilds itself.
- Settings live in `DonatScreen.ini` next to the exe (portable): `Dest=<visible 0/1>|<path>` lines (relative to the exe when inside it),
  `Default=<index>`, `Ask=0/1`, `Lang=en/id`. A v1.0 `Folder=` line is still read.
- Every capture goes to the clipboard *before* the save popup; files are never overwritten (`name (2).png`).
- Win modifier = `Hotkeys.Win` (bit 0x80000, unused by `Keys`); written to the ini as an extra `, Win` token.
- Shortcut recording uses a `WH_KEYBOARD_LL` hook that swallows keys while a `HotkeyBox` is focused.
  Clash checks: `Hotkeys.Conflict` (Windows-reserved list + rules) → `App.CanRegister` probe → other action.
- Autostart = `HKCU\...\Run\DonatScreen` with `--autostart` (silent start); `Autostart.Repair` follows a moved exe.
- Clipboard gets two formats: 24 bpp Bitmap (Office/chat) + raw `PNG` (browsers).

## Conventions

- Code, identifiers, comments: English. UI strings: written in Indonesian inside `L.T(...)`, with the English in `Lang.cs`
  (a new UI text needs both, or test.ps1 fails). test.ps1 drives the English UI.
- `Graphics.CopyFromScreen` rejects `CaptureBlt` on .NET Framework — keep the raw `BitBlt`.
- UI Automation sees WinForms (.NET Framework) controls only by window text, so give custom controls a `Text`.
- Folder lists (settings + save dialog) use `ThemedForm.ScrollList`: max `VisibleRows` (5), then a dark scrollbar.
  In settings, clicking a row's path line (`FolderCheck.PathClicked`) opens an inline editor; `CommitPath` refuses missing/duplicate folders.
- Paths are drawn by `Theme.DrawPath`. Don't combine `NoPadding` with `PathEllipsis`: GDI then skips the ellipsis on slight overflow.
- test.ps1 finds windows by title (`TopWindow`), not `Process.MainWindowHandle` (can be a tooltip), and reads other-process text with
  `WM_GETTEXT` + timeout. It clears the user's autostart entry at start and restores it, even on failure.
- test.ps1: never send keystrokes without first checking a Donat Screen window owns the foreground (the user may be
  working on the same desktop); prefer UIA patterns. Avoid non-ASCII literals in .ps1 (read as ANSI) and the name `Type` (alias).
