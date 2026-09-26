using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;

namespace DesktopOverlayBoard.Application;

public enum BoardSetupStatus { Cancelled, Added, AlreadyAdded, Blocked, ReadFailed, NoColumns, WriteFailed }
public sealed record BoardSetupResult(BoardSetupStatus Status, BoardConfig? Board = null,
    bool FileCreated = false, string? Error = null);

public sealed class BoardSetupWorkflow(MarkdownKanbanService kanban)
{
    public BoardSetupResult PrepareNew(AppConfig draft,
        Func<KanbanBoardTemplate?> chooseTemplate, Func<string?> choosePath)
    {
        var template = chooseTemplate();
        if (template is null) return new(BoardSetupStatus.Cancelled);
        var path = choosePath();
        if (path is null) return new(BoardSetupStatus.Cancelled);
        if (ContainsPath(draft, path)) return new(BoardSetupStatus.AlreadyAdded);

        var result = kanban.CreateBoardFile(path, template.Value);
        if (!result.Success) return new(BoardSetupStatus.WriteFailed, Error: result.Error);
        return new(BoardSetupStatus.Added, CreateBoard(path,
            MarkdownKanbanService.GetTemplateColumns(template.Value)[0]), FileCreated: true);
    }

    public BoardSetupResult PrepareExisting(AppConfig draft,
        Func<string?> choosePath, Func<IReadOnlyList<string>, string?> chooseColumn)
    {
        var path = choosePath();
        if (path is null) return new(BoardSetupStatus.Cancelled);
        if (MarkdownKanbanService.IsBlockedPath(path)) return new(BoardSetupStatus.Blocked);
        if (ContainsPath(draft, path)) return new(BoardSetupStatus.AlreadyAdded);

        IReadOnlyList<string> columns;
        try { columns = kanban.GetColumnTitles(path); }
        catch (Exception error) { return new(BoardSetupStatus.ReadFailed, Error: error.Message); }
        if (columns.Count == 0) return new(BoardSetupStatus.NoColumns);
        var column = chooseColumn(columns);
        return column is null ? new(BoardSetupStatus.Cancelled)
            : new(BoardSetupStatus.Added, CreateBoard(path, column));
    }

    private static bool ContainsPath(AppConfig config, string path) => config.Boards.Any(board =>
        string.Equals(board.FilePath, path, StringComparison.OrdinalIgnoreCase));

    private static BoardConfig CreateBoard(string path, string column) => new()
    {
        DisplayName = Path.GetFileNameWithoutExtension(path),
        VaultName = GuessVaultName(path),
        FilePath = path,
        DefaultColumn = column,
        Enabled = true,
    };

    private static string GuessVaultName(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            var parent = Directory.GetParent(directory);
            if (parent is not null && !string.IsNullOrWhiteSpace(parent.Name)) return parent.Name;
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}
