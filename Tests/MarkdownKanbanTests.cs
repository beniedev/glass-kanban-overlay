using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using System.Windows;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class MarkdownKanbanTests
{
    public static void Run(string root)
    {
        var service = new MarkdownKanbanService();
        TestParseDefaults(service, root);
        TestCreateBoardTemplates(service, root);
        TestCreateBoardRefusesExistingFile(service, root);
        TestCreateMissingColumnPreservesDocument(service, root);
        TestCreateMissingColumnRefusesConflict(service, root);
        TestToggleRenameAddDelete(service, root);
        TestArchiveTask(service, root);
        TestArchiveTaskWithSettings(service, root);
        TestTaskReorder(service, root);
        TestExternalChangeRefusal(service, root);
        TestMultilineTaskRefusal(service, root);
        TestLockedFileFailure(service, root);
        TestBlockedArchivePath();
    }

    private static void TestParseDefaults(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "Reading Track.md");
        File.WriteAllText(path, """
    ---
    kanban-plugin: board
    ---

    ## in Process - English Reading

    - [x] The faces of Injustice: Introduction ^3ydh9v
    - [ ] Moby Dick: or, The Whale

    ## Completed - English reading

    %% kanban:settings
    ```
    {"kanban-plugin":"board"}
    ```
    %%
    """);

        var doc = service.Parse(path);
        Assert(doc.Columns.Count == 2, "expected two columns");
        var first = doc.Columns[0];
        Assert(first.Title == "in Process - English Reading", "column title mismatch");
        Assert(first.Tasks.Count == 2, "expected two tasks");
        Assert(first.Tasks[0].BlockId == "^3ydh9v", "block id not preserved");
        Assert(first.Tasks[0].Text == "The faces of Injustice: Introduction", "block id should be removed from editable text");
    }

    private static void TestCreateBoardTemplates(MarkdownKanbanService service, string root)
    {
        foreach (var template in new[] { KanbanBoardTemplate.TodoDone, KanbanBoardTemplate.TodoDoingDone })
        {
            var path = Path.Combine(root, $"new-{template}.md");
            var result = service.CreateBoardFile(path, template);
            Assert(result.Success, $"{template} board creation failed: {result.Error}");
            var document = service.Parse(path);
            var expected = MarkdownKanbanService.GetTemplateColumns(template);
            Assert(document.Columns.Select(x => x.Title).SequenceEqual(expected), $"{template} columns mismatch");
            var text = File.ReadAllText(path);
            Assert(text.Contains("kanban-plugin: board", StringComparison.Ordinal), "new board should include frontmatter");
            Assert(text.Contains("%% kanban:settings", StringComparison.Ordinal), "new board should include Kanban settings");
        }
    }

    private static void TestCreateBoardRefusesExistingFile(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "existing-board.md");
        File.WriteAllText(path, "keep this file\n");
        var before = File.ReadAllBytes(path);
        var result = service.CreateBoardFile(path, KanbanBoardTemplate.TodoDone);
        Assert(!result.Success, "new board must refuse an existing file");
        Assert(File.ReadAllBytes(path).SequenceEqual(before), "existing board must remain byte-for-byte unchanged");

        var blockedDirectory = Path.Combine(root, "backup");
        Directory.CreateDirectory(blockedDirectory);
        var blocked = service.CreateBoardFile(Path.Combine(blockedDirectory, "new-board.md"), KanbanBoardTemplate.TodoDone);
        Assert(!blocked.Success, "new board must refuse a blocked archive/backup path");
    }

    private static void TestCreateMissingColumnPreservesDocument(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "missing-column.md");
        var content = "---\r\nkanban-plugin: board\r\n---\r\n\r\nIntro paragraph.\r\n\r\n## TODO\r\n\r\n- [ ] keep this ^keep123\r\n\r\n***\r\n\r\n## Archive\r\n\r\n- [x] old card ^old123\r\n\r\n%% kanban:settings\r\n```\r\n{\"kanban-plugin\":\"board\"}\r\n```\r\n%%\r\n";
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
        var expectedHash = service.Parse(path).FullHash;
        var result = service.CreateMissingColumn(path, "DOING", expectedHash);
        Assert(result.Success, $"missing column creation failed: {result.Error}");

        var after = File.ReadAllText(path);
        Assert(after.Contains("## DOING\r\n", StringComparison.Ordinal), "missing column heading should be created");
        Assert(after.IndexOf("## DOING", StringComparison.Ordinal) < after.IndexOf("***", StringComparison.Ordinal), "new column should stay before archive");
        Assert(after.Contains("Intro paragraph.\r\n", StringComparison.Ordinal), "ordinary Markdown should be preserved");
        Assert(after.Contains("^keep123", StringComparison.Ordinal) && after.Contains("^old123", StringComparison.Ordinal), "block IDs should be preserved");
        Assert(after.Contains("%% kanban:settings\r\n", StringComparison.Ordinal), "Kanban settings should be preserved");
        Assert(!after.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n'), "line endings should stay CRLF");
        Assert(service.Parse(path).Columns.Select(x => x.Title).SequenceEqual(new[] { "TODO", "DOING" }), "created column should be parser-visible");

        var noFinalNewlinePath = Path.Combine(root, "missing-column-no-final-newline.md");
        File.WriteAllText(noFinalNewlinePath, "## TODO", new System.Text.UTF8Encoding(false));
        var noFinalNewlineHash = service.Parse(noFinalNewlinePath).FullHash;
        Assert(service.CreateMissingColumn(noFinalNewlinePath, "DOING", noFinalNewlineHash).Success, "column creation without a final newline should succeed");
        Assert(!File.ReadAllText(noFinalNewlinePath).EndsWith('\n'), "missing-column creation should preserve no final newline");
    }

    private static void TestCreateMissingColumnRefusesConflict(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "missing-column-conflict.md");
        File.WriteAllText(path, "## TODO\n\n- [ ] first\n");
        var expectedHash = service.Parse(path).FullHash;
        File.AppendAllText(path, "\nExternal edit\n");
        var before = File.ReadAllText(path);
        var result = service.CreateMissingColumn(path, "DOING", expectedHash);
        Assert(!result.Success, "missing column creation must refuse a changed source");
        Assert(File.ReadAllText(path) == before, "conflicting source must remain unchanged");
    }

    private static void TestToggleRenameAddDelete(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "看板.md");
        File.WriteAllText(path, """
    ---
    kanban-plugin: board
    ---

    ## TODO

    - [ ] update flashcard app

    %% kanban:settings
    ```
    {"kanban-plugin":"board"}
    ```
    %%
    """);

        var board = new BoardConfig
        {
            DisplayName = "Language",
            VaultName = "Language",
            FilePath = path,
            DefaultColumn = "TODO",
        };

        var group = service.LoadGroup(board, incompleteOnly: false);
        var task = group.Tasks.Single();
        Assert(service.ToggleTask(task, done: true).Success, "toggle failed");
        var afterToggle = File.ReadAllText(path);
        Assert(afterToggle.Contains("- [x] update flashcard app"), "toggle should only change checkbox");

        group = service.LoadGroup(board, incompleteOnly: false);
        task = group.Tasks.Single();
        Assert(service.RenameTask(task, "update flashcard app slowly ^not-a-block").Success, "rename failed");
        var afterRename = File.ReadAllText(path);
        Assert(afterRename.Contains("- [x] update flashcard app slowly ^not-a-block"), "rename should preserve text");

        group = service.LoadGroup(board, incompleteOnly: false);
        Assert(service.AddTask(board, "TODO", group.ColumnRangeHash, "French review 20 min").Success, "add failed");
        var afterAdd = File.ReadAllText(path);
        Assert(afterAdd.IndexOf("- [ ] French review 20 min", StringComparison.Ordinal) < afterAdd.IndexOf("%% kanban:settings", StringComparison.Ordinal), "add should insert before kanban settings");

        group = service.LoadGroup(board, incompleteOnly: false);
        var deleteTarget = group.Tasks.First(x => x.Text.Contains("French review", StringComparison.Ordinal));
        Assert(service.DeleteTask(deleteTarget).Success, "delete failed");
        Assert(!File.ReadAllText(path).Contains("French review", StringComparison.Ordinal), "delete should remove only target line");
    }

    private static void TestArchiveTask(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "completed-section.md");
        File.WriteAllText(path, """
    ---
    kanban-plugin: board
    ---

    ## TODO

    - [ ] keep plain markdown ^abc123

    %% kanban:settings
    ```
    {"kanban-plugin":"board"}
    ```
    %%
    """);

        var board = new BoardConfig
        {
            DisplayName = "Archive",
            VaultName = "Archive",
            FilePath = path,
            DefaultColumn = "TODO",
        };

        var task = service.LoadGroup(board, incompleteOnly: false).Tasks.Single();
        Assert(service.ArchiveTask(task).Success, "archive failed");
        var after = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert(after.Contains("***\n\n## Archive\n\n- [ ] keep plain markdown ^abc123", StringComparison.Ordinal), "archive should append card to Kanban archive section");
        Assert(after.IndexOf("## TODO", StringComparison.Ordinal) < after.IndexOf("***", StringComparison.Ordinal), "archive section should stay below board lanes");
        Assert(after.IndexOf("***", StringComparison.Ordinal) < after.IndexOf("%% kanban:settings", StringComparison.Ordinal), "archive section should stay before kanban settings");
        var group = service.LoadGroup(board, incompleteOnly: false);
        Assert(group.Tasks.Count == 0, "archived task should leave active column");
    }

    private static void TestArchiveTaskWithSettings(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "completed-settings.md");
        File.WriteAllText(path, """
    ## TODO

    - [ ] first
    - [ ] second

    ***

    ## Archive

    - [ ] old archived

    %% kanban:settings
    ```
    {"kanban-plugin":"board","archive-with-date":true,"archive-date-format":"YYYY-MM-DD","archive-date-separator":"::","append-archive-date":true,"max-archive-size":1}
    ```
    %%
    """);

        var board = new BoardConfig
        {
            DisplayName = "ArchiveSettings",
            VaultName = "ArchiveSettings",
            FilePath = path,
            DefaultColumn = "TODO",
        };

        var task = service.LoadGroup(board, incompleteOnly: false).Tasks.First(x => x.Text == "first");
        Assert(service.ArchiveTask(task).Success, "archive with settings failed");
        var after = File.ReadAllText(path);
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        Assert(after.Contains($"- [ ] first :: {today}", StringComparison.Ordinal), "archive date should follow append/separator settings");
        Assert(!after.Contains("old archived", StringComparison.Ordinal), "max-archive-size should remove oldest archived card");
        Assert(after.Contains("- [ ] second", StringComparison.Ordinal), "archive should not touch other active cards");
    }

    private static void TestTaskReorder(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "reorder.md");
        File.WriteAllText(path, """
    ## TODO

    - [ ] first
    - [ ] second
    - [ ] third
    """);

        var board = new BoardConfig
        {
            DisplayName = "Reorder",
            VaultName = "Reorder",
            FilePath = path,
            DefaultColumn = "TODO",
        };

        var group = service.LoadGroup(board, incompleteOnly: false);
        var third = group.Tasks.Single(x => x.Text == "third");
        Assert(service.MoveTaskToTop(third).Success, "move to top failed");
        var afterTop = File.ReadAllText(path);
        Assert(afterTop.IndexOf("third", StringComparison.Ordinal) < afterTop.IndexOf("first", StringComparison.Ordinal), "third should be first");

        group = service.LoadGroup(board, incompleteOnly: false);
        var first = group.Tasks.Single(x => x.Text == "first");
        var second = group.Tasks.Single(x => x.Text == "second");
        Assert(service.MoveTaskAfter(first, second).Success, "move after failed");
        var afterMove = File.ReadAllText(path);
        Assert(afterMove.IndexOf("second", StringComparison.Ordinal) < afterMove.IndexOf("first", StringComparison.Ordinal), "first should move after second");
    }

    private static void TestExternalChangeRefusal(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "conflict.md");
        File.WriteAllText(path, """
    ## TODO

    - [ ] first
    - [ ] second
    """);

        var board = new BoardConfig
        {
            DisplayName = "Conflict",
            VaultName = "Conflict",
            FilePath = path,
            DefaultColumn = "TODO",
        };

        var task = service.LoadGroup(board, incompleteOnly: false).Tasks.First();
        File.AppendAllText(path, "\n- [ ] external\n");
        var result = service.ToggleTask(task, done: true);
        Assert(!result.Success, "external column change must be refused");
    }

    private static void TestMultilineTaskRefusal(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "single-line.md");
        File.WriteAllText(path, "## TODO\n\n- [ ] original\n");
        var board = new BoardConfig
        {
            DisplayName = "Single line",
            VaultName = "Tests",
            FilePath = path,
            DefaultColumn = "TODO",
        };

        var group = service.LoadGroup(board, incompleteOnly: false);
        var before = File.ReadAllText(path);
        Assert(!service.RenameTask(group.Tasks.Single(), "changed\n## Injected").Success, "multiline rename should be refused");
        Assert(!service.AddTask(board, group.ColumnTitle, group.ColumnRangeHash, "added\r\n- [ ] injected").Success, "multiline add should be refused");
        Assert(File.ReadAllText(path) == before, "multiline input must not change the board");
    }

    private static void TestLockedFileFailure(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "locked.md");
        File.WriteAllText(path, "## TODO\n\n- [ ] original\n");
        var board = new BoardConfig
        {
            DisplayName = "Locked",
            VaultName = "Tests",
            FilePath = path,
            DefaultColumn = "TODO",
        };

        var task = service.LoadGroup(board, incompleteOnly: false).Tasks.Single();
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = service.ToggleTask(task, done: true);
        Assert(!result.Success, "locked file should return a write failure instead of throwing");
    }

    private static void TestBlockedArchivePath()
    {
        Assert(MarkdownKanbanService.IsBlockedPath(@"C:\ExampleVaults\Vault\归档\Kanban.md"), "归档 path should be blocked");
        Assert(MarkdownKanbanService.IsBlockedPath(@"C:\ExampleVaults\Vault\backup\Kanban.md"), "backup path should be blocked");
    }
}
