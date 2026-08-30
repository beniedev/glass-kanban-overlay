using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using System.Windows;

var tempRoot = Path.Combine(Path.GetTempPath(), "DesktopOverlayBoardTests", Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(tempRoot);

try
{
    var service = new MarkdownKanbanService();
    TestParseDefaults(service, tempRoot);
    TestCreateBoardTemplates(service, tempRoot);
    TestCreateBoardRefusesExistingFile(service, tempRoot);
    TestCreateMissingColumnPreservesDocument(service, tempRoot);
    TestCreateMissingColumnRefusesConflict(service, tempRoot);
    TestToggleRenameAddDelete(service, tempRoot);
    TestArchiveTask(service, tempRoot);
    TestArchiveTaskWithSettings(service, tempRoot);
    TestTaskReorder(service, tempRoot);
    TestExternalChangeRefusal(service, tempRoot);
    TestMultilineTaskRefusal(service, tempRoot);
    TestLockedFileFailure(service, tempRoot);
    TestBlockedArchivePath();
    TestPublicDefaultConfig(tempRoot);
    TestConfigCleanup(tempRoot);
    TestRemoveBoardView(tempRoot);
    TestPendingRefreshGate();
    TestWindowPlacementClamp();
    TestWindowPlacementVisibleWorkingArea();
    TestSingleInstanceSignal();
    TestLocalization();
    Console.WriteLine("DesktopOverlayBoard.Tests: all tests passed");
    return 0;
}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch
    {
        // Best effort cleanup.
    }
}

static void TestParseDefaults(MarkdownKanbanService service, string root)
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

static void TestCreateBoardTemplates(MarkdownKanbanService service, string root)
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

static void TestCreateBoardRefusesExistingFile(MarkdownKanbanService service, string root)
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

static void TestCreateMissingColumnPreservesDocument(MarkdownKanbanService service, string root)
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

static void TestCreateMissingColumnRefusesConflict(MarkdownKanbanService service, string root)
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

static void TestToggleRenameAddDelete(MarkdownKanbanService service, string root)
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

static void TestArchiveTask(MarkdownKanbanService service, string root)
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
    var after = File.ReadAllText(path);
    Assert(after.Contains("***\n\n## Archive\n\n- [ ] keep plain markdown ^abc123", StringComparison.Ordinal), "archive should append card to Kanban archive section");
    Assert(after.IndexOf("## TODO", StringComparison.Ordinal) < after.IndexOf("***", StringComparison.Ordinal), "archive section should stay below board lanes");
    Assert(after.IndexOf("***", StringComparison.Ordinal) < after.IndexOf("%% kanban:settings", StringComparison.Ordinal), "archive section should stay before kanban settings");
    var group = service.LoadGroup(board, incompleteOnly: false);
    Assert(group.Tasks.Count == 0, "archived task should leave active column");
}

static void TestArchiveTaskWithSettings(MarkdownKanbanService service, string root)
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

static void TestExternalChangeRefusal(MarkdownKanbanService service, string root)
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

static void TestBlockedArchivePath()
{
    Assert(MarkdownKanbanService.IsBlockedPath(@"C:\ExampleVaults\Vault\归档\Kanban.md"), "归档 path should be blocked");
    Assert(MarkdownKanbanService.IsBlockedPath(@"C:\ExampleVaults\Vault\backup\Kanban.md"), "backup path should be blocked");
}

static void TestMultilineTaskRefusal(MarkdownKanbanService service, string root)
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

static void TestLockedFileFailure(MarkdownKanbanService service, string root)
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

static void TestPublicDefaultConfig(string root)
{
    var previousHome = Environment.GetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME");
    var home = Path.Combine(root, "public-config");
    Directory.CreateDirectory(home);
    try
    {
        Environment.SetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME", home);
        var config = new ConfigService().Load();
        Assert(config.Boards.Count == 0, "public default config should not include machine-specific boards");
        Assert(config.UiLanguage == "auto", "public default language should be auto");
    }
    finally
    {
        Environment.SetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME", previousHome);
    }
}

static void TestConfigCleanup(string root)
{
    var previousHome = Environment.GetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME");
    var home = Path.Combine(root, "config-cleanup");
    Directory.CreateDirectory(Path.Combine(home, "Data"));
    try
    {
        Environment.SetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME", home);
        var validBoard = "valid-board";
        var disabledBoard = "disabled-board";
        var json = $$"""
        {
          "uiLanguage": "en",
          "boards": [
            { "id": "{{validBoard}}", "displayName": "Valid", "filePath": "C:\\Boards\\valid.md", "defaultColumn": "TODO", "enabled": true },
            { "id": "{{disabledBoard}}", "displayName": "Disabled", "filePath": "C:\\Boards\\disabled.md", "defaultColumn": "TODO", "enabled": false },
            { "id": "", "displayName": "Needs id", "filePath": "C:\\Boards\\needs-id.md", "defaultColumn": "TODO", "enabled": true }
          ],
          "summaryWindow": { "left": 0, "top": 0, "width": 0, "height": -1, "opacity": 2, "alwaysOnTop": true, "placementMode": "unknown" },
          "boardWindows": {
            "{{validBoard}}": { "left": 12, "top": 13, "width": 321, "height": 322, "opacity": 0.5, "alwaysOnTop": false, "placementMode": "normal" },
            "{{disabledBoard}}": { "left": 20, "top": 20, "width": 320, "height": 320, "opacity": 0.5, "alwaysOnTop": false, "placementMode": "normal" },
            "missing-board": { "left": 1, "top": 1, "width": 100, "height": 100, "opacity": 0.5, "alwaysOnTop": true, "placementMode": "topmost" },
            "null-layout": null
          },
          "openBoardWindowIds": [ "{{validBoard}}", "{{validBoard}}", "{{disabledBoard}}", "missing-board" ]
        }
        """;
        File.WriteAllText(AppPaths.ConfigPath, json);

        var config = new ConfigService().Load();
        Assert(config.Boards.All(x => !string.IsNullOrWhiteSpace(x.Id)), "board IDs should be non-empty");
        Assert(config.Boards.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == config.Boards.Count, "board IDs should be unique");
        Assert(config.BoardWindows.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { validBoard }), "invalid or disabled board layouts should be removed");
        Assert(config.OpenBoardWindowIds.SequenceEqual(new[] { validBoard }), "open windows should only retain enabled board IDs");
        Assert(config.SummaryWindow.Width == 420 && config.SummaryWindow.Height == 620, "invalid summary dimensions should use defaults");
        Assert(config.SummaryWindow.Opacity == 0.95 && config.SummaryWindow.PlacementMode == "topmost", "invalid summary layout should be normalized");
        var validLayout = config.BoardWindows[validBoard];
        Assert(validLayout.Left == 12 && validLayout.Width == 321 && validLayout.PlacementMode == "normal", "valid board layout should be preserved");

        config.OpenBoardWindowIds.Clear();
        new ConfigService().Save(config);
        var closed = new ConfigService().Load();
        Assert(closed.OpenBoardWindowIds.Count == 0, "closing all widgets must stay closed when layouts remain saved");
    }
    finally
    {
        Environment.SetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME", previousHome);
    }
}

static void TestRemoveBoardView(string root)
{
    var path = Path.Combine(root, "remove-view-source.md");
    File.WriteAllText(path, "## TODO\r\n\r\n- [ ] keep this source\r\n", new System.Text.UTF8Encoding(false));
    var before = File.ReadAllBytes(path);
    var board = new BoardConfig
    {
        Id = "board-to-remove",
        DisplayName = "Remove me",
        FilePath = path,
        DefaultColumn = "TODO",
    };
    var summary = new WindowLayout { Left = 17, Top = 19 };
    var config = new AppConfig
    {
        Boards = new List<BoardConfig> { board },
        OpenBoardWindowIds = new List<string> { "BOARD-TO-REMOVE" },
        BoardWindows = new Dictionary<string, WindowLayout> { ["BOARD-TO-REMOVE"] = new WindowLayout() },
        SummaryWindow = summary,
    };

    var removed = new ConfigService().RemoveBoardView(config, board.Id);
    Assert(removed, "remove board view should report a removed view");
    Assert(config.Boards.Count == 0, "remove board view should remove the board configuration");
    Assert(config.OpenBoardWindowIds.Count == 0, "remove board view should clear open window state");
    Assert(config.BoardWindows.Count == 0, "remove board view should clear saved board layout");
    Assert(ReferenceEquals(config.SummaryWindow, summary), "remove board view should not alter summary layout");
    Assert(File.ReadAllBytes(path).SequenceEqual(before), "remove board view must not modify the source Markdown");
}

static void TestPendingRefreshGate()
{
    var gate = new PendingRefreshGate();
    Assert(!gate.ShouldDefer && !gate.HasPendingRefresh, "refresh gate should start ready");

    gate.BeginDraft();
    Assert(gate.ShouldDefer, "active draft should defer refresh");
    gate.Defer();
    Assert(gate.HasPendingRefresh, "deferred refresh should become pending");
    Assert(!gate.TryConsumeReady(), "pending refresh must not consume during a draft");

    gate.EndDraft();
    Assert(gate.TryConsumeReady(), "pending refresh should consume after draft ends");
    Assert(!gate.HasPendingRefresh, "consuming refresh should clear pending state");

    gate.MarkPending();
    gate.Clear();
    Assert(!gate.TryConsumeReady(), "cleared refresh should not consume");
}

static void TestWindowPlacementClamp()
{
    var clamped = WindowPlacementService.ClampToWorkingArea(new Rect(-120, -40, 500, 300), new Rect(0, 0, 1920, 1080));
    Assert(clamped == new Rect(0, 0, 500, 300), "off-screen window should be clamped into the working area");

    var multiScreen = new Rect(-1800, 120, 500, 300);
    var preserved = WindowPlacementService.ClampToWorkingArea(multiScreen, new Rect(-1920, 0, 1920, 1080));
    Assert(preserved == multiScreen, "valid multi-screen placement should be preserved");
}

static void TestWindowPlacementVisibleWorkingArea()
{
    var areas = new[]
    {
        new Rect(-1920, 0, 1920, 1080),
        new Rect(0, 0, 1920, 1080),
    };
    var valid = new Rect(-1800, 120, 500, 300);
    Assert(WindowPlacementService.ClampToVisibleWorkingArea(valid, areas) == valid, "valid multi-screen placement should remain unchanged");

    var onePixelSliver = new Rect(1919, 200, 100, 100);
    var recovered = WindowPlacementService.ClampToVisibleWorkingArea(onePixelSliver, areas);
    Assert(recovered == new Rect(1820, 200, 100, 100), "a one-pixel intersection should be recovered into a visible working area");
}

static void TestSingleInstanceSignal()
{
    var suffix = Guid.NewGuid().ToString("n");
    var firstActivated = new ManualResetEventSlim(false);
    using var first = new SingleInstanceService($"Local\\GlassKanbanOverlay.Tests.{suffix}", $"Local\\GlassKanbanOverlay.Tests.Activate.{suffix}");
    Assert(first.TryAcquire(firstActivated.Set), "first instance should acquire the mutex");

    using var secondCompleted = new ManualResetEventSlim(false);
    var secondRejected = false;
    var secondTask = Task.Run(() =>
    {
        using var second = new SingleInstanceService($"Local\\GlassKanbanOverlay.Tests.{suffix}", $"Local\\GlassKanbanOverlay.Tests.Activate.{suffix}");
        secondRejected = !second.TryAcquire(() => { });
        secondCompleted.Set();
    });
    Assert(secondCompleted.Wait(TimeSpan.FromSeconds(2)), "second instance attempt should complete");
    secondTask.GetAwaiter().GetResult();
    Assert(secondRejected, "second instance should be rejected");
    Assert(firstActivated.Wait(TimeSpan.FromSeconds(2)), "second instance should signal the first instance");
}

static void TestLocalization()
{
    var recoveryKeys = new[]
    {
        "Action.ReselectColumn",
        "Action.CreateMissingColumn",
        "Action.RemoveFromSummary",
        "Dialog.MissingColumn",
        "Dialog.CreateMissingColumn",
        "Dialog.RemoveFromSummary",
        "Message.ReselectColumnPrompt",
        "Message.CreateMissingColumnPrompt",
        "Message.RemoveFromSummaryPrompt",
    };

    foreach (var language in new[] { "en", "zh", "zh-Hant", "ja", "ko", "fr", "es", "ru", "ar" })
    {
        LocalizationService.Use(language);
        var addCard = LocalizationService.Text("Action.AddCard");
        Assert(!string.IsNullOrWhiteSpace(addCard), $"{language} add-card label should exist");
        Assert(addCard != "Action.AddCard", $"{language} add-card label should be translated");
        Assert(LocalizationService.Text("Error.WriteFailed", "test").Contains("test", StringComparison.Ordinal), $"{language} write error should format details");
        foreach (var key in recoveryKeys)
        {
            var text = LocalizationService.Text(key);
            Assert(!string.IsNullOrWhiteSpace(text) && text != key, $"{language} {key} should be available");
        }

        Assert(LocalizationService.Text("Message.CreateMissingColumnPrompt", "DOING").Contains("DOING", StringComparison.Ordinal), $"{language} missing-column prompt should format the title");
        Assert(LocalizationService.Text("Message.RemoveFromSummaryPrompt", "Reading").Contains("Reading", StringComparison.Ordinal), $"{language} remove prompt should format the board");
    }

    LocalizationService.Use("zh");
    Assert(LocalizationService.Text("Action.ReselectColumn") == "重新选择列", "zh reselect-column label mismatch");
    Assert(LocalizationService.Text("Action.CreateMissingColumn") == "创建缺失列", "zh create-column label mismatch");
    Assert(LocalizationService.Text("Action.RemoveFromSummary") == "从汇总移除", "zh remove-summary label mismatch");
    Assert(LocalizationService.NormalizeCode("zh-TW") == "zh-Hant", "zh-TW should normalize to zh-Hant");
    LocalizationService.Use("zh-TW");
    Assert(LocalizationService.CurrentCode == "zh-Hant", "zh-TW should resolve to zh-Hant");
    LocalizationService.Use("auto");
}

static void TestTaskReorder(MarkdownKanbanService service, string root)
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

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
