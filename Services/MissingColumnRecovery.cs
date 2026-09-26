using System.Windows;
using DesktopOverlayBoard.Models;

namespace DesktopOverlayBoard.Services;

public enum MissingColumnRecoveryAction
{
    Reselect,
    Create,
    OpenSource,
    Remove,
}

public interface IMissingColumnRecoveryDialogs
{
    string? SelectColumn(Window owner, IReadOnlyList<string> columns);
    bool ConfirmCreate(Window owner, string columnTitle);
    void ShowNotice(Window owner, string title, string message);
}

/// <summary>
/// Shared missing-column workflow. The application retains config, window and
/// refresh ownership; MarkdownKanbanService retains all file-write guards.
/// </summary>
public sealed class MissingColumnRecovery
{
    private readonly MarkdownKanbanService _kanban;
    private readonly Func<AppConfig> _getConfig;
    private readonly Action<AppConfig> _saveConfig;
    private readonly Func<Task> _reload;
    private readonly Func<BoardConfig, Window, Task> _removeBoard;
    private readonly IMissingColumnRecoveryDialogs _dialogs;

    public MissingColumnRecovery(
        MarkdownKanbanService kanban,
        Func<AppConfig> getConfig,
        Action<AppConfig> saveConfig,
        Func<Task> reload,
        Func<BoardConfig, Window, Task> removeBoard,
        IMissingColumnRecoveryDialogs? dialogs = null)
    {
        _kanban = kanban;
        _getConfig = getConfig;
        _saveConfig = saveConfig;
        _reload = reload;
        _removeBoard = removeBoard;
        _dialogs = dialogs ?? new RecoveryDialogs();
    }

    private static string T(string key, params object?[] args) => LocalizationService.Text(key, args);

    public static bool IsMissingColumnGroup(BoardGroup group)
    {
        return File.Exists(group.Board.FilePath) &&
               !string.IsNullOrWhiteSpace(group.SourceHash) &&
               string.IsNullOrWhiteSpace(group.ColumnRangeHash);
    }

    public async Task RecoverAsync(BoardGroup group, Window owner, MissingColumnRecoveryAction action)
    {
        if (!IsMissingColumnGroup(group))
        {
            return;
        }

        switch (action)
        {
            case MissingColumnRecoveryAction.Reselect:
                IReadOnlyList<string> columns;
                try
                {
                    columns = _kanban.GetColumnTitles(group.Board.FilePath);
                }
                catch (Exception ex)
                {
                    _dialogs.ShowNotice(owner, T("Dialog.ReadFailed"), T("Message.ReadFailed", ex.Message));
                    return;
                }

                if (columns.Count == 0)
                {
                    _dialogs.ShowNotice(owner, T("Dialog.NoColumns"), T("Message.NoColumns"));
                    return;
                }

                var selectedColumn = _dialogs.SelectColumn(owner, columns);
                if (selectedColumn is null)
                {
                    return;
                }

                // Settings can replace the config while a modal dialog is open.
                var config = _getConfig();
                var board = config.Boards.FirstOrDefault(x =>
                    string.Equals(x.Id, group.Board.Id, StringComparison.OrdinalIgnoreCase));
                if (board is null)
                {
                    return;
                }

                board.DefaultColumn = selectedColumn;
                _saveConfig(config);
                await _reload();
                return;

            case MissingColumnRecoveryAction.Create:
                if (!_dialogs.ConfirmCreate(owner, group.ColumnTitle))
                {
                    return;
                }

                var result = _kanban.CreateMissingColumn(
                    group.Board.FilePath, group.ColumnTitle, group.SourceHash);
                if (!result.Success)
                {
                    _dialogs.ShowNotice(owner, T("Dialog.WriteFailed"), result.Error ?? T("Dialog.WriteFailed"));
                    return;
                }

                await _reload();
                return;

            case MissingColumnRecoveryAction.OpenSource:
                _kanban.OpenSource(group.Board.FilePath);
                return;

            case MissingColumnRecoveryAction.Remove:
                await _removeBoard(group.Board, owner);
                return;
        }
    }

    private sealed class RecoveryDialogs : IMissingColumnRecoveryDialogs
    {
        public string? SelectColumn(Window owner, IReadOnlyList<string> columns)
        {
            var select = new ColumnSelectWindow(columns) { Owner = owner };
            return select.ShowDialog() == true ? select.SelectedColumn : null;
        }

        public bool ConfirmCreate(Window owner, string columnTitle) => GlassConfirmWindow.Show(
            owner,
            T("Dialog.CreateMissingColumn"),
            T("Message.CreateMissingColumnPrompt", columnTitle),
            T("Action.CreateMissingColumn"),
            T("Action.Cancel"));

        public void ShowNotice(Window owner, string title, string message) =>
            GlassConfirmWindow.ShowNotice(owner, title, message);
    }
}
