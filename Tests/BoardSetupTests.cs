using DesktopOverlayBoard.Application;
using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class BoardSetupTests
{
    public static void Run(string root)
    {
        TestNewCancellation(root);
        TestExistingCancellation(root);
        TestNewBoard(root);
        TestDuplicateAndBlocked(root);
        TestReadAndNoColumns(root);
        TestCreateFailure(root);
        TestExistingSelection(root);
    }

    private static void TestNewCancellation(string root)
    {
        var setup = new BoardSetupWorkflow(new MarkdownKanbanService());
        var draft = new AppConfig();
        var calls = 0;
        var result = setup.PrepareNew(draft, () => null, () => { calls++; return Path.Combine(root, "cancel.md"); });
        Assert(result.Status == BoardSetupStatus.Cancelled && calls == 0, "cancelled template must not open path chooser");
        result = setup.PrepareNew(draft, () => KanbanBoardTemplate.TodoDone, () => null);
        Assert(result.Status == BoardSetupStatus.Cancelled && draft.Boards.Count == 0, "cancelled path must not add or create a board");
        Assert(!File.Exists(Path.Combine(root, "cancel.md")), "cancelled setup must not write Markdown");
    }

    private static void TestExistingCancellation(string root)
    {
        var setup = new BoardSetupWorkflow(new MarkdownKanbanService());
        var draft = new AppConfig();
        var calls = 0;
        var result = setup.PrepareExisting(draft, () => null, _ => { calls++; return "TODO"; });
        Assert(result.Status == BoardSetupStatus.Cancelled && calls == 0, "cancelled path must not select a column");
        var path = Path.Combine(root, "cancel-existing.md");
        File.WriteAllText(path, "## TODO\n\n- [ ] keep\n");
        var original = File.ReadAllBytes(path);
        result = setup.PrepareExisting(draft, () => path, _ => null);
        Assert(result.Status == BoardSetupStatus.Cancelled && draft.Boards.Count == 0, "cancelled column must not add a view");
        Assert(File.ReadAllBytes(path).SequenceEqual(original), "cancelled column must preserve the source");
    }

    private static void TestNewBoard(string root)
    {
        var kanban = new MarkdownKanbanService();
        var setup = new BoardSetupWorkflow(kanban);
        foreach (var template in new[] { KanbanBoardTemplate.TodoDone, KanbanBoardTemplate.TodoDoingDone })
        {
            var draft = new AppConfig();
            var path = Path.Combine(root, template + ".md");
            var result = setup.PrepareNew(draft, () => template, () => path);
            Assert(result.Status == BoardSetupStatus.Added && result.FileCreated, "new board must report creation");
            Assert(draft.Boards.Count == 0, "setup prepares a view for the caller's draft; it must not own active config");
            Assert(result.Board is { Enabled: true, DefaultColumn: "TODO" } && result.Board.FilePath == path,
                "new view must preserve path/default column/enabled state");
            Assert(kanban.GetColumnTitles(path).SequenceEqual(MarkdownKanbanService.GetTemplateColumns(template)), "selected template must be written");
        }
    }

    private static void TestDuplicateAndBlocked(string root)
    {
        var setup = new BoardSetupWorkflow(new MarkdownKanbanService());
        var path = Path.Combine(root, "duplicate.md");
        File.WriteAllText(path, "## TODO\n");
        var original = File.ReadAllBytes(path);
        var draft = new AppConfig { Boards = new() { new BoardConfig { FilePath = path.ToUpperInvariant() } } };
        var calls = 0;
        Assert(setup.PrepareNew(draft, () => KanbanBoardTemplate.TodoDone, () => path).Status == BoardSetupStatus.AlreadyAdded,
            "duplicate new path must be rejected case-insensitively before creating a file");
        Assert(setup.PrepareExisting(draft, () => path, _ => { calls++; return "TODO"; }).Status == BoardSetupStatus.AlreadyAdded && calls == 0,
            "duplicate existing path must not read/select columns");
        var blocked = Path.Combine(root, "archive", "blocked.md");
        Assert(setup.PrepareExisting(new AppConfig(), () => blocked, _ => { calls++; return "TODO"; }).Status == BoardSetupStatus.Blocked && calls == 0,
            "blocked path must be rejected before reading or selecting");
        Assert(File.ReadAllBytes(path).SequenceEqual(original), "duplicate checks must not overwrite Markdown");
    }

    private static void TestReadAndNoColumns(string root)
    {
        var setup = new BoardSetupWorkflow(new MarkdownKanbanService());
        var calls = 0;
        var missing = setup.PrepareExisting(new AppConfig(), () => Path.Combine(root, "missing.md"), _ => { calls++; return "TODO"; });
        Assert(missing.Status == BoardSetupStatus.NoColumns && calls == 0, "missing file must retain the parser's empty-column behavior without selecting");
        var path = Path.Combine(root, "no-columns.md");
        File.WriteAllText(path, "ordinary synthetic text\n");
        Assert(setup.PrepareExisting(new AppConfig(), () => path, _ => { calls++; return "TODO"; }).Status == BoardSetupStatus.NoColumns && calls == 0,
            "no-column file must report without selecting");
        using var handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert(setup.PrepareExisting(new AppConfig(), () => path, _ => "TODO").Status == BoardSetupStatus.ReadFailed,
            "locked source must report actual read failure");
    }

    private static void TestCreateFailure(string root)
    {
        var setup = new BoardSetupWorkflow(new MarkdownKanbanService());
        var path = Path.Combine(root, "existing-create.md");
        File.WriteAllText(path, "synthetic existing source\n");
        var original = File.ReadAllBytes(path);
        var result = setup.PrepareNew(new AppConfig(), () => KanbanBoardTemplate.TodoDone, () => path);
        Assert(result.Status == BoardSetupStatus.WriteFailed && !result.FileCreated && result.Board is null,
            "CreateNew failure must not claim file/view creation");
        Assert(File.ReadAllBytes(path).SequenceEqual(original), "failed new setup must not overwrite an existing file");
        result = setup.PrepareNew(new AppConfig(), () => KanbanBoardTemplate.TodoDone,
            () => Path.Combine(root, "backup", "blocked-new.md"));
        Assert(result.Status == BoardSetupStatus.WriteFailed && !result.FileCreated, "new source safety policy must still reject backup paths");
    }

    private static void TestExistingSelection(string root)
    {
        var setup = new BoardSetupWorkflow(new MarkdownKanbanService());
        var path = Path.Combine(root, "selected-existing.md");
        File.WriteAllText(path, "## TODO\n\n- [ ] keep ^keep-id\n\n## DONE\n");
        var original = File.ReadAllBytes(path);
        var draft = new AppConfig();
        var result = setup.PrepareExisting(draft, () => path, columns =>
        {
            Assert(columns.SequenceEqual(new[] { "TODO", "DONE" }), "chooser must receive actual source columns");
            return "DONE";
        });
        Assert(result.Status == BoardSetupStatus.Added && result.Board?.DefaultColumn == "DONE" && !result.FileCreated,
            "existing source must prepare selected column without claiming creation");
        Assert(draft.Boards.Count == 0 && File.ReadAllBytes(path).SequenceEqual(original), "adding a view must not mutate active config or source");
    }
}
