# Glass Kanban Overlay

English | [简体中文](README.zh-CN.md)

Keep selected Obsidian Kanban columns visible as transparent Windows desktop widgets.

Glass Kanban Overlay is a local-first WPF/.NET 8 app for Windows. You choose the Markdown board files and `##` columns you want to see; the app shows only those columns in a summary window or as separate desktop widgets.

It is not an Obsidian plugin. It does not scan the whole vault, use AI, sync to a cloud service, or send telemetry.

## Usage example

![Glass Kanban Overlay summary window and desktop widgets](docs/assets/glass-kanban-overlay-preview.png)

The summary window keeps selected columns together. **Split to desktop** opens the same boards as independent widgets. This screenshot uses the public-safe Markdown boards in [`examples/`](examples/).

## Project status

**Pre-release — awaiting maintainer acceptance.**

The current source builds cleanly and its service-level regression suite passes. Neutral Windows UI checks cover board creation, adding an existing board, card actions, missing-column recovery, external-edit conflicts, single-instance activation, window recovery, and the current toolbar/menu layout.

Manual acceptance is still required for Microsoft Pinyin candidate-selection Enter behavior and a real multi-display topology. No binary release has been published. The GitHub workflow verifies build and service-level tests; it does not verify desktop UI behavior.

## What you can do

- Create a new Markdown board from a `TODO / DONE` or `TODO / DOING / DONE` template.
- Add an existing Markdown board and choose one of its `##` columns.
- View selected columns together in the summary window.
- Split selected columns into independent desktop widgets.
- Add, edit, complete, move to top, reorder, archive, or delete cards.
- Open the source Markdown file in the system default editor.
- Choose desktop, always-on-top, or normal window mode.
- Restore off-screen windows to the nearest usable display area.
- Keep drafts intact when an external file refresh arrives during editing.

Long board titles and widget notes wrap within the available header width. Action buttons remain in their own fixed column.

## Main actions

The summary toolbar uses these labels and this order:

1. **New board**
2. **Add existing**
3. **Refresh boards**
4. **Configure boards**
5. **Split to desktop**

Each board menu uses:

1. **Split to desktop**
2. **Open source Markdown file**
3. **Configure window**
4. **Remove board**

Removing a board from the app closes its split widget and clears its saved window state. It does not delete or rewrite the source Markdown file.

## Obsidian Kanban compatibility

The parser targets the Markdown subset used by the [Obsidian Kanban plugin](https://github.com/obsidian-community/obsidian-kanban):

- `##` headings are board columns.
- Checkbox tasks such as `- [ ] Write README` are cards.
- Frontmatter, ordinary paragraphs, block IDs, and Kanban settings blocks are preserved.
- Public-safe examples are available in [`examples/`](examples/).

A minimal supported settings block looks like this:

````markdown
%% kanban:settings
```
{"kanban-plugin":"board"}
```
%%
````

### Archive behavior

Archive is different from delete. Delete removes the card line. Archive moves the card out of the active column and into the Markdown archive section near the end of the file.

The app recognizes the standard `## Archive` heading. It also recognizes the literal Simplified Chinese heading `## 归档` when that heading follows the `***` archive separator.

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

These Obsidian Kanban archive settings are preserved when present:

- `archive-with-date`
- `archive-date-format`
- `archive-date-separator`
- `append-archive-date`
- `max-archive-size`

## Safety boundary

Markdown writes are deliberately conservative:

- Only board files explicitly added in the app are read or written.
- Archive- or backup-like paths are blocked by default.
- Every write re-reads the source file first.
- A write is refused if the target column changed after the widget loaded it.
- Card edits also verify that the original task line is still where expected.
- Writes to one board are serialized across app instances and committed with an atomic same-directory replacement.
- Card text must remain on one line and cannot inject extra Markdown lines.
- Adding a card inserts one task line; other actions patch only the intended line, column, or archive area.

Missing columns are handled explicitly. You can choose another column, create the missing heading after confirmation, open the source file, or remove the board from the app.

## Interface languages

The maintained interface languages are:

- English
- 简体中文

**Auto / 跟随 Windows** uses Simplified Chinese for Chinese Windows locales and English for other locales. Legacy Traditional Chinese locale codes migrate to Simplified Chinese. Removed language values fall back to automatic selection instead of breaking configuration loading.

The README is maintained in the same two languages: this English file and [README.zh-CN.md](README.zh-CN.md).

## Quick start from source

Requirements:

- Windows
- .NET 8 SDK

Build and run the regression suite:

```powershell
dotnet build .\DesktopOverlayBoard.sln
dotnet run --project .\Tests\DesktopOverlayBoard.Tests.csproj
```

Launch from source or an existing local build:

```powershell
.\run-glass-kanban-overlay.ps1
```

Create a fresh self-contained win-x64 portable candidate:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\New-PortableRelease.ps1
```

The packaging script refuses to overwrite an existing candidate. It builds and tests the app, publishes a self-contained executable, includes the license, notice, and both README languages, then creates a zip. It never packages local configuration or logs and does not replace the flat local dogfood executable.

## Local configuration

Runtime configuration is stored in:

```text
Data\config.json
```

This file is ignored because it contains machine-specific paths. Use [`Data/config.sample.json`](Data/config.sample.json) for public examples. Tests create temporary neutral board files and do not depend on a maintainer's vault.

## Limits

Glass Kanban Overlay does not currently provide:

- automatic whole-vault scanning;
- cross-column drag-and-drop or bulk selection;
- automatic archive sweeps;
- guaranteed support for every complex nested Obsidian Kanban card shape;
- an installer, automatic updater, Microsoft Store package, code signing, or a multi-architecture release matrix.

## Repository map

```text
glass-kanban-overlay\
  App.xaml                          shared glass styles
  MainWindow.xaml(.cs)              summary window, tray, split-widget orchestration
  SingleBoardWindow.xaml(.cs)       one board column as a desktop widget
  SettingsWindow.xaml(.cs)          board setup, language, and startup settings
  Services\MarkdownKanbanService.cs Markdown parsing and write-safety boundary
  Services\LocalizationService.cs   English and Simplified Chinese interface text
  Services\ConfigService.cs         configuration loading, saving, and migration
  Services\SingleInstanceService.cs one-instance mutex and activation signal
  Services\WindowPlacementService.cs window modes and display-area recovery
  Models\*.cs                       configuration and Kanban data models
  Tests\Program.cs                  service-level regression tests
  Data\config.sample.json           public sample configuration
  docs\assets\                     public-safe preview image
  scripts\New-PortableRelease.ps1  portable candidate builder
```

## Development notes

Read [AGENTS.md](AGENTS.md) before editing this repository. In particular:

- Do not broaden the app into a vault scanner.
- Do not add AI, cloud sync, telemetry, or background network behavior.
- Do not weaken conflict checks or silently swallow write failures.
- Verify code changes with the solution build and regression suite.
- Verify UI changes with a real Windows launch and screenshot.

## Reporting a problem

Include the Windows/.NET version, a neutral example board, and the exact action that failed. Do not attach `Data/config.json`, logs, private vault files, or credentials.
