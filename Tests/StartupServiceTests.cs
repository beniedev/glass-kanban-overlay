using DesktopOverlayBoard.Services;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class StartupServiceTests
{
    public static void Run(string root)
    {
        TestUnavailable(root);
        TestRead(root);
        TestEnableDisableOwnership(root);
        TestFailures(root);
        TestExecutablePriority(root);
        TestCapturedPaths(root);
    }

    private static StartupService Create(string root, FakeRunKeys keys,
        string? processPath = null, string? baseDirectory = null) =>
        new(AppPaths.FromRoot(root), keys, processPath, baseDirectory ?? root);

    private static void TestUnavailable(string root)
    {
        var keys = new FakeRunKeys { Available = false };
        var service = Create(root, keys);
        var read = service.ReadStartWithWindows();
        Assert(!read.Available && !read.Enabled && !string.IsNullOrWhiteSpace(read.Error), "null read key must report unavailable");
        Assert(!service.ApplyStartWithWindows(true).Success, "null write key must not claim enabling startup");
        Assert(!service.ApplyStartWithWindows(false).Success, "null write key must not claim disabling startup");
        Assert(keys.Events.SequenceEqual(new[] { "open:read", "open:write", "open:write" }), "unavailable key must not attempt registry operations");
    }

    private static void TestRead(string root)
    {
        var keys = new FakeRunKeys();
        var service = Create(root, keys);
        Assert(service.ReadStartWithWindows() is { Available: true, Enabled: false }, "empty readable key should report disabled");
        keys.Values["DesktopOverlayBoard"] = "synthetic legacy command";
        Assert(service.ReadStartWithWindows() is { Available: true, Enabled: true }, "existing legacy nonempty value remains readable");
        keys.Values.Clear(); keys.Values["GlassKanbanOverlay"] = "synthetic current command";
        Assert(service.ReadStartWithWindows().Enabled, "current Run value should report enabled");
        keys.Values["GlassKanbanOverlay"] = "  ";
        Assert(!service.ReadStartWithWindows().Enabled, "blank Run value must not report enabled");
        Assert(keys.DisposeCount == 4, "each successful read must dispose its Run key");
    }

    private static void TestEnableDisableOwnership(string root)
    {
        var keys = new FakeRunKeys();
        var exe = Path.Combine(root, "synthetic-app.exe");
        var service = Create(root, keys, exe);
        keys.Values["DesktopOverlayBoard"] = "foreign-command";
        Assert(service.ApplyStartWithWindows(true).Success, "synthetic enable should succeed");
        Assert((string)keys.Values["GlassKanbanOverlay"] == $"\"{exe}\" --startup", "startup command quoting/argument must remain compatible");
        Assert((string)keys.Values["DesktopOverlayBoard"] == "foreign-command", "foreign legacy Run value must be retained");
        Assert(service.ApplyStartWithWindows(false).Success && !keys.Values.ContainsKey("GlassKanbanOverlay"), "disable must remove current app entry");
        Assert(keys.Values.ContainsKey("DesktopOverlayBoard"), "disable must also retain foreign legacy values");

        foreach (var owned in new[] { "GlassKanbanOverlay.exe", "glass-kanban-overlay portable", Path.Combine(root, "old.exe") })
        {
            keys.Values["DesktopOverlayBoard"] = owned;
            Assert(service.ApplyStartWithWindows(true).Success && !keys.Values.ContainsKey("DesktopOverlayBoard"), "enable must clean only owned legacy value");
            keys.Values["DesktopOverlayBoard"] = owned;
            Assert(service.ApplyStartWithWindows(false).Success && !keys.Values.ContainsKey("DesktopOverlayBoard"), "disable must clean owned legacy value");
        }
        Assert(keys.DisposeCount == 8, "each application must dispose its key");
    }

    private static void TestFailures(string root)
    {
        foreach (var operation in new[] { "open:write", "get:DesktopOverlayBoard", "set:GlassKanbanOverlay", "dispose" })
        {
            var keys = new FakeRunKeys { FailOn = operation };
            var result = Create(root, keys).ApplyStartWithWindows(true);
            Assert(!result.Success && result.Error?.Contains("synthetic", StringComparison.Ordinal) == true,
                "startup failure must be an observable failed receipt: " + operation);
            if (operation != "open:write") Assert(keys.DisposeCount == 1, "failed operations must dispose the opened key");
        }
        var deleteKeys = new FakeRunKeys { FailOn = "delete:GlassKanbanOverlay" };
        Assert(!Create(root, deleteKeys).ApplyStartWithWindows(false).Success && deleteKeys.DisposeCount == 1,
            "disable failure must also report and dispose");
        foreach (var operation in new[] { "open:read", "get:GlassKanbanOverlay", "dispose" })
        {
            var keys = new FakeRunKeys { FailOn = operation };
            Assert(!Create(root, keys).ReadStartWithWindows().Available, "read error must remain distinguishable from disabled: " + operation);
        }
    }

    private static void TestExecutablePriority(string root)
    {
        var process = Path.Combine(root, "process", "synthetic.exe");
        var portableRoot = Path.Combine(root, "portable-priority");
        var portable = Path.Combine(portableRoot, "dist", "GlassKanbanOverlay-win-x64-portable", "GlassKanbanOverlay.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(portable)!); File.WriteAllText(portable, "synthetic");
        var flat = Path.Combine(portableRoot, "dist", "GlassKanbanOverlay.exe"); File.WriteAllText(flat, "synthetic");
        Assert(Create(portableRoot, new FakeRunKeys(), process).ResolveStartupExecutable() == portable, "portable executable must have first priority");
        var flatRoot = Path.Combine(root, "flat-priority");
        var flatOnly = Path.Combine(flatRoot, "dist", "GlassKanbanOverlay.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(flatOnly)!); File.WriteAllText(flatOnly, "synthetic");
        Assert(Create(flatRoot, new FakeRunKeys(), process).ResolveStartupExecutable() == flatOnly, "flat dist executable must precede process path");
        var processRoot = Path.Combine(root, "process-priority");
        Assert(Create(processRoot, new FakeRunKeys(), process).ResolveStartupExecutable() == process, "non-dotnet process path must precede base directory");
        var fallback = Path.Combine(root, "fallback");
        Assert(Create(processRoot, new FakeRunKeys(), Path.Combine(root, "dotnet.exe"), fallback).ResolveStartupExecutable() ==
            Path.Combine(fallback, "GlassKanbanOverlay.exe"), "dotnet host must use captured base directory fallback");
        Assert(Create(processRoot, new FakeRunKeys(), null, fallback).ResolveStartupExecutable() ==
            Path.Combine(fallback, "GlassKanbanOverlay.exe"), "missing process path must use base directory fallback");
    }

    private static void TestCapturedPaths(string root)
    {
        var keys = new FakeRunKeys();
        var originalRoot = Path.Combine(root, "captured");
        var service = Create(originalRoot, keys, null, originalRoot);
        var previousHome = Environment.GetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME");
        var previousCwd = Directory.GetCurrentDirectory();
        var other = Path.Combine(root, "unrelated"); Directory.CreateDirectory(other);
        try
        {
            Environment.SetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME", other);
            Directory.SetCurrentDirectory(other);
            keys.Values["DesktopOverlayBoard"] = Path.Combine(originalRoot, "old.exe");
            Assert(service.ApplyStartWithWindows(true).Success && !keys.Values.ContainsKey("DesktopOverlayBoard"),
                "legacy ownership must use the captured application root");
            Assert((string)keys.Values["GlassKanbanOverlay"] == $"\"{Path.Combine(originalRoot, "GlassKanbanOverlay.exe")}\" --startup",
                "startup resolution must not drift with later environment/CWD changes");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCwd);
            Environment.SetEnvironmentVariable("GLASS_KANBAN_OVERLAY_HOME", previousHome);
        }
    }

    private sealed class FakeRunKeys : IStartupRunKeyAccess
    {
        public readonly Dictionary<string, object> Values = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Events = new();
        public bool Available = true;
        public string? FailOn;
        public int DisposeCount;

        public IStartupRunKey? Open(bool writable)
        {
            Record(writable ? "open:write" : "open:read");
            return Available ? new FakeKey(this) : null;
        }

        private void Record(string operation)
        {
            Events.Add(operation);
            if (FailOn == operation) throw new IOException("synthetic " + operation + " failure");
        }

        private sealed class FakeKey(FakeRunKeys owner) : IStartupRunKey
        {
            public object? GetValue(string name) { owner.Record("get:" + name); return owner.Values.GetValueOrDefault(name); }
            public void SetValue(string name, string value) { owner.Record("set:" + name); owner.Values[name] = value; }
            public void DeleteValue(string name) { owner.Record("delete:" + name); owner.Values.Remove(name); }
            public void Dispose() { owner.DisposeCount++; owner.Record("dispose"); }
        }
    }
}
