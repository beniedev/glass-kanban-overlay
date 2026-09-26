namespace DesktopOverlayBoard.Tests;

internal static class TestRunner
{
    private sealed record Group(string Name, Func<string, Task> RunAsync);

    private static readonly Group[] Groups =
    [
        new("markdown", root => RunSync(() => MarkdownKanbanTests.Run(root))),
        new("recovery", MissingColumnRecoveryTests.RunAsync),
        new("widget-ui", _ => RunSync(WidgetUiTests.Run)),
        new("config", root => RunSync(() => ConfigTests.Run(root))),
        new("settings-workflow", SettingsWorkflowTests.RunAsync),
        new("board-setup", root => RunSync(() => BoardSetupTests.Run(root))),
        new("startup", root => RunSync(() => StartupServiceTests.Run(root))),
        new("window-state", root => RunSync(() => WindowStateTests.Run(root))),
        new("runtime", root => RunSync(() => RuntimeContractTests.Run(root))),
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        string? tempRoot = null;
        var previousHome = Environment.GetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME");
        try
        {
            if (args.SequenceEqual(new[] { "--list" }))
            {
                Console.WriteLine(string.Join(Environment.NewLine, Groups.Select(group => group.Name)));
                return 0;
            }

            var selected = Groups.AsEnumerable();
            if (args.Length != 0)
            {
                if (args.Length != 2 || args[0] != "--group")
                {
                    throw new ArgumentException("Use --list or --group <name>; omit arguments to run every group.");
                }
                selected = Groups.Where(group => group.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase));
                if (!selected.Any()) throw new ArgumentException($"Unknown test group: {args[1]}");
            }

            tempRoot = Path.Combine(Path.GetTempPath(), "GlassKanbanOverlay.Tests", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(tempRoot);
            var syntheticHome = Path.Combine(tempRoot, "synthetic-home");
            Directory.CreateDirectory(syntheticHome);
            Environment.SetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME", syntheticHome);
            DesktopOverlayBoard.Services.LogService.Initialize(
                DesktopOverlayBoard.Services.AppPaths.FromRoot(syntheticHome));

            foreach (var group in selected)
            {
                var groupRoot = Path.Combine(tempRoot, group.Name);
                Directory.CreateDirectory(groupRoot);
                await group.RunAsync(groupRoot);
                Console.WriteLine($"DesktopOverlayBoard.Tests: {group.Name} passed");
            }
            Console.WriteLine(args.Length == 0
                ? "DesktopOverlayBoard.Tests: all tests passed"
                : "DesktopOverlayBoard.Tests: selected group passed");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"DesktopOverlayBoard.Tests: failed: {error}");
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME", previousHome);
            if (tempRoot is not null)
            {
                try { Directory.Delete(tempRoot, recursive: true); }
                catch { /* Best effort cleanup of this invocation's synthetic fixture. */ }
            }
        }
    }

    private static Task RunSync(Action test)
    {
        test();
        return Task.CompletedTask;
    }
}
