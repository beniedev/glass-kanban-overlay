# Glass Kanban Overlay

Transparent Windows desktop widgets for selected Obsidian Kanban / Markdown board columns.

![Glass Kanban Overlay preview](docs/assets/glass-kanban-overlay-preview.png)

Glass Kanban Overlay is a local-first WPF/.NET 8 desktop app. You explicitly choose Markdown board files and columns, then keep those columns visible as glass widgets without opening Obsidian. It is not an Obsidian plugin, does not use AI or cloud sync, and does not scan your whole vault.

## Status

The source tree is under maintainer acceptance. The current build and service-level regression suite cover the local interaction needed for a first trial:

- **New board** creates a new `.md` file from either a `TODO / DONE` or `TODO / DOING / DONE` template. Existing files are never overwritten.
- **Add existing** keeps the original flow for selecting an existing Markdown board and one of its `##` columns.
- **Remove from summary** removes the app view, closes its split widget, and clears saved open-window state without deleting or rewriting the source Markdown file.
- If a selected column is missing, the error state offers four recovery paths: select another existing column, create the missing `##` heading after confirmation, open the source file, or remove the view.
- While a card is being entered or edited, an external file change does not destroy the draft. Refresh waits until submit/cancel; a changed target remains a visible, fail-closed conflict.
- A named Windows mutex keeps one current app instance. A second launch signals the existing instance to show/restore and activate its window, then exits.

Summary and split windows now route saved rectangles through a shared working-area clamp. A rectangle with at least `48x48` of usable intersection stays where it is; a fully or clearly off-screen rectangle is moved to the nearest working area. The 48x48 reachability rule has automated coverage, and failed Win32 placement calls are written to the local log. A real display-topology change still needs manual acceptance.

No binary release is published yet. The source can produce a self-contained win-x64 portable candidate with the packaging script below, but the maintainer still needs to build and inspect that package. The GitHub workflow checks build and service-level tests only; it does not verify desktop UI behavior.

## What it shows

- A summary window with selected columns side by side.
- Each selected column as an independent desktop widget.
- Desktop, topmost, and normal window modes.
- A tray icon for showing the summary, opening split widgets, settings, and exit.
- Glass styling for widgets, settings, menus, edit prompts, and confirmations.
- Inline add, checkbox toggle, edit, move-to-top, delete, archive, and same-column reorder actions.
- The source Markdown file in the system default editor.

## Obsidian Kanban compatibility

The parser targets the Markdown subset used by the Obsidian Kanban plugin:

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

The app recognizes these archive settings when present:

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
```
{"kanban-plugin":"board"}
```
%%
````

The app also recognizes an existing `## 归档` heading when it follows the `***` archive separator. For the full supported subset, inspect the parser and the public-safe files in [`examples/`](examples/).

## Safety contract

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

## Out of scope

- AI integration, cloud sync, telemetry, or background network behavior.
- Automatic full-vault scanning.
- Cross-column drag/drop or bulk multi-select.
- Automatic archive sweeps.
- Guaranteed support for every advanced Obsidian Kanban card shape, such as complex nested card bodies.
- An installer, updater, Microsoft Store package, signing certificate, or multi-architecture release matrix.

## Quick start from source

Requirements:

- Windows
- .NET 8 SDK

Build and run the service-level regression suite:

```powershell
dotnet build .\DesktopOverlayBoard.sln
dotnet run --project .\Tests\DesktopOverlayBoard.Tests.csproj
```

Prepare a fresh portable candidate (the script refuses to overwrite an existing candidate directory or zip):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\New-PortableRelease.ps1
```

The script builds the app and tests, publishes a self-contained `win-x64` directory, copies `LICENSE`, `NOTICE.md`, and `README.md`, and creates a zip. It does not package `Data\config.json` or `Log\`. It does not replace the flat `dist\GlassKanbanOverlay.exe` dogfood build.

To launch a local build, use:

```powershell
.\run-glass-kanban-overlay.ps1
```

The launcher prefers the portable candidate, then the flat published executable, and otherwise runs the project from source.

## Configuration

Local config lives at:

```text
Data\config.json
```

That file is intentionally ignored because it contains machine-specific paths. Public examples should use [`Data/config.sample.json`](Data/config.sample.json); tests use temporary board files and do not depend on a maintainer's vault.

## Repository layout

```text
glass-kanban-overlay\
  App.xaml                         shared glass styles
  MainWindow.xaml(.cs)             summary board, tray, split-window orchestration
  SingleBoardWindow.xaml(.cs)      one board column as a desktop widget
  SettingsWindow.xaml(.cs)         new/existing board and startup settings
  Services\MarkdownKanbanService.cs Markdown parse/write safety boundary
  Services\ConfigService.cs        config load/save and migration
  Services\SingleInstanceService.cs one-instance mutex and activation signal
  Services\WindowPlacementService.cs placement modes and rectangle helper
  Models\*.cs                      config and Kanban data models
  Tests\Program.cs                 lightweight service-level regression tests
  Data\config.sample.json          public sample config
  docs\assets\                    public-safe preview image
  scripts\New-PortableRelease.ps1 portable candidate builder
```

## Agent handoff

If you are an AI agent working on this repo, read [`AGENTS.md`](AGENTS.md) before editing:

- Do not broaden the app into a vault scanner.
- Do not add cloud sync or AI behavior.
- Do not silently swallow write failures.
- Do not weaken conflict checks to make tests pass.
- Keep changes small and verify with `dotnet build` plus `dotnet run --project Tests\DesktopOverlayBoard.Tests.csproj`.
- UI changes need a manual Windows launch and screenshot check because this is a visual desktop widget.
- After non-trivial changes, update this README or [`docs/WORKLOG.md`](docs/WORKLOG.md) with changed files, reason, validation, and remaining risk.

## Release status

The source build and service-level tests are green. Maintainer acceptance and manual Windows checks are still in progress, including neutral example-board interaction, IME/external-edit behavior, display-topology recovery, and portable-package inspection. No binary release is published yet; source review and binary distribution are separate decisions.

## Related projects

- [Obsidian Kanban plugin](https://github.com/obsidian-community/obsidian-kanban): the Markdown-backed Kanban format this app aims to interoperate with.
- [Task Board](https://www.obsidianstats.com/plugins/task-board): an Obsidian plugin that scans tasks across a vault.
- [CardBoard](https://community.obsidian.md/plugins/card-board): an Obsidian plugin for showing Markdown tasks on Kanban-style boards.
- [TaskForge](https://taskforge.md/): a standalone Obsidian task app with mobile apps and a Windows app planned.

Glass Kanban Overlay is different because it is a Windows desktop overlay for explicitly selected board files and columns across vaults.

## Support

This is a local-first project. Please include your Windows/.NET version, the neutral example board used, and the exact operation that failed when reporting a problem. Do not attach `Data\config.json`, `Log\`, private vault files, or credentials.
