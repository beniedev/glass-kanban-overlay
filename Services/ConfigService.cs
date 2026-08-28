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

    private static void EnsureDefaults(AppConfig config)
    {
        config.UiLanguage = LocalizationService.NormalizeCode(config.UiLanguage);
        config.Boards ??= new();
        config.BoardWindows ??= new();
        config.OpenBoardWindowIds ??= new();

        var existingBoardIds = new HashSet<string>(
            config.Boards
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .Select(x => x.Id),
            StringComparer.OrdinalIgnoreCase);

        if (config.OpenBoardWindowIds.Count == 0 && config.BoardWindows.Count > 0)
        {
            config.OpenBoardWindowIds = config.BoardWindows.Keys
                .Where(existingBoardIds.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            config.OpenBoardWindowIds = config.OpenBoardWindowIds
                .Where(existingBoardIds.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
