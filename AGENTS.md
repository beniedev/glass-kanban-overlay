# Agent Notes

This repository maintains a Windows WPF/.NET 8 desktop overlay for selected Obsidian Kanban Markdown columns.

## Guardrails

- Do not turn the app into a full-vault scanner.
- Do not add AI, cloud sync, telemetry, or background network behavior.
- Only read/write Markdown files that the user explicitly added in settings.
- Keep archive/backup path blocking intact.
- Do not weaken column-hash or original-line conflict checks.
- Preserve frontmatter, Kanban settings blocks, ordinary paragraphs, and block IDs.
- Keep UI changes consistent with the transparent glass widget style.

## Verification

Run these after non-trivial changes:

```powershell
dotnet build .\DesktopOverlayBoard.sln
dotnet run --project .\Tests\DesktopOverlayBoard.Tests.csproj
```

For UI changes, launch the app and capture a screenshot before calling the task complete.

Keep `Data\config.json`, `Log\`, `bin\`, `obj\`, and `dist\` out of commits; they contain local configuration or generated output.

## Documentation

Update `README.md` for user-facing or agent-facing behavior changes. Update `docs/WORKLOG.md` for work history, validation, and remaining risk.
