using DesktopOverlayBoard.Models;

namespace DesktopOverlayBoard.Services;

internal static class ConfigNormalizer
{
    public static void Normalize(AppConfig config, Func<string> createId)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(createId);
        config.UiLanguage = LocalizationService.NormalizeCode(config.UiLanguage);
        config.Boards ??= new();
        config.BoardWindows ??= new();
        config.OpenBoardWindowIds ??= new();
        config.SummaryWindow ??= WindowLayout.Default(420, 620, 0.78);
        config.Startup ??= new StartupOptions();

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
                board.Id = createId();
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
