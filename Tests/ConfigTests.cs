using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using System.Windows;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class ConfigTests
{
    public static void Run(string root)
    {
        TestPublicDefaultConfig(root);
        TestConfigCleanup(root);
        TestRemoveBoardView(root);
    }

    private static void TestPublicDefaultConfig(string root)
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

    private static void TestConfigCleanup(string root)
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

        var removed = new ConfigService().RemoveBoardView(config, board.Id);
        Assert(removed, "remove board view should report a removed view");
        Assert(config.Boards.Count == 0, "remove board view should remove the board configuration");
        Assert(config.OpenBoardWindowIds.Count == 0, "remove board view should clear open window state");
        Assert(config.BoardWindows.Count == 0, "remove board view should clear saved board layout");
        Assert(ReferenceEquals(config.SummaryWindow, summary), "remove board view should not alter summary layout");
        Assert(File.ReadAllBytes(path).SequenceEqual(before), "remove board view must not modify the source Markdown");
    }
}
