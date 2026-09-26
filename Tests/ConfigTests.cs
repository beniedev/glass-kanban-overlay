using System.Text;
using System.Text.Json;
using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class ConfigTests
{
    public static void Run(string root)
    {
        LogService.Initialize(AppPaths.FromRoot(root));
        TestPublicDefaultConfig(root);
        TestConfigCleanup(root);
        TestRemoveBoardView(root);
        TestJsonNullCompatibility(root);
        TestNestedNullDefaults(root);
        TestPathResolution(root);
        TestFixedServiceAndLogPaths(root);
        TestNormalizer();
        TestReadFailuresPreserveConfig(root);
        TestAtomicSavePublishes(root);
        TestPartialTemporaryWritePreservesExisting(root);
        TestPublishFailurePreservesExisting(root);
        TestMissingPublishFailureDoesNotClaimSave(root);
        TestLockedDestinationPreservesConfig(root);
        TestInitialPublicationRace(root);
        TestCleanupFailurePreservesPrimary(root);
    }

    private static void TestPublicDefaultConfig(string root)
    {
        var service = new ConfigService(AppPaths.FromRoot(Path.Combine(root, "public-config")));
        var config = service.Load();
        Assert(config.Boards.Count == 0, "public default config should not include machine-specific boards");
        Assert(config.UiLanguage == "auto", "public default language should be auto");
        Assert(File.Exists(service.Paths.ConfigPath), "missing config should be created");
        Assert(Directory.Exists(service.Paths.LogDirectory), "Load should retain explicit directory initialization");
    }

    private static void TestConfigCleanup(string root)
    {
        var paths = AppPaths.FromRoot(Path.Combine(root, "config-cleanup"));
        var service = new ConfigService(paths);
        service.Initialize();
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
        File.WriteAllText(paths.ConfigPath, json);

        var config = service.Load();
        Assert(config.Boards.All(x => !string.IsNullOrWhiteSpace(x.Id)), "board IDs should be non-empty");
        Assert(config.Boards.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == config.Boards.Count, "board IDs should be unique");
        Assert(config.BoardWindows.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { validBoard }), "invalid or disabled board layouts should be removed");
        Assert(config.OpenBoardWindowIds.SequenceEqual(new[] { validBoard }), "open windows should only retain enabled board IDs");
        Assert(config.SummaryWindow.Width == 420 && config.SummaryWindow.Height == 620, "invalid summary dimensions should use defaults");
        Assert(config.SummaryWindow.Opacity == 0.95 && config.SummaryWindow.PlacementMode == "topmost", "invalid summary layout should be normalized");
        var validLayout = config.BoardWindows[validBoard];
        Assert(validLayout.Left == 12 && validLayout.Width == 321 && validLayout.PlacementMode == "normal", "valid board layout should be preserved");

        config.OpenBoardWindowIds.Clear();
        service.Save(config);
        var closed = service.Load();
        Assert(closed.OpenBoardWindowIds.Count == 0, "closing all widgets must stay closed when layouts remain saved");
    }

    private static void TestRemoveBoardView(string root)
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

        var removed = new ConfigService(AppPaths.FromRoot(root)).RemoveBoardView(config, board.Id);
        Assert(removed, "remove board view should report a removed view");
        Assert(config.Boards.Count == 0, "remove board view should remove the board configuration");
        Assert(config.OpenBoardWindowIds.Count == 0, "remove board view should clear open window state");
        Assert(config.BoardWindows.Count == 0, "remove board view should clear saved board layout");
        Assert(ReferenceEquals(config.SummaryWindow, summary), "remove board view should not alter summary layout");
        Assert(File.ReadAllBytes(path).SequenceEqual(before), "remove board view must not modify the source Markdown");
    }

    private static void TestJsonNullCompatibility(string root)
    {
        var service = new ConfigService(AppPaths.FromRoot(Path.Combine(root, "json-null-characterization")));
        service.Initialize();
        File.WriteAllText(service.Paths.ConfigPath, "null");
        var loaded = service.Load();
        Assert(loaded.Boards.Count == 0 && loaded.UiLanguage == "auto" && loaded.Startup is not null,
            "JSON null must load the existing public default configuration");
        Assert(File.ReadAllText(service.Paths.ConfigPath).TrimStart().StartsWith('{'),
            "JSON null must be replaced by the serialized default configuration");
    }

    private static void TestNestedNullDefaults(string root)
    {
        var service = new ConfigService(AppPaths.FromRoot(Path.Combine(root, "null-defaults")));
        service.Initialize();
        File.WriteAllText(service.Paths.ConfigPath,
            """{"boards":null,"boardWindows":null,"openBoardWindowIds":null,"summaryWindow":null,"startup":null}""");
        var loaded = service.Load();
        Assert(loaded.Boards.Count == 0 && loaded.BoardWindows.Count == 0 && loaded.OpenBoardWindowIds.Count == 0,
            "null collections must retain their default semantics");
        Assert(loaded.SummaryWindow.Width == 420 && loaded.SummaryWindow.Height == 620,
            "null summary layout must use the existing default");
        Assert(loaded.Startup is not null && !loaded.Startup.StartWithWindows && !loaded.Startup.StartMinimizedToTray,
            "startup:null must normalize to default options rather than fail at startup");
        var persisted = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(service.Paths.ConfigPath), ConfigJson.Options)!;
        Assert(persisted.Startup is not null, "the compatible Startup default must also be persisted");
    }

    private static void TestPathResolution(string root)
    {
        var current = Path.Combine(root, "path-current");
        var configured = Path.Combine(root, "path-home");
        var fallback = Path.Combine(root, "path-base");
        Directory.CreateDirectory(current);
        Directory.CreateDirectory(configured);
        Directory.CreateDirectory(fallback);
        File.WriteAllText(Path.Combine(current, "DesktopOverlayBoard.csproj"), "");
        Assert(AppPaths.Resolve(configured, current, fallback).RootDirectory == configured,
            "an existing configured home must take precedence over the CWD marker");
        Assert(AppPaths.Resolve(Path.Combine(root, "nonexistent-home"), current, fallback).RootDirectory == current,
            "a nonexistent configured home must fall back to a project CWD");
        Assert(AppPaths.Resolve("\0", current, fallback).RootDirectory == current,
            "an invalid configured home must retain fallback semantics");
        var relative = Path.Combine(current, "relative-home");
        Directory.CreateDirectory(relative);
        Assert(AppPaths.Resolve("relative-home", current, fallback).RootDirectory == relative,
            "a relative configured home must resolve against the captured CWD");
        var dataCurrent = Path.Combine(root, "path-data-current");
        Directory.CreateDirectory(Path.Combine(dataCurrent, "Data"));
        Assert(AppPaths.Resolve(null, dataCurrent, fallback).RootDirectory == dataCurrent,
            "a CWD Data directory must retain its selection priority");
        var emptyCurrent = Path.Combine(root, "path-empty-current");
        Directory.CreateDirectory(emptyCurrent);
        Assert(AppPaths.Resolve(null, emptyCurrent, fallback).RootDirectory == fallback,
            "an unmarked CWD must fall back to the explicit base directory");
        Throws<ArgumentException>(() => AppPaths.FromRoot("relative-root"), "explicit roots must not fall back to an ambient CWD");
    }

    private static void TestFixedServiceAndLogPaths(string root)
    {
        var initialHome = Path.Combine(root, "fixed-home");
        var otherHome = Path.Combine(root, "changed-home");
        var otherCurrent = Path.Combine(root, "changed-current");
        Directory.CreateDirectory(initialHome);
        Directory.CreateDirectory(otherHome);
        Directory.CreateDirectory(otherCurrent);
        var paths = AppPaths.Resolve(initialHome, root, otherHome);
        var service = new ConfigService(paths);
        var config = service.Load();
        var previousHome = Environment.GetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME");
        var previousCurrent = Directory.GetCurrentDirectory();
        try
        {
            Environment.SetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME", otherHome);
            Directory.SetCurrentDirectory(otherCurrent);
            config.UiLanguage = "en";
            service.Save(config);
            var loaded = service.Load();
            Assert(loaded.UiLanguage == "en" && ReferenceEquals(service.Paths, paths),
                "Load and Save must keep the same captured path values after environment/CWD changes");
            Assert(!Directory.Exists(Path.Combine(otherHome, "Data")) && !Directory.Exists(Path.Combine(otherCurrent, "Data")),
                "changed environment/CWD must not create or read a second configuration root");
            LogService.Error(new IOException("Synthetic logging failure"), "Synthetic fixed-path log check");
            Assert(Directory.EnumerateFiles(Path.Combine(root, "Log"), "*.log").Any(),
                "explicit log initialization must keep logs in the test root");
            Assert(!Directory.Exists(Path.Combine(otherHome, "Log")) && !Directory.Exists(Path.Combine(otherCurrent, "Log")),
                "logs must not drift to a changed environment/CWD");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrent);
            Environment.SetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME", previousHome);
        }
    }

    private static void TestNormalizer()
    {
        var boardLayout = new WindowLayout { Left = double.NaN, Width = double.NaN, Height = -1, Opacity = 2, PlacementMode = "invalid" };
        var config = new AppConfig
        {
            UiLanguage = "EN-us",
            Boards = new()
            {
                new BoardConfig { Id = "kept" },
                new BoardConfig { Id = "KEPT" },
                new BoardConfig { Id = "" },
                new BoardConfig { Id = "disabled", Enabled = false },
                null!,
            },
            SummaryWindow = new WindowLayout
            {
                Left = double.NaN, Top = double.PositiveInfinity, Width = double.NegativeInfinity,
                Height = 0, Opacity = double.NaN, PlacementMode = "invalid", AlwaysOnTop = false,
            },
            BoardWindows = new()
            {
                ["kept"] = boardLayout, ["KEPT"] = new WindowLayout { Left = 999 },
                ["disabled"] = new WindowLayout(), ["missing"] = new WindowLayout(), ["null"] = null!,
            },
            OpenBoardWindowIds = new() { "KEPT", "kept", "disabled", "missing" },
            Startup = null!,
        };
        var ids = new Queue<string>(new[] { "kept", "replacement", "blank-id" });
        ConfigNormalizer.Normalize(config, ids.Dequeue);
        Assert(config.UiLanguage == "en" && config.Boards.Select(board => board.Id).SequenceEqual(new[] { "kept", "replacement", "blank-id", "disabled" }),
            "normalization must preserve valid IDs and regenerate blank/case-colliding IDs");
        Assert(config.OpenBoardWindowIds.SequenceEqual(new[] { "KEPT" }) && config.BoardWindows.Count == 1,
            "enabled IDs, first layout and open-window order must retain their existing semantics");
        Assert(ReferenceEquals(config.BoardWindows["KEPT"], boardLayout),
            "case-insensitive layout lookup must retain the first valid object");
        Assert(config.SummaryWindow.Left == 80 && config.SummaryWindow.Top == 80 &&
            config.SummaryWindow.Width == 420 && config.SummaryWindow.Height == 620 &&
            config.SummaryWindow.Opacity == 0.78 && config.SummaryWindow.PlacementMode == "desktop",
            "non-finite summary values must use existing defaults");
        Assert(boardLayout.Left == 80 && boardLayout.Width == 380 && boardLayout.Height == 560 &&
            boardLayout.Opacity == 0.95 && boardLayout.PlacementMode == "topmost",
            "board layout normalization must retain clamp, size and placement defaults");
        Assert(config.Startup is not null, "normalization must include null startup options");
    }

    private static void TestReadFailuresPreserveConfig(string root)
    {
        var service = new ConfigService(AppPaths.FromRoot(Path.Combine(root, "read-failures")));
        service.Initialize();
        File.WriteAllText(service.Paths.ConfigPath, "{ broken JSON", new UTF8Encoding(false));
        var original = File.ReadAllBytes(service.Paths.ConfigPath);
        Throws<JsonException>(() => service.Load(), "invalid JSON must propagate rather than create defaults");
        Assert(File.ReadAllBytes(service.Paths.ConfigPath).SequenceEqual(original), "invalid JSON must retain the original bytes");
        AssertNoTemporaryFiles(service.Paths);
        var directory = Path.Combine(root, "locked-read");
        var lockedService = new ConfigService(AppPaths.FromRoot(directory));
        lockedService.Save(new AppConfig { UiLanguage = "en" });
        var lockedOriginal = File.ReadAllBytes(lockedService.Paths.ConfigPath);
        using (var handle = new FileStream(lockedService.Paths.ConfigPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Throws<IOException>(() => lockedService.Load(), "a locked read must propagate rather than create defaults");
        }
        Assert(File.ReadAllBytes(lockedService.Paths.ConfigPath).SequenceEqual(lockedOriginal), "a read failure must not change configuration bytes");
        AssertNoTemporaryFiles(lockedService.Paths);
    }

    private static void TestAtomicSavePublishes(string root)
    {
        var service = new ConfigService(AppPaths.FromRoot(Path.Combine(root, "atomic-save")));
        service.Save(new AppConfig { UiLanguage = "en" });
        service.Save(new AppConfig { UiLanguage = "zh" });
        var bytes = File.ReadAllBytes(service.Paths.ConfigPath);
        Assert(!(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf), "atomic saves must retain UTF-8 without BOM");
        var persisted = JsonSerializer.Deserialize<AppConfig>(Encoding.UTF8.GetString(bytes), ConfigJson.Options)!;
        Assert(persisted.UiLanguage == "zh" && persisted.Startup is not null, "Save must publish the complete latest configuration");
        AssertNoTemporaryFiles(service.Paths);
    }

    private static void TestPartialTemporaryWritePreservesExisting(string root)
    {
        var paths = AppPaths.FromRoot(Path.Combine(root, "partial-write"));
        new ConfigService(paths).Save(new AppConfig { UiLanguage = "en" });
        var original = File.ReadAllBytes(paths.ConfigPath);
        var publishCalls = 0;
        var failure = new IOException("Synthetic temporary write failure");
        var service = new ConfigService(paths, (temporary, _) =>
        {
            File.WriteAllText(temporary, "partial JSON", new UTF8Encoding(false));
            throw failure;
        }, (_, _, _) => publishCalls++);
        var observed = Throws<IOException>(() => service.Save(new AppConfig { UiLanguage = "zh" }), "temporary write failure must propagate");
        Assert(ReferenceEquals(observed, failure) && publishCalls == 0, "failed temporary writes must not enter publication");
        Assert(File.ReadAllBytes(paths.ConfigPath).SequenceEqual(original), "partial temporary writes must preserve the old JSON");
        AssertNoTemporaryFiles(paths);
    }

    private static void TestPublishFailurePreservesExisting(string root)
    {
        var paths = AppPaths.FromRoot(Path.Combine(root, "publish-failure"));
        new ConfigService(paths).Save(new AppConfig { UiLanguage = "en" });
        var original = File.ReadAllBytes(paths.ConfigPath);
        var service = new ConfigService(paths, ConfigService.WriteTemporaryFile, (temporary, destination, exists) =>
        {
            Assert(exists && destination == paths.ConfigPath && Path.GetDirectoryName(temporary) == paths.DataDirectory && File.Exists(temporary),
                "publication must receive an existing target and a complete same-directory temporary file");
            throw new IOException("Synthetic replacement failure");
        });
        Throws<IOException>(() => service.Save(new AppConfig { UiLanguage = "zh" }), "replacement failure must propagate");
        Assert(File.ReadAllBytes(paths.ConfigPath).SequenceEqual(original), "replacement failure must preserve the old JSON");
        AssertNoTemporaryFiles(paths);
    }

    private static void TestMissingPublishFailureDoesNotClaimSave(string root)
    {
        var paths = AppPaths.FromRoot(Path.Combine(root, "missing-publish-failure"));
        var service = new ConfigService(paths, ConfigService.WriteTemporaryFile, (_, _, exists) =>
        {
            Assert(!exists, "missing configuration must use non-overwriting publication");
            throw new IOException("Synthetic initial publication failure");
        });
        Throws<IOException>(() => service.Load(), "initial default publication failure must propagate");
        Assert(!File.Exists(paths.ConfigPath), "failed default publication must not claim a saved configuration");
        AssertNoTemporaryFiles(paths);
    }

    private static void TestLockedDestinationPreservesConfig(string root)
    {
        var service = new ConfigService(AppPaths.FromRoot(Path.Combine(root, "locked-publication")));
        service.Save(new AppConfig { UiLanguage = "en" });
        var original = File.ReadAllBytes(service.Paths.ConfigPath);
        using (var handle = new FileStream(service.Paths.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Throws<IOException>(() => service.Save(new AppConfig { UiLanguage = "zh" }), "a locked replacement must propagate");
        }
        Assert(File.ReadAllBytes(service.Paths.ConfigPath).SequenceEqual(original), "a locked target must retain the old JSON");
        AssertNoTemporaryFiles(service.Paths);
    }

    private static void TestInitialPublicationRace(string root)
    {
        var paths = AppPaths.FromRoot(Path.Combine(root, "initial-publication-race"));
        var external = """{"uiLanguage":"en","boards":[]}""";
        var service = new ConfigService(paths, ConfigService.WriteTemporaryFile, (temporary, destination, exists) =>
        {
            Assert(!exists, "publication race should begin with a missing target");
            File.WriteAllText(destination, external, new UTF8Encoding(false));
            ConfigService.PublishTemporaryFile(temporary, destination, exists);
        });
        Throws<IOException>(() => service.Save(new AppConfig { UiLanguage = "zh" }), "initial publication must not overwrite a concurrently created target");
        Assert(File.ReadAllText(paths.ConfigPath) == external, "publication race must preserve the newly created external JSON");
        AssertNoTemporaryFiles(paths);
    }

    private static void TestCleanupFailurePreservesPrimary(string root)
    {
        var paths = AppPaths.FromRoot(Path.Combine(root, "cleanup-failure"));
        new ConfigService(paths).Save(new AppConfig { UiLanguage = "en" });
        var original = File.ReadAllBytes(paths.ConfigPath);
        var failure = new IOException("Synthetic primary write failure");
        string? blockedCleanup = null;
        var service = new ConfigService(paths, (temporary, _) =>
        {
            blockedCleanup = temporary;
            Directory.CreateDirectory(temporary);
            throw failure;
        }, (_, _, _) => throw new InvalidOperationException("Failed writes must not publish."));
        var observed = Throws<IOException>(() => service.Save(new AppConfig { UiLanguage = "zh" }),
            "cleanup failure must not mask the primary save failure");
        Assert(ReferenceEquals(observed, failure) && File.ReadAllBytes(paths.ConfigPath).SequenceEqual(original),
            "cleanup failure must preserve the primary exception and old configuration");
        Assert(Directory.Exists(blockedCleanup), "failed cleanup must not claim the temporary path was removed");
        Assert(Directory.EnumerateFiles(Path.Combine(root, "Log"), "*.log")
            .Any(path => File.ReadAllText(path).Contains("Configuration temporary-file cleanup failed.", StringComparison.Ordinal)),
            "cleanup failure diagnostics must use only the explicitly initialized synthetic log root");
    }

    private static T Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidOperationException(message);
    }

    private static void AssertNoTemporaryFiles(ResolvedAppPaths paths)
    {
        Assert(!Directory.EnumerateFiles(paths.DataDirectory, ".config.json.overlay-*.tmp").Any(),
            "the current Save must clean its own temporary file");
    }
}
