# Worklog

## 2026-07-21 Split-Window IME And Atomic Write Safety

- Preserved the existing split-window interaction: double-click and **Edit card** continue editing directly inside the task card.
- Re-enable IME after switching the card from read-only to editable, then defer activation, WPF keyboard focus, and text selection until the initiating mouse/menu event has completed.
- The existing summary-window edit dialog also activates and explicitly gives its task box WPF keyboard focus after rendering.
- Hardened Markdown writes without weakening the existing column-hash or original-line checks:
  - serialize writes to the same board across app processes with a path-keyed named mutex;
  - re-read the source through a handle that denies concurrent writers while allowing atomic replacement;
  - write and flush a unique same-directory temporary file, then commit with `File.Replace`;
  - convert lock, permission, and I/O exceptions into visible write failures instead of letting them escape the UI event handler;
  - refuse task text containing CR/LF so a card edit cannot create additional Markdown lines.
- Restored public-default and localization regression coverage that had been removed from the local test file; removed its machine-specific real-board paths so the suite is hermetic again.
- Restored ignore rules for test output, coverage, temporary/backup files, and OS metadata, plus the public-safe sample language/path defaults.

Validation:

- `dotnet build DesktopOverlayBoard.sln` passed with 0 warnings and 0 errors.
- `dotnet run --no-build --project Tests\DesktopOverlayBoard.Tests.csproj` passed, including new multiline-refusal and locked-file failure tests.
- Published the framework-dependent win-x64 build to `dist` and restarted `GlassKanbanOverlay.exe --startup`.
- Runtime UI Automation verification on a restored split widget passed: **Edit card** kept editing inside the original task row, opened no `EditTaskWindow`, made the row editable, gave it keyboard focus, accepted temporary Chinese text, and restored the original text/read-only state on Escape.
- Captured and inspected `Log\verify-inline-ime-focus-20260721.png`; the temporary Chinese text is visible inside the existing task card. The probe canceled the edit, so no Markdown write occurred.
- Automated Microsoft Pinyin candidate-window verification remained inconclusive because the installed profile stayed in its English sub-mode; a short human pinyin/candidate check remains useful.

## 2026-07-20 EditTaskWindow IME (Chinese Input) Broken

- Symptom: double-click a card to edit text → `EditTaskWindow` opens → switch to a Chinese IME and type pinyin → the IME candidate window never appears; keys seem to do nothing. Inline add/edit TextBoxes on the summary board and split windows worked fine, so `TextInputService.EnableIme` was OK.
- Root cause: `EditTaskWindow.xaml` used `AllowsTransparency="True"` + `WindowStyle="None"` + `Background="Transparent"`, the well-known WPF combo where transparent borderless windows don't get an IMM32 context attached for newly-shown dialogs. The summary/split windows dodge this because their TextBoxes live inside an already-activated main window; `EditTaskWindow` is `new`-ed and `ShowDialog`-ed every time, so the IME never hooks up. `TextInputService` is a WPF-layer toggle and cannot fix the underlying HWND/IMM32 association.
- Fix in `EditTaskWindow.xaml` (one file, ~3 lines):
  - `AllowsTransparency="True"` → `"False"`
  - `Background="Transparent"` → `"#EA182230"` (lift the glass color from the outer Border to the Window)
  - outer `Border CornerRadius="14"` → `"0"` and the `Border.Effect` `DropShadowEffect` removed (rounded corners and outer drop shadow are not cleanly renderable under `AllowsTransparency=False`; kept the visual loss contained to this dialog only)
  - `WindowStyle="None"` kept so `DragMove()` still works.
- Other windows' IME was not broken, so `SettingsWindow.xaml` / `ColumnSelectWindow.xaml` / `GlassConfirmWindow.xaml` were intentionally left alone. If any of them ever shows the same symptom, the same `AllowsTransparency=False` swap is the right fix.
- Also corrected the launcher `Run-打开透明看板.ps1`: the `dist` exe name was hard-coded to `dist\透明看板.exe` (stale name; `AssemblyName=GlassKanbanOverlay` in csproj produces `GlassKanbanOverlay.exe`), so every launch silently fell through to `dotnet run` (debug source build). Switched the hardcoded path to `dist\GlassKanbanOverlay.exe`. The desktop shortcut `透明看板.lnk` already pointed at `dist\GlassKanbanOverlay.exe`, so no shortcut change was needed.

Validation:

- `dotnet build DesktopOverlayBoard.sln` passed with 0 warnings and 0 errors.
- Stopped the previously running `GlassKanbanOverlay.exe` that locked the old dist DLL, then `dotnet publish DesktopOverlayBoard.csproj -c Release -r win-x64 --self-contained false -o dist`. Published assembly is `dist\GlassKanbanOverlay.exe`.
- Manual GUI verification (double-click a card → EditTaskWindow → Chinese IME → pinyin → candidate window should appear and selected text should land in the TextBox) is required and is left to the maintainer on first run.

## 2026-05-22

- Added Kanban-compatible card archiving:
  - `ArchiveTask` moves a card to the board archive section instead of deleting it;
  - creates the `***` + `## Archive` section before `%% kanban:settings` when missing;
  - recognizes existing `## Archive` and `## 归档` sections that follow the archive separator;
  - honors `archive-with-date`, `archive-date-format`, `archive-date-separator`, `append-archive-date`, and `max-archive-size`.
- Added archive actions to card menus in summary and split windows.
- Reworked `README.md` for public GitHub/agent handoff and added `AGENTS.md`.
- Added `Data/config.sample.json` for public repo use without local machine paths.
- Added regression tests for archive creation, timestamp formatting, and archive size trimming.
- Unified the most visible native WPF UI surfaces with the transparent board style:
  - context menus now use a dark glass menu style;
  - delete and warning prompts use `GlassConfirmWindow`;
  - settings, edit task, and column select dialogs use borderless glass windows.
- Added a visible `×` button to the summary board toolbar; it uses the existing minimize-to-tray behavior.
- Kept task data safety unchanged: writes still go through `MarkdownKanbanService` column-hash and original-line checks.
- Public release note: comparable Obsidian task/Kanban tools exist inside Obsidian and as standalone task apps, but this project is currently positioned as a Windows desktop overlay for explicitly selected Markdown board columns across vaults.

Validation:

- `dotnet build DesktopOverlayBoard.sln` passed with 0 warnings and 0 errors.
- `dotnet run --project Tests\DesktopOverlayBoard.Tests.csproj` passed.
- `dotnet publish DesktopOverlayBoard.csproj -c Release -r win-x64 --self-contained false -o dist` completed after stopping the running app instance that locked `dist\透明看板.dll`.
- Manual screenshots captured:
  - `Log\verify-ui-polish-main.png`
  - `Log\verify-ui-polish-settings.png`
  - `Log\verify-ui-polish-menu.png`
  - `Log\verify-ui-polish-confirm.png`

## 2026-05-25 IME And Startup Restore Fix

- Added `Services\TextInputService.cs` to explicitly enable WPF IME support for task/edit/settings text boxes.
- Guarded Enter and lost-focus commits while an IME composition is active, so Chinese input candidate selection is not treated as "save now".
- Updated startup registration to use the published `dist\透明看板.exe` path and append `--startup`.
- Added startup-launch handling that restores previously open split windows, then re-applies their saved placement/layer after a short delay. This is intended to survive Windows Explorer/login timing when desktop-layer widgets start with Windows.
- Published the fixed self-use build back to `dist`.

Validation:

- `dotnet build .\DesktopOverlayBoard.sln` passed with 0 warnings and 0 errors.
- `dotnet run --project .\Tests\DesktopOverlayBoard.Tests.csproj` passed.
- `dotnet publish .\DesktopOverlayBoard.csproj -c Release -r win-x64 --self-contained false -o .\dist` passed after stopping the running `透明看板` process that locked the old DLL.
- Started the published executable and confirmed the Run key uses the expected `dist\...exe --startup` command.
- Captured `Log\verify-startup-ime-fix.png` after launch.

## 2026-07-04 Split Window Subtitle Not Updating After Re-selecting Column

- Symptom: in a split board window (`SingleBoardWindow`), after re-selecting a Markdown column via "配置看板" (Settings) and saving, the subtitle (`ColumnText`) did not update to the new column, while the task list refreshed fine.
- Root cause: `ColumnText`/`MainTitleText`/`TitleText` were only assigned once in `SingleBoardWindow_Loaded`. `MainWindow.ShowSettingsAsync` syncs config via `window.ApplyConfig(...)` + `window.ReloadAsync()`, but neither refreshed the header text, so it kept showing the `DefaultColumn` resolved at first load (config `widgetNote` is empty, so `GetWidgetNote()` falls back to `_board.DefaultColumn`).
- Fix in `SingleBoardWindow.xaml.cs`:
  - extracted `RefreshHeader()` that re-applies `TitleText`, `MainTitleText`, `ColumnText`;
  - called it from `SingleBoardWindow_Loaded` and at the top of `ReloadAsync`, so config-save, file-watch, and manual refresh paths all re-sync the header.
- No change to MainWindow / SettingsWindow / data-write logic; column-hash safety intact.

Validation:

- `dotnet build -c Debug` passed with 0 warnings and 0 errors.
- Republished self-use build to `dist` and restarted `透明看板.exe`.
 - Note: publish against the repo root (with `DesktopOverlayBoard.sln`) also pulls in the Tests project and writes `DesktopOverlayBoard.Tests.*` into `dist`; publish the csproj instead to keep `dist` clean:
   `dotnet publish DesktopOverlayBoard.csproj -c Release -r win-x64 --self-contained false -o dist`
- Removed stray `DesktopOverlayBoard.Tests.*` files that the solution-level publish had written into `dist`.
