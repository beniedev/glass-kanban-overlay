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
        TestConflictPolicyBoundaries(service, root);
        TestWriteMutexBusy(service, root);
        TestWriteMutexAbandoned(service, root);
        TestExistingDocumentReadFailures(service, root);
        TestExistingDocumentPublishFailures(service, root);
        TestBlockedWriteEntryPoints(service, root);
        TestTaskWriteLineEndings(service, root);
        TestTransactionFailureAndSourceLifetime(service, root);
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

    private static void TestConflictPolicyBoundaries(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "transaction-conflicts.md");
        const string content = "## BEFORE\n\n- [ ] earlier\n\n## TODO\n\n- [ ] target ^target-id\n\n## AFTER\n\n- [ ] later\n";
        File.WriteAllText(path, content);
        var board = BoardFor(path);
        var task = service.LoadGroup(board, incompleteOnly: false).Tasks.Single();
        var before = File.ReadAllBytes(path);

        var staleLine = task with { OriginalLine = "- [ ] different original line" };
        AssertRejectedUnchanged(path, before, service.ToggleTask(staleLine, done: true),
            LocalizationService.Text("Error.SourceChanged"), "OriginalLine mismatch");
        AssertRejectedUnchanged(path, before, service.ToggleTask(staleLine with { ColumnRangeHash = "stale" }, done: true),
            LocalizationService.Text("Error.ColumnChanged"), "column hash must be checked before OriginalLine");
        AssertRejectedUnchanged(path, before, service.ToggleTask(staleLine with { ColumnTitle = "Missing", ColumnRangeHash = "stale" }, done: true),
            LocalizationService.Text("Error.ColumnMissing", "Missing"), "missing column must be checked before column hash");

        File.WriteAllText(path, content.Replace("- [ ] earlier", "- [ ] earlier\n\n- [ ] inserted before", StringComparison.Ordinal));
        Assert(service.LoadGroup(board, incompleteOnly: false).ColumnRangeHash == task.ColumnRangeHash,
            "a preceding column edit must retain the target column hash");
        before = File.ReadAllBytes(path);
        AssertRejectedUnchanged(path, before, service.ToggleTask(task, done: true),
            LocalizationService.Text("Error.SourceChanged"), "shifted LineIndex must be protected by OriginalLine");

        File.WriteAllText(path, content);
        task = service.LoadGroup(board, incompleteOnly: false).Tasks.Single();
        const string external = "\n- [ ] external after\n";
        File.AppendAllText(path, external);
        Assert(service.LoadGroup(board, incompleteOnly: false).ColumnRangeHash == task.ColumnRangeHash,
            "a following column edit must retain the target column hash");
        Assert(service.ToggleTask(task, done: true).Success, "unrelated following-column edit must not be treated as a full-document conflict");
        Assert(File.ReadAllText(path) == (content + external).Replace("- [ ] target ^target-id", "- [x] target ^target-id", StringComparison.Ordinal),
            "column-scoped writing must preserve the unrelated edit");

        var oldHash = service.Parse(path).FullHash;
        File.AppendAllText(path, "\n## DOING\n");
        before = File.ReadAllBytes(path);
        AssertRejectedUnchanged(path, before, service.CreateMissingColumn(path, "DOING", oldHash),
            LocalizationService.Text("Error.SourceChanged"), "full-document hash must be checked before an already-existing column");
        AssertRejectedUnchanged(path, before, service.CreateMissingColumn(path, "doing", service.Parse(path).FullHash),
            LocalizationService.Text("Error.ColumnAlreadyExists", "doing"), "existing columns must be found case-insensitively");
        AssertMutexAvailableFromOtherThread(path);
    }

    private static void TestWriteMutexBusy(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "transaction-busy.md");
        File.WriteAllText(path, "## TODO\n\n- [ ] original\n");
        var task = service.LoadGroup(BoardFor(path), incompleteOnly: false).Tasks.Single();
        var hash = service.Parse(path).FullHash;
        var before = File.ReadAllBytes(path);
        using var mutex = MarkdownWriteTransaction.CreateMutex(path);
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception? ownerError = null;
        var owner = new Thread(() =>
        {
            var lockTaken = false;
            try
            {
                lockTaken = mutex.WaitOne(TestTimeout);
                if (!lockTaken) throw new TimeoutException("synthetic mutex owner could not acquire the lock");
                acquired.Set();
                if (!release.Wait(TestTimeout)) throw new TimeoutException("synthetic mutex owner was not released");
            }
            catch (Exception ex)
            {
                ownerError = ex;
                acquired.Set();
            }
            finally
            {
                if (lockTaken) mutex.ReleaseMutex();
            }
        }) { IsBackground = true };
        owner.Start();
        try
        {
            Assert(acquired.Wait(TestTimeout), "synthetic mutex owner must signal acquisition");
            Assert(ownerError is null, $"synthetic mutex owner failed: {ownerError}");
            var busy = LocalizationService.Text("Error.WriteBusy");
            AssertRejectedUnchanged(path, before, service.ToggleTask(task, done: true), busy, "busy task write");
            AssertRejectedUnchanged(path, before, service.CreateMissingColumn(path, "DOING", hash), busy, "busy missing-column write");
            AssertRejectedUnchanged(path, before, service.CreateBoardFile(path, KanbanBoardTemplate.TodoDone), busy,
                "CreateNew must retain its mutex-before-existing-file check");
        }
        finally
        {
            release.Set();
            Assert(owner.Join(TestTimeout), "synthetic mutex owner must terminate");
        }

        Assert(ownerError is null, $"synthetic mutex owner failed: {ownerError}");
        Assert(service.ToggleTask(task, done: true).Success, "a released busy mutex must permit a subsequent task write");
        Assert(service.CreateMissingColumn(path, "DOING", service.Parse(path).FullHash).Success,
            "a released busy mutex must permit a subsequent missing-column write");
        AssertMutexAvailableFromOtherThread(path);
    }

    private static void TestWriteMutexAbandoned(MarkdownKanbanService service, string root)
    {
        foreach (var operation in new[] { "task", "column", "new-board" })
        {
            var path = Path.Combine(root, $"transaction-abandoned-{operation}.md");
            KanbanTask? task = null;
            var hash = "";
            if (operation != "new-board")
            {
                File.WriteAllText(path, "## TODO\n\n- [ ] original\n");
                task = service.LoadGroup(BoardFor(path), incompleteOnly: false).Tasks.Single();
                hash = service.Parse(path).FullHash;
            }

            // Keep this handle alive while its owning thread exits without releasing.
            using var mutex = MarkdownWriteTransaction.CreateMutex(path);
            Exception? ownerError = null;
            var owner = new Thread(() =>
            {
                try
                {
                    if (!mutex.WaitOne(TestTimeout)) throw new TimeoutException("synthetic abandoned owner could not acquire the lock");
                }
                catch (Exception ex)
                {
                    ownerError = ex;
                }
            }) { IsBackground = true };
            owner.Start();
            Assert(owner.Join(TestTimeout), "synthetic abandoned owner must terminate");
            Assert(ownerError is null, $"synthetic abandoned owner failed: {ownerError}");

            var result = operation switch
            {
                "task" => service.ToggleTask(task!, done: true),
                "column" => service.CreateMissingColumn(path, "DOING", hash),
                _ => service.CreateBoardFile(path, KanbanBoardTemplate.TodoDone),
            };
            Assert(result.Success, $"{operation} must recover an abandoned mutex: {result.Error}");
            AssertMutexAvailableFromOtherThread(path);
        }
    }

    private static void TestExistingDocumentReadFailures(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "transaction-read-failure.md");
        File.WriteAllText(path, "## TODO\n\n- [ ] original\n");
        var task = service.LoadGroup(BoardFor(path), incompleteOnly: false).Tasks.Single();
        var hash = service.Parse(path).FullHash;
        var before = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            AssertWriteFailure(service.ToggleTask(task, done: true), "locked task read");
            AssertWriteFailure(service.CreateMissingColumn(path, "DOING", hash), "locked missing-column read");
            AssertMutexAvailableFromOtherThread(path);
        }

        Assert(File.ReadAllBytes(path).SequenceEqual(before), "read failures must preserve original bytes");
        AssertNoTransactionTempFiles(path);
        var missingPath = Path.Combine(root, "transaction-nonexistent.md");
        AssertWriteFailure(service.ToggleTask(task with { FilePath = missingPath }, done: true), "missing task source");
        AssertWriteFailure(service.CreateMissingColumn(missingPath, "DOING", hash), "missing column source");
        Assert(!File.Exists(missingPath), "existing-document writes must never create a missing source");
        AssertNoTransactionTempFiles(missingPath);
        AssertMutexAvailableFromOtherThread(missingPath);
    }

    private static void TestExistingDocumentPublishFailures(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "transaction-publish-failure.md");
        File.WriteAllText(path, "## TODO\n\n- [ ] original\n");
        var task = service.LoadGroup(BoardFor(path), incompleteOnly: false).Tasks.Single();
        var hash = service.Parse(path).FullHash;
        var before = File.ReadAllBytes(path);
        // Reading remains possible; this handle prevents File.Replace from publishing.
        using var cannotReplace = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        AssertWriteFailure(service.ToggleTask(task, done: true), "task publish blocked by a reader without delete sharing");
        Assert(File.ReadAllBytes(path).SequenceEqual(before), "failed task publication must preserve original bytes");
        AssertNoTransactionTempFiles(path);
        AssertMutexAvailableFromOtherThread(path);
        AssertWriteFailure(service.CreateMissingColumn(path, "DOING", hash), "missing-column publish blocked by a reader without delete sharing");
        Assert(File.ReadAllBytes(path).SequenceEqual(before), "failed missing-column publication must preserve original bytes");
        AssertNoTransactionTempFiles(path);
        AssertMutexAvailableFromOtherThread(path);
    }

    private static void TestBlockedWriteEntryPoints(MarkdownKanbanService service, string root)
    {
        var directory = Path.Combine(root, "backup");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "blocked-existing.md");
        File.WriteAllText(path, "## TODO\n\n- [ ] original\n");
        var board = BoardFor(path);
        var group = service.LoadGroup(board, incompleteOnly: false);
        var before = File.ReadAllBytes(path);
        var blocked = LocalizationService.Text("Error.BlockedWritePath");
        AssertRejectedUnchanged(path, before, service.ToggleTask(group.Tasks.Single(), done: true), blocked, "blocked task write");
        AssertRejectedUnchanged(path, before, service.AddTask(board, "TODO", group.ColumnRangeHash, "added"), blocked, "blocked add");
        AssertRejectedUnchanged(path, before, service.CreateMissingColumn(path, "DOING", service.Parse(path).FullHash), blocked, "blocked missing-column write");
        AssertRejectedUnchanged(path, before, service.CreateMissingColumn(path, "invalid\ncolumn", ""),
            LocalizationService.Text("Error.InvalidColumn"), "column input validation must precede blocked-path validation");
        AssertRejectedUnchanged(path, before, service.CreateMissingColumn(path, "DOING", ""),
            LocalizationService.Text("Error.SourceChanged"), "missing document hash must precede blocked-path validation");
        AssertNoTransactionTempFiles(path);
    }

    private static void TestTaskWriteLineEndings(MarkdownKanbanService service, string root)
    {
        foreach (var newLine in new[] { "\n", "\r\n" })
        foreach (var finalNewLine in new[] { false, true })
        {
            var path = Path.Combine(root, $"transaction-format-{newLine.Length}-{finalNewLine}.md");
            var content = string.Join(newLine, new[]
            {
                "---", "kanban-plugin: board", "---", "", "Intro paragraph.", "", "## TODO", "",
                "- [ ] first ^first-id", "", "***", "", "## Archive", "", "- [x] old ^old-id", "",
                "%% kanban:settings", "```", "{\"kanban-plugin\":\"board\"}", "```", "%%",
            }) + (finalNewLine ? newLine : "");
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
            var board = BoardFor(path);
            var task = service.LoadGroup(board, incompleteOnly: false).Tasks.Single();
            Assert(service.ToggleTask(task, done: true).Success, "formatted task toggle must succeed");
            var expected = content.Replace("- [ ] first ^first-id", "- [x] first ^first-id", StringComparison.Ordinal);
            Assert(File.ReadAllBytes(path).SequenceEqual(System.Text.Encoding.UTF8.GetBytes(expected)),
                "toggle must preserve line endings, EOF, frontmatter, paragraphs, archive, settings and block IDs byte-for-byte");
            task = service.LoadGroup(board, incompleteOnly: false).Tasks.Single();
            Assert(service.RenameTask(task, "renamed").Success, "formatted task rename must succeed");
            expected = expected.Replace("- [x] first ^first-id", "- [x] renamed ^first-id", StringComparison.Ordinal);
            Assert(File.ReadAllBytes(path).SequenceEqual(System.Text.Encoding.UTF8.GetBytes(expected)),
                "rename must preserve all bytes outside the edited text");
        }
    }

    private static void TestTransactionFailureAndSourceLifetime(MarkdownKanbanService service, string root)
    {
        var path = Path.Combine(root, "transaction-shell.md");
        const string content = "## TODO\n\n- [ ] original\n";
        File.WriteAllText(path, content);
        var before = File.ReadAllBytes(path);
        var document = service.Parse(path);
        var operationCalled = false;
        var parseFailure = MarkdownWriteTransaction.Execute(path, "Synthetic parse failure",
            _ => throw new IOException("synthetic parse failure"),
            _ => { operationCalled = true; return KanbanWriteResult.Ok(); });
        AssertRejectedUnchanged(path, before, parseFailure, LocalizationService.Text("Error.WriteFailed", "synthetic parse failure"), "parse failure");
        Assert(!operationCalled, "a failed parse must never enter the operation");
        AssertMutexAvailableFromOtherThread(path);

        var operationFailure = MarkdownWriteTransaction.Execute(path, "Synthetic operation failure",
            _ => document, _ => throw new IOException("synthetic operation failure"));
        AssertRejectedUnchanged(path, before, operationFailure, LocalizationService.Text("Error.WriteFailed", "synthetic operation failure"), "operation exception");
        AssertMutexAvailableFromOtherThread(path);

        var rejected = KanbanWriteResult.Fail("synthetic operation rejection");
        var result = MarkdownWriteTransaction.Execute(path, "Synthetic handle lifetime",
            text => { Assert(text == content, "the parser must receive the complete raw source"); return document; },
            parsed =>
            {
                Assert(ReferenceEquals(parsed, document), "the operation must receive the parser's document");
                try
                {
                    using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    throw new InvalidOperationException("source handle closed before the operation");
                }
                catch (IOException)
                {
                    return rejected;
                }
            });
        Assert(ReferenceEquals(result, rejected), "an operation rejection must be returned unchanged");
        Assert(File.ReadAllBytes(path).SequenceEqual(before), "operation rejection must preserve original bytes");
        using (var after = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None)) { }
        AssertNoTransactionTempFiles(path);
        AssertMutexAvailableFromOtherThread(path);
    }

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static BoardConfig BoardFor(string path) => new()
    {
        DisplayName = "Synthetic board",
        VaultName = "Tests",
        FilePath = path,
        DefaultColumn = "TODO",
    };

    private static void AssertRejectedUnchanged(string path, byte[] before, KanbanWriteResult result, string error, string context)
    {
        Assert(!result.Success && result.Error == error, $"{context} must return the expected failure: {result.Error}");
        Assert(File.ReadAllBytes(path).SequenceEqual(before), $"{context} must preserve original bytes");
    }

    private static void AssertWriteFailure(KanbanWriteResult result, string context)
    {
        Assert(!result.Success && result.Error is not null &&
            result.Error.StartsWith(LocalizationService.Text("Error.WriteFailed", ""), StringComparison.Ordinal),
            $"{context} must return a write failure: {result.Error}");
    }

    private static void AssertNoTransactionTempFiles(string path)
    {
        Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.overlay-*.tmp").Any(),
            "a completed or rejected write must not leave a transaction temporary file");
    }

    private static void AssertMutexAvailableFromOtherThread(string path)
    {
        var available = false;
        Exception? error = null;
        var observer = new Thread(() =>
        {
            using var mutex = MarkdownWriteTransaction.CreateMutex(path);
            var lockTaken = false;
            try
            {
                lockTaken = mutex.WaitOne(0);
                available = lockTaken;
            }
            catch (AbandonedMutexException ex)
            {
                lockTaken = true;
                error = ex;
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                if (lockTaken) mutex.ReleaseMutex();
            }
        }) { IsBackground = true };
        observer.Start();
        Assert(observer.Join(TestTimeout), "mutex observer must terminate");
        Assert(error is null && available, $"the transaction must release its mutex for another thread: {error}");
    }
}
