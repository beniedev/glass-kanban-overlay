using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using System.Windows;

namespace DesktopOverlayBoard.Tests;

internal static class MissingColumnRecoveryTests
{
    public static async Task RunAsync(string root)
    {
        await TestReselectAsync(root);
        await TestCreateAsync(root);
        await TestReadErrorsAsync(root);
        await TestGuardAndRemovalAsync(root);
    }

    private static async Task TestReselectAsync(string root)
    {
        var fixture = new Fixture(root, "reselect");
        var original = File.ReadAllBytes(fixture.Board.FilePath);
        await fixture.RunAsync(MissingColumnRecoveryAction.Reselect);
        Require(fixture.Events.SequenceEqual(new[] { "select" }), "cancel must not save or refresh");
        Require(fixture.Dialogs.Columns.SequenceEqual(new[] { "TODO" }), "chooser must receive parsed columns");

        fixture.Events.Clear();
        var oldConfig = fixture.Config;
        var replacement = new BoardConfig { Id = fixture.Board.Id.ToUpperInvariant(), DefaultColumn = "Missing" };
        fixture.Dialogs.Selection = "TODO";
        fixture.Dialogs.OnSelect = () => fixture.Config = new AppConfig { Boards = new() { replacement } };
        await fixture.RunAsync(MissingColumnRecoveryAction.Reselect);
        Require(fixture.Events.SequenceEqual(new[] { "select", "save", "reload" }), "selection must save before refresh");
        Require(ReferenceEquals(fixture.Saved, fixture.Config), "selection must save the current config instance");
        Require(replacement.DefaultColumn == "TODO", "case-insensitive board identity must be retained");
        Require(oldConfig.Boards[0].DefaultColumn == "Missing", "replaced config must not be mutated");
        Require(File.ReadAllBytes(fixture.Board.FilePath).SequenceEqual(original), "reselect must not modify Markdown");

        fixture.Events.Clear();
        fixture.Dialogs.OnSelect = () => fixture.Config = new AppConfig();
        await fixture.RunAsync(MissingColumnRecoveryAction.Reselect);
        Require(fixture.Events.SequenceEqual(new[] { "select" }), "removed board must not be saved or recreated");

        var failure = new Fixture(root, "save-failure");
        failure.Dialogs.Selection = "TODO";
        failure.FailSave = true;
        try
        {
            await failure.RunAsync(MissingColumnRecoveryAction.Reselect);
            throw new InvalidOperationException("save failure was swallowed");
        }
        catch (IOException)
        {
            Require(failure.Events.SequenceEqual(new[] { "select", "save" }), "failed save must not refresh");
        }
    }

    private static async Task TestCreateAsync(string root)
    {
        var fixture = new Fixture(root, "create");
        var original = File.ReadAllBytes(fixture.Board.FilePath);
        await fixture.RunAsync(MissingColumnRecoveryAction.Create);
        Require(fixture.Events.SequenceEqual(new[] { "confirm" }), "cancelled creation must not refresh");
        Require(File.ReadAllBytes(fixture.Board.FilePath).SequenceEqual(original), "cancelled creation must not write");

        fixture.Events.Clear();
        fixture.Dialogs.Confirmed = true;
        await fixture.RunAsync(MissingColumnRecoveryAction.Create);
        Require(fixture.Events.SequenceEqual(new[] { "confirm", "reload" }), "successful creation must refresh without saving config");
        Require(fixture.Kanban.GetColumnTitles(fixture.Board.FilePath).SequenceEqual(new[] { "TODO", "Missing" }), "missing column must be created");
        Require(File.ReadAllText(fixture.Board.FilePath).Contains("- [ ] keep ^keep-id", StringComparison.Ordinal), "existing task must remain unchanged");

        var conflict = new Fixture(root, "conflict");
        conflict.Dialogs.Confirmed = true;
        conflict.Dialogs.OnConfirm = () => File.AppendAllText(conflict.Board.FilePath, "external edit\n");
        await conflict.RunAsync(MissingColumnRecoveryAction.Create);
        Require(conflict.Events.SequenceEqual(new[] { "confirm", "notice" }), "stale document hash must report failure without refresh");
        Require(conflict.Dialogs.Notices[0].Title == LocalizationService.Text("Dialog.WriteFailed"), "write failure notice must be retained");
        Require(!conflict.Kanban.GetColumnTitles(conflict.Board.FilePath).Contains("Missing"), "conflict must not create a column");
        Require(File.ReadAllText(conflict.Board.FilePath).EndsWith("external edit\n", StringComparison.Ordinal), "conflict must preserve external edit");
    }

    private static async Task TestReadErrorsAsync(string root)
    {
        var empty = new Fixture(root, "empty");
        File.WriteAllText(empty.Board.FilePath, "ordinary text\n");
        await empty.RunAsync(MissingColumnRecoveryAction.Reselect);
        Require(empty.Events.SequenceEqual(new[] { "notice" }), "no columns must not open a chooser");
        Require(empty.Dialogs.Notices[0].Title == LocalizationService.Text("Dialog.NoColumns"), "empty column notice must be retained");

        var locked = new Fixture(root, "locked");
        using var handle = new FileStream(locked.Board.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await locked.RunAsync(MissingColumnRecoveryAction.Reselect);
        Require(locked.Events.SequenceEqual(new[] { "notice" }), "read failure must not save or refresh");
        Require(locked.Dialogs.Notices[0].Title == LocalizationService.Text("Dialog.ReadFailed"), "read failure notice must be retained");
    }

    private static async Task TestGuardAndRemovalAsync(string root)
    {
        var fixture = new Fixture(root, "guard");
        var healthy = new BoardGroup
        {
            Board = fixture.Board, ColumnTitle = "TODO", ColumnRangeHash = "existing-column", SourceHash = fixture.Group.SourceHash,
        };
        await fixture.Recovery.RecoverAsync(healthy, null!, MissingColumnRecoveryAction.Create);
        Require(fixture.Events.Count == 0, "healthy groups must not enter recovery");
        var unknown = new BoardGroup { Board = fixture.Board, ColumnTitle = "Missing", ColumnRangeHash = "" };
        await fixture.Recovery.RecoverAsync(unknown, null!, MissingColumnRecoveryAction.Create);
        Require(fixture.Events.Count == 0, "missing source hash must not enter recovery");

        var original = File.ReadAllBytes(fixture.Board.FilePath);
        await fixture.RunAsync(MissingColumnRecoveryAction.Remove);
        Require(fixture.Events.SequenceEqual(new[] { "remove" }), "removal must delegate once to the existing window owner");
        Require(ReferenceEquals(fixture.Removed, fixture.Board), "removal must use the original board identity");
        Require(File.ReadAllBytes(fixture.Board.FilePath).SequenceEqual(original), "recovery must not delete the source document");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        public readonly MarkdownKanbanService Kanban = new();
        public readonly List<string> Events = new();
        public readonly FakeDialogs Dialogs;
        public readonly BoardConfig Board;
        public readonly BoardGroup Group;
        public readonly MissingColumnRecovery Recovery;
        public AppConfig Config;
        public AppConfig? Saved;
        public BoardConfig? Removed;
        public bool FailSave;

        public Fixture(string root, string name)
        {
            var file = Path.Combine(root, $"recovery-{name}.md");
            File.WriteAllText(file, "# Synthetic board\n\n## TODO\n\n- [ ] keep ^keep-id\n");
            Board = new BoardConfig { Id = "synthetic-board", FilePath = file, DefaultColumn = "Missing" };
            Config = new AppConfig { Boards = new() { Board } };
            Group = new BoardGroup { Board = Board, ColumnTitle = "Missing", ColumnRangeHash = "", SourceHash = Kanban.Parse(file).FullHash };
            Dialogs = new FakeDialogs(Events);
            Recovery = new MissingColumnRecovery(Kanban, () => Config,
                config =>
                {
                    Events.Add("save");
                    if (FailSave) throw new IOException("synthetic save failure");
                    Saved = config;
                },
                () => { Events.Add("reload"); return Task.CompletedTask; },
                (board, _) => { Events.Add("remove"); Removed = board; return Task.CompletedTask; }, Dialogs);
        }

        // Fake dialogs never create or access a WPF owner; all filesystem data
        // lives under the existing test runner's temporary root.
        public Task RunAsync(MissingColumnRecoveryAction action) => Recovery.RecoverAsync(Group, null!, action);
    }

    private sealed class FakeDialogs(List<string> events) : IMissingColumnRecoveryDialogs
    {
        public string? Selection;
        public bool Confirmed;
        public Action? OnSelect;
        public Action? OnConfirm;
        public IReadOnlyList<string> Columns = Array.Empty<string>();
        public readonly List<(string Title, string Message)> Notices = new();

        public string? SelectColumn(Window owner, IReadOnlyList<string> columns)
        {
            events.Add("select");
            Columns = columns;
            OnSelect?.Invoke();
            return Selection;
        }

        public bool ConfirmCreate(Window owner, string columnTitle)
        {
            events.Add("confirm");
            OnConfirm?.Invoke();
            return Confirmed;
        }

        public void ShowNotice(Window owner, string title, string message)
        {
            events.Add("notice");
            Notices.Add((title, message));
        }
    }
}
