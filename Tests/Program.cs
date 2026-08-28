using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;

var tempRoot = Path.Combine(Path.GetTempPath(), "DesktopOverlayBoardTests", Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(tempRoot);

try
{
    var service = new MarkdownKanbanService();
    TestParseDefaults(service, tempRoot);
    TestToggleRenameAddDelete(service, tempRoot);
    TestArchiveTask(service, tempRoot);
    TestArchiveTaskWithSettings(service, tempRoot);
    TestTaskReorder(service, tempRoot);
    TestExternalChangeRefusal(service, tempRoot);
    TestMultilineTaskRefusal(service, tempRoot);
    TestLockedFileFailure(service, tempRoot);
    TestBlockedArchivePath();
    TestPublicDefaultConfig(tempRoot);
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

static void TestLocalization()
{
    foreach (var language in new[] { "en", "zh", "zh-Hant", "ja", "ko", "fr", "es", "ru", "ar" })
    {
        LocalizationService.Use(language);
        var addCard = LocalizationService.Text("Action.AddCard");
        Assert(!string.IsNullOrWhiteSpace(addCard), $"{language} add-card label should exist");
        Assert(addCard != "Action.AddCard", $"{language} add-card label should be translated");
        Assert(LocalizationService.Text("Error.WriteFailed", "test").Contains("test", StringComparison.Ordinal), $"{language} write error should format details");
    }

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
