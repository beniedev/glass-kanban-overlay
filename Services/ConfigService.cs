using System.Text;
using System.Text.Json;
using DesktopOverlayBoard.Models;

namespace DesktopOverlayBoard.Services;

public sealed class ConfigService
{
    private readonly Action<string, string> _writeTemporary;
    private readonly Action<string, string, bool> _publishTemporary;
    public ResolvedAppPaths Paths { get; }

    public ConfigService(ResolvedAppPaths paths) : this(paths, WriteTemporaryFile, PublishTemporaryFile) { }

    internal ConfigService(ResolvedAppPaths paths,
        Action<string, string> writeTemporary, Action<string, string, bool> publishTemporary)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(writeTemporary);
        ArgumentNullException.ThrowIfNull(publishTemporary);
        Paths = paths;
        _writeTemporary = writeTemporary;
        _publishTemporary = publishTemporary;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Paths.DataDirectory);
        Directory.CreateDirectory(Paths.LogDirectory);
    }

    public AppConfig Load()
    {
        Initialize();
        string json;
        try
        {
            json = File.ReadAllText(Paths.ConfigPath);
        }
        catch (FileNotFoundException)
        {
            var config = CreateDefault();
            Save(config);
            return config;
        }

        // JSON null retains the existing default-and-persist behavior; parse/read failures propagate.
        var loaded = JsonSerializer.Deserialize<AppConfig>(json, ConfigJson.Options) ?? CreateDefault();
        ConfigNormalizer.Normalize(loaded, NewBoardId);
        Save(loaded);
        return loaded;
    }

    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ConfigNormalizer.Normalize(config, NewBoardId);
        var json = JsonSerializer.Serialize(config, ConfigJson.Options);
        Directory.CreateDirectory(Paths.DataDirectory);
        string? temporary = Path.Combine(Paths.DataDirectory,
            $".config.json.overlay-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
        try
        {
            _writeTemporary(temporary, json);
            _publishTemporary(temporary, Paths.ConfigPath, File.Exists(Paths.ConfigPath));
            temporary = null;
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception error) { LogService.Error(error, "Configuration temporary-file cleanup failed."); }
            }
        }
    }

    internal static void WriteTemporaryFile(string path, string json)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
        writer.Write(json);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    internal static void PublishTemporaryFile(string temporary, string destination, bool destinationExists)
    {
        if (destinationExists) File.Replace(temporary, destination, destinationBackupFileName: null);
        else File.Move(temporary, destination);
    }

    public AppConfig CreateDefault()
    {
        var config = new AppConfig();
        ConfigNormalizer.Normalize(config, NewBoardId);
        return config;
    }

    private static string NewBoardId() => Guid.NewGuid().ToString("n");

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

}
