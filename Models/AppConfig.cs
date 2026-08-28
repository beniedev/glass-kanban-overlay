using System.Text.Encodings.Web;
using System.Text.Json;

namespace DesktopOverlayBoard.Models;

public sealed class AppConfig
{
    public string UiLanguage { get; set; } = "auto";
    public List<BoardConfig> Boards { get; set; } = new();
    public WindowLayout SummaryWindow { get; set; } = WindowLayout.Default(420, 620, 0.78);
    public Dictionary<string, WindowLayout> BoardWindows { get; set; } = new();
    public List<string> OpenBoardWindowIds { get; set; } = new();
    public StartupOptions Startup { get; set; } = new();

    public AppConfig Clone()
    {
        var json = JsonSerializer.Serialize(this, ConfigJson.Options);
        return JsonSerializer.Deserialize<AppConfig>(json, ConfigJson.Options) ?? new AppConfig();
    }
}

public sealed class StartupOptions
{
    public bool StartMinimizedToTray { get; set; }
    public bool StartWithWindows { get; set; }
}

public sealed class BoardConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string DisplayName { get; set; } = "";
    public string VaultName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string DefaultColumn { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string WidgetTitle { get; set; } = "";
    public string WidgetNote { get; set; } = "";
    public string WidgetTheme { get; set; } = "ink";
}

public sealed class WindowLayout
{
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 620;
    public double Opacity { get; set; } = 0.78;
    public bool AlwaysOnTop { get; set; } = true;
    public bool Locked { get; set; }
    public string PlacementMode { get; set; } = "topmost";

    public static WindowLayout Default(double width, double height, double opacity)
    {
        return new WindowLayout
        {
            Width = width,
            Height = height,
            Opacity = opacity,
            AlwaysOnTop = true,
            PlacementMode = "topmost",
        };
    }
}

public static class ConfigJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
