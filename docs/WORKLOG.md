# Worklog

## 2026-08-30 Modal Focus And Summary Configuration Revision

- Deferred auto-launched New/Add flows until the Settings window completes its initial render, preventing a nested modal from competing with its owner during activation.
- The column/template chooser now explicitly activates and focuses its combo box, with Enter to accept and Escape to cancel.
- Renamed the summary refresh action to **Refresh boards** and restored **Configure boards** between refresh and split-to-desktop in both summary chrome layouts.

## 2026-08-30 English And Simplified Chinese Documentation And UI

- Reworked the public README around the user-facing mental model, current interaction labels, Markdown compatibility, safety boundary, pre-release status, and source quick start; added a complete Simplified Chinese counterpart with matching scope and limitations.
- Reduced the maintained UI languages to English and Simplified Chinese because the removed translations were incomplete and silently fell back to English. Legacy Traditional Chinese locale values now migrate to Simplified Chinese; other removed values fall back to automatic selection.
- Completed every Simplified Chinese localization key, localized the theme labels and remaining static tooltips, and aligned source-file and split-to-desktop wording with the README.
- Updated the portable candidate script to include both README languages. Markdown write behavior and safety checks are unchanged.

## 2026-08-30 Maintainer Toolbar And Board-Menu Revision

- Reordered the summary toolbar to **New board**, **Add existing**, **Refresh**, and **Split to desktop**, with the close-to-tray control retained after them and the visible Settings button removed.
- Routed the two new toolbar entries through the existing Settings-window creation and add-existing flows instead of duplicating file or validation logic.
- Replaced the per-board menu with **Split to desktop**, **Open source Markdown file**, **Configure window**, and **Remove board**; the split action now explicitly applies desktop mode, and the menu button has a localized accessible name.
- Made long board-title and widget-note fields wrap inside the available header width in summary cards and split widgets, while keeping the action-button columns fixed.
- Replaced the crowded horizontal missing-column recovery row with four full-width vertical actions in both summary cards and split widgets.
- Markdown write safety, removal confirmation, source-file preservation, and conflict checks are unchanged.

## 2026-08-30 GKO-PUBLIC-001 Public-Candidate Integration, Review And RC Evidence

- Added the first public-candidate interaction paths: two new-board templates (`TODO / DONE` and `TODO / DOING / DONE`), add-existing-board flow, remove-from-summary cleanup, and four missing-column recovery actions.
- Added service-level coverage for template parsing, refusing to overwrite an existing file, preserving Markdown while creating a missing column, refusing source-hash conflicts, configuration cleanup, working-area rectangle clamping, and the single-instance activation signal.
- Sol's review found that closing a removed split window could write its old layout back into the new config; the close-without-saving path now prevents that stale write.
- Centralized multi-screen reachability and placement recovery in `WindowPlacementService`; summary and split windows use the shared 48x48 usable-intersection rule, with automated coverage for valid multi-screen and 1px-sliver cases.
- Replaced ad hoc pending-refresh flags with `PendingRefreshGate`, so draft refresh deferral and post-submit/cancel consumption share one explicit state contract.
- Clarified the missing-column recovery actions and labels, including selecting another column, creating the missing heading after confirmation, opening the source, and removing the view.
- Added draft guards to the summary and split windows so an external source change is deferred while a card is being entered or edited; existing line and column conflict checks remain the write boundary.
- Added the native single-instance startup guard and checked Win32 placement-call failures.
- Hardened the portable-release script to refuse existing output targets and to reject local config/log content; added a Windows CI workflow that runs build and service-level tests only.

Validation:

- `dotnet build .\DesktopOverlayBoard.sln` passed with 0 warnings and 0 errors on the current working tree.
- `dotnet run --project .\Tests\DesktopOverlayBoard.Tests.csproj` passed: `DesktopOverlayBoard.Tests: all tests passed`.
- `scripts\New-PortableRelease.ps1` completed successfully. The portable directory and zip contain only `GlassKanbanOverlay.exe`, `LICENSE`, `NOTICE.md`, and `README.md`; no `Data`, `Log`, or PDB files are present. The zip SHA-256 is `A15C1195E7714AB4F99919E71DE2FD63A959666C80C5A4A2D42420BDA14F73F3`.
- Neutral-home UI automation verified: a second launch exited 0, the first instance restored from minimized state, and only one RC process remained; both new-board templates, add-existing, card add, draft preservation during external refresh, fail-closed conflicts, all four missing-column paths, and summary/Settings removal were exercised. Removal left source files unchanged and cleared window/open/layout state. A simulated `(50000, 50000)` placement returned both summary and split windows to a visible area.
- Neutral screenshots were generated under ignored `TestResults` output and are not commit candidates.
- Maintainer still needs to run the RC personally and verify real Chinese IME candidate selection, real multi-monitor topology changes, and the final privacy/identity/reachable-history gate. This entry does not claim public release, completion, or maintainer acceptance.

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
