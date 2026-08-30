using System.Text.Json;
using DesktopOverlayBoard.Models;

namespace DesktopOverlayBoard.Services;

public sealed class ConfigService
{
    public AppConfig Load()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        Directory.CreateDirectory(AppPaths.LogDirectory);

        if (!File.Exists(AppPaths.ConfigPath))
        {
            var config = CreateDefault();
            Save(config);
            return config;
        }

        var json = File.ReadAllText(AppPaths.ConfigPath);
        var loaded = JsonSerializer.Deserialize<AppConfig>(json, ConfigJson.Options) ?? CreateDefault();
        EnsureDefaults(loaded);
        Save(loaded);
        return loaded;
    }

    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        EnsureDefaults(config);
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(config, ConfigJson.Options);
        File.WriteAllText(AppPaths.ConfigPath, json);
    }

    public AppConfig CreateDefault()
    {
        var config = new AppConfig();
        EnsureDefaults(config);
        return config;
    }

    public bool RemoveBoardView(AppConfig config, string boardId)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(boardId))
        {
            return false;
        }

        config.Boards ??= new();
        config.OpenBoardWindowIds ??= new();
        config.BoardWindows ??= new();

        var removed = config.Boards.RemoveAll(board =>
            board is not null && string.Equals(board.Id, boardId, StringComparison.OrdinalIgnoreCase)) > 0;
        removed |= config.OpenBoardWindowIds.RemoveAll(id =>
            string.Equals(id, boardId, StringComparison.OrdinalIgnoreCase)) > 0;

        foreach (var key in config.BoardWindows.Keys
                     .Where(key => string.Equals(key, boardId, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            removed |= config.BoardWindows.Remove(key);
        }

        return removed;
    }

    private static void EnsureDefaults(AppConfig config)
    {
        config.UiLanguage = LocalizationService.NormalizeCode(config.UiLanguage);
        config.Boards ??= new();
        config.BoardWindows ??= new();
        config.OpenBoardWindowIds ??= new();
        config.SummaryWindow ??= WindowLayout.Default(420, 620, 0.78);

        config.Boards = config.Boards.Where(x => x is not null).ToList();
        var usedBoardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var board in config.Boards)
        {
            if (!string.IsNullOrWhiteSpace(board.Id) && usedBoardIds.Add(board.Id))
            {
                continue;
            }

            do
            {
                board.Id = Guid.NewGuid().ToString("n");
            }
            while (!usedBoardIds.Add(board.Id));
        }

        var enabledBoardIds = new HashSet<string>(
            config.Boards.Where(x => x.Enabled).Select(x => x.Id),
            StringComparer.OrdinalIgnoreCase);

        NormalizeLayout(config.SummaryWindow, 420, 620, 0.78);

        var boardWindows = new Dictionary<string, WindowLayout>(StringComparer.OrdinalIgnoreCase);
        foreach (var (boardId, layout) in config.BoardWindows)
        {
            if (string.IsNullOrWhiteSpace(boardId) || layout is null || !enabledBoardIds.Contains(boardId))
            {
                continue;
            }

            NormalizeLayout(layout, 380, 560, 0.76);
            boardWindows.TryAdd(boardId, layout);
        }

        config.BoardWindows = boardWindows;
        config.OpenBoardWindowIds = config.OpenBoardWindowIds
            .Where(enabledBoardIds.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void NormalizeLayout(WindowLayout layout, double defaultWidth, double defaultHeight, double defaultOpacity)
    {
        if (!double.IsFinite(layout.Left))
        {
            layout.Left = 80;
        }

        if (!double.IsFinite(layout.Top))
        {
            layout.Top = 80;
        }

        if (!double.IsFinite(layout.Width) || layout.Width <= 0)
        {
            layout.Width = defaultWidth;
        }

        if (!double.IsFinite(layout.Height) || layout.Height <= 0)
        {
            layout.Height = defaultHeight;
        }

        layout.Opacity = double.IsFinite(layout.Opacity)
            ? Math.Clamp(layout.Opacity, 0.2, 0.95)
            : defaultOpacity;

        if (layout.PlacementMode is not ("topmost" or "normal" or "desktop"))
        {
            layout.PlacementMode = layout.AlwaysOnTop ? "topmost" : "desktop";
        }
    }
}
