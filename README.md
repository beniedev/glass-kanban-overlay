# Glass Kanban Overlay

Transparent Windows desktop widgets for selected Obsidian Kanban / Markdown board columns.

Glass Kanban Overlay is a local-first WPF/.NET 8 desktop app. It is not an Obsidian plugin, does not use AI, does not sync to a cloud service, and does not scan your whole vault. You explicitly add Markdown board files, choose one column from each file, and keep those columns on your desktop as glass widgets.

The project is designed for people who keep several Obsidian vaults, or several Kanban Markdown files inside one vault, and want a small always-visible task surface without opening Obsidian.

## Current Status

This repo is being prepared for public release. The core local workflow works, but first-run setup and packaging still need polish before a general binary release.

## Features

- Add explicit `.md` board files from one or more vaults.
- Pick a default `##` column per board file.
- Show a summary window with multiple columns side by side.
- Pop each board column out as an independent desktop widget.
- Restore previously open split widgets on launch, including Windows auto-start.
- Window modes:
  - desktop widget mode: stays behind normal apps;
  - topmost mode: stays above other windows;
  - normal app mode.
- Tray icon with show, split, settings, and exit actions.
- Glass UI for widgets, settings, menus, edit prompts, and confirmation prompts.
- Inline add card at the bottom of the target column.
- Toggle checkboxes, edit cards inline, move cards to top, delete cards, archive cards, and reorder cards within the same column.
- Open the source Markdown file in the system default editor.

## Obsidian Kanban Compatibility

This app treats the Obsidian Kanban plugin format as the baseline compatibility target:

- board files are Markdown files with `##` headings as columns;
- cards are Markdown checkbox tasks such as `- [ ] Write README`;
- Kanban settings blocks are preserved:

````markdown
%% kanban:settings
```
{"kanban-plugin":"board"}
```
%%
````

The app currently supports the following Kanban archive settings:

- `archive-with-date`
- `archive-date-format`
- `archive-date-separator`
- `append-archive-date`
- `max-archive-size`

Archive is not delete. Delete removes the card line. Archive moves the card out of the active column and into the board's Markdown archive section near the bottom of the file:

````markdown
***

## Archive

- [ ] Archived card

%% kanban:settings
````

The Obsidian Kanban plugin documentation says a board archive is viewed by opening the board as Markdown; this app follows that model. For Chinese-localized boards, an existing `## 归档` archive heading is also recognized when it follows the `***` archive separator.

## Safety Contract

The writer is intentionally conservative:

- Only files explicitly added in settings are read or written.
- Archive-like paths are refused by default if they contain `归档`, `Archive`, `archive`, `backup`, `backups`, `备份`, or `_任务备份`.
- Every write re-reads the source file first.
- Writes are refused if the target column hash changed since the widget loaded it.
- Task edits also check that the original task line is still exactly where expected.
- Writes to one board are serialized across app instances, re-read while write-locked, and committed with an atomic same-directory replacement.
- Task text containing line breaks is refused so one card cannot inject extra Markdown lines.
- Frontmatter, Kanban settings, ordinary paragraphs, and block IDs are preserved.
- Adding a card inserts only one task line.
- Editing, toggling, deleting, archiving, and same-column reordering patch only the target line or target column/archive area.

## Not Supported Yet

- No AI integration.
- No cloud sync.
- No automatic full-vault scan.
- No cross-column drag/drop.
- No bulk multi-select.
- No automatic archive sweep.
- No guaranteed support yet for every advanced Obsidian Kanban card shape, such as complex nested card bodies.

## Repository Layout

```text
glass-kanban-overlay\
  App.xaml                         shared glass styles
  MainWindow.xaml(.cs)             summary board, tray, split-window orchestration
  SingleBoardWindow.xaml(.cs)      one board column as a desktop widget
  SettingsWindow.xaml(.cs)         explicit board file picker and startup options
  Services\MarkdownKanbanService.cs Markdown parse/write safety boundary
  Services\ConfigService.cs        config load/save and migration
  Models\*.cs                      config and Kanban data models
  Tests\Program.cs                 lightweight service-level regression tests
  Data\config.sample.json          public sample config
  Data\config.json                 local machine config, ignored by git
  Log\                             screenshots/logs, ignored by git
  dist\                            local publish output, ignored by git
```

## Build And Run

Requirements:

- Windows
- .NET 8 SDK

Commands:

```powershell
dotnet build .\DesktopOverlayBoard.sln
dotnet run --project .\Tests\DesktopOverlayBoard.Tests.csproj
dotnet publish .\DesktopOverlayBoard.csproj -c Release -r win-x64 --self-contained false -o .\dist
```

Run the local published app:

```powershell
.\run-glass-kanban-overlay.ps1
```

## Configuration

Local config lives at:

```text
Data\config.json
```

That file is intentionally ignored because it contains machine-specific paths. Public examples should use `Data\config.sample.json`; tests use temporary board files and do not depend on the maintainer's vaults.

## Agent Handoff

If you are an AI agent working on this repo, read this before editing:

- Do not broaden the app into a vault scanner.
- Do not add cloud sync or AI behavior.
- Do not silently swallow write failures.
- Do not weaken conflict checks to make tests pass.
- Keep changes small and verify with `dotnet build` plus `dotnet run --project Tests\DesktopOverlayBoard.Tests.csproj`.
- UI changes should be manually launched and screenshot-checked because this is a visual desktop widget.
- After non-trivial changes, update this README or `docs/WORKLOG.md` with changed files, reason, validation, and remaining risk.

## Public Release Checklist

- Add a first-run setup flow or wizard.
- Add clean screenshots to a tracked docs/assets folder.
- Verify that release packages include `LICENSE` and `NOTICE.md`.
- Replace machine-specific defaults in code/config samples.
- Add release packaging notes.
- Document the exact supported subset of Obsidian Kanban syntax.
- Add tests for any new Markdown write behavior before shipping it.

## Related Projects

- [Obsidian Kanban plugin](https://github.com/obsidian-community/obsidian-kanban): the Markdown-backed Kanban format this app aims to interoperate with.
- [Task Board](https://www.obsidianstats.com/plugins/task-board): an Obsidian plugin that scans tasks across a vault.
- [CardBoard](https://community.obsidian.md/plugins/card-board): an Obsidian plugin for showing Markdown tasks on Kanban-style boards.
- [TaskForge](https://taskforge.md/): a standalone Obsidian task app with mobile apps and a Windows app planned.

Glass Kanban Overlay is different because it is a Windows desktop overlay for explicitly selected board files/columns across vaults.

## Support

No "buy me a coffee" needed. If this saves you a few minutes, buy Codex and GLM tokens instead. The machines are thirsty.
