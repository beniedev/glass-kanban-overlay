using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using DesktopOverlayBoard.Models;

namespace DesktopOverlayBoard.Services;

public sealed partial class MarkdownKanbanService
{
    private static string T(string key, params object?[] args) => LocalizationService.Text(key, args);

    private static readonly string[] BlockedPathTokens =
    [
        "归档",
        "Archive",
        "archive",
        "backup",
        "backups",
        "备份",
        "_任务备份",
    ];

    public static bool IsBlockedPath(string path)
    {
        return BlockedPathTokens.Any(token => path.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    public KanbanDocument Parse(string filePath)
    {
        var text = File.Exists(filePath) ? File.ReadAllText(filePath, Encoding.UTF8) : "";
        return ParseText(filePath, text);
    }

    private static KanbanDocument ParseText(string filePath, string text)
    {
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsWithNewLine = text.EndsWith("\n", StringComparison.Ordinal) || text.EndsWith("\r", StringComparison.Ordinal);
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();
        if (endsWithNewLine && lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var columns = ParseColumns(filePath, lines);
        return new KanbanDocument(
            filePath,
            newLine,
            endsWithNewLine,
            Sha256(text),
            lines,
            columns);
    }

    public IReadOnlyList<string> GetColumnTitles(string filePath)
    {
        return Parse(filePath).Columns.Select(x => x.Title).ToList();
    }

    public BoardGroup LoadGroup(BoardConfig board, bool incompleteOnly)
    {
        try
        {
            var document = Parse(board.FilePath);
            var column = FindColumn(document, board.DefaultColumn);
            if (column is null)
            {
                return new BoardGroup
                {
                    Board = board,
                    ColumnTitle = board.DefaultColumn,
                    ColumnRangeHash = "",
                    Error = T("Error.ColumnMissing", board.DefaultColumn),
                };
            }

            var tasks = incompleteOnly
                ? column.Tasks.Where(x => !x.Done).ToList()
                : column.Tasks.ToList();

            return new BoardGroup
            {
                Board = board,
                ColumnTitle = column.Title,
                ColumnRangeHash = column.RangeHash,
                Tasks = tasks,
            };
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"Load board failed: {board.FilePath}");
            return new BoardGroup
            {
                Board = board,
                ColumnTitle = board.DefaultColumn,
                ColumnRangeHash = "",
                Error = ex.Message,
            };
        }
    }

    public KanbanWriteResult ToggleTask(KanbanTask task, bool done)
    {
        return PatchTaskLine(task, line =>
        {
            var match = TaskLineRegex().Match(line);
            if (!match.Success)
            {
                return line;
            }

            var state = done ? "x" : " ";
            return $"{match.Groups["prefix"].Value}[{state}]{match.Groups["after"].Value}{match.Groups["body"].Value}";
        });
    }

    public KanbanWriteResult RenameTask(KanbanTask task, string newText)
    {
        newText = newText.Trim();
        if (string.IsNullOrWhiteSpace(newText))
        {
            return KanbanWriteResult.Fail(T("Error.EmptyTask"));
        }

        if (ContainsLineBreak(newText))
        {
            return KanbanWriteResult.Fail(T("Error.SingleLineTask"));
        }

        return PatchTaskLine(task, line =>
        {
            var match = TaskLineRegex().Match(line);
            if (!match.Success)
            {
                return line;
            }

            var blockId = ExtractBlockId(match.Groups["body"].Value, out _);
            var suffix = string.IsNullOrEmpty(blockId) ? "" : $" {blockId}";
            return $"{match.Groups["prefix"].Value}[{match.Groups["state"].Value}]{match.Groups["after"].Value}{newText}{suffix}";
        });
    }

    public KanbanWriteResult DeleteTask(KanbanTask task)
    {
        return PatchDocument(task.FilePath, task.ColumnTitle, task.ColumnRangeHash, lines =>
        {
            if (!IsValidLine(lines, task.LineIndex) || lines[task.LineIndex] != task.OriginalLine)
            {
                return KanbanWriteResult.Fail(T("Error.SourceChanged"));
            }

            lines.RemoveAt(task.LineIndex);
            return KanbanWriteResult.Ok();
        });
    }

    public KanbanWriteResult ArchiveTask(KanbanTask task)
    {
        return PatchDocument(task.FilePath, task.ColumnTitle, task.ColumnRangeHash, lines =>
        {
            if (!IsValidLine(lines, task.LineIndex) || lines[task.LineIndex] != task.OriginalLine)
            {
                return KanbanWriteResult.Fail(T("Error.SourceChanged"));
            }

            var settings = ReadKanbanSettings(lines);
            var archiveLine = BuildArchivedTaskLine(task.OriginalLine, settings);
            var archive = FindArchiveSection(lines);
            var insertAt = EnsureArchiveSection(lines, archive);

            lines.RemoveAt(task.LineIndex);
            if (insertAt > task.LineIndex)
            {
                insertAt--;
            }

            lines.Insert(insertAt, archiveLine);
            ApplyArchiveLimit(lines, settings);
            return KanbanWriteResult.Ok();
        });
    }

    public KanbanWriteResult MoveTaskToTop(KanbanTask task)
    {
        return PatchDocument(task.FilePath, task.ColumnTitle, task.ColumnRangeHash, lines =>
        {
            if (!IsValidLine(lines, task.LineIndex) || lines[task.LineIndex] != task.OriginalLine)
            {
                return KanbanWriteResult.Fail(T("Error.SourceChanged"));
            }

            var document = BuildDocumentFromLines(task.FilePath, lines);
            var column = FindColumn(document, task.ColumnTitle);
            if (column is null)
            {
                return KanbanWriteResult.Fail(T("Error.ColumnMissing", task.ColumnTitle));
            }

            var target = GetFirstTaskLine(column);
            if (target < 0 || target == task.LineIndex)
            {
                return KanbanWriteResult.Ok();
            }

            var line = lines[task.LineIndex];
            lines.RemoveAt(task.LineIndex);
            if (target > task.LineIndex)
            {
                target--;
            }

            lines.Insert(target, line);
            return KanbanWriteResult.Ok();
        });
    }

    public KanbanWriteResult MoveTaskBefore(KanbanTask task, KanbanTask beforeTask)
    {
        if (!string.Equals(task.FilePath, beforeTask.FilePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(task.ColumnTitle, beforeTask.ColumnTitle, StringComparison.OrdinalIgnoreCase))
        {
            return KanbanWriteResult.Fail(T("Error.SameColumnOnly"));
        }

        return PatchDocument(task.FilePath, task.ColumnTitle, task.ColumnRangeHash, lines =>
        {
            if (!IsValidLine(lines, task.LineIndex) || lines[task.LineIndex] != task.OriginalLine ||
                !IsValidLine(lines, beforeTask.LineIndex) || lines[beforeTask.LineIndex] != beforeTask.OriginalLine)
            {
                return KanbanWriteResult.Fail(T("Error.SourceChanged"));
            }

            if (task.LineIndex == beforeTask.LineIndex)
            {
                return KanbanWriteResult.Ok();
            }

            var line = lines[task.LineIndex];
            var insertAt = beforeTask.LineIndex;
            lines.RemoveAt(task.LineIndex);
            if (insertAt > task.LineIndex)
            {
                insertAt--;
            }

            lines.Insert(insertAt, line);
            return KanbanWriteResult.Ok();
        });
    }

    public KanbanWriteResult MoveTaskAfter(KanbanTask task, KanbanTask afterTask)
    {
        if (!string.Equals(task.FilePath, afterTask.FilePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(task.ColumnTitle, afterTask.ColumnTitle, StringComparison.OrdinalIgnoreCase))
        {
            return KanbanWriteResult.Fail(T("Error.SameColumnOnly"));
        }

        return PatchDocument(task.FilePath, task.ColumnTitle, task.ColumnRangeHash, lines =>
        {
            if (!IsValidLine(lines, task.LineIndex) || lines[task.LineIndex] != task.OriginalLine ||
                !IsValidLine(lines, afterTask.LineIndex) || lines[afterTask.LineIndex] != afterTask.OriginalLine)
            {
                return KanbanWriteResult.Fail(T("Error.SourceChanged"));
            }

            if (task.LineIndex == afterTask.LineIndex)
            {
                return KanbanWriteResult.Ok();
            }

            var line = lines[task.LineIndex];
            var insertAt = afterTask.LineIndex + 1;
            lines.RemoveAt(task.LineIndex);
            if (insertAt > task.LineIndex)
            {
                insertAt--;
            }

            lines.Insert(insertAt, line);
            return KanbanWriteResult.Ok();
        });
    }

    public KanbanWriteResult AddTask(BoardConfig board, string columnTitle, string expectedColumnHash, string text)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return KanbanWriteResult.Fail(T("Error.EmptyTask"));
        }

        if (ContainsLineBreak(text))
        {
            return KanbanWriteResult.Fail(T("Error.SingleLineTask"));
        }

        return PatchDocument(board.FilePath, columnTitle, expectedColumnHash, lines =>
        {
            var document = BuildDocumentFromLines(board.FilePath, lines);
            var column = FindColumn(document, columnTitle);
            if (column is null)
            {
                return KanbanWriteResult.Fail(T("Error.ColumnMissing", columnTitle));
            }

            var insertAt = GetInsertLine(lines, column);
            lines.Insert(insertAt, $"- [ ] {text}");
            return KanbanWriteResult.Ok();
        });
    }

    public void OpenSource(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true,
        });
    }

    private KanbanWriteResult PatchTaskLine(KanbanTask task, Func<string, string> rewrite)
    {
        return PatchDocument(task.FilePath, task.ColumnTitle, task.ColumnRangeHash, lines =>
        {
            if (!IsValidLine(lines, task.LineIndex) || lines[task.LineIndex] != task.OriginalLine)
            {
                return KanbanWriteResult.Fail(T("Error.SourceChanged"));
            }

            lines[task.LineIndex] = rewrite(lines[task.LineIndex]);
            return KanbanWriteResult.Ok();
        });
    }

    private KanbanWriteResult PatchDocument(
        string filePath,
        string columnTitle,
        string expectedColumnHash,
        Func<List<string>, KanbanWriteResult> mutate)
    {
        if (IsBlockedPath(filePath))
        {
            return KanbanWriteResult.Fail(T("Error.BlockedWritePath"));
        }

        using var writeMutex = CreateWriteMutex(filePath);
        var lockTaken = false;
        try
        {
            try
            {
                lockTaken = writeMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }

            if (!lockTaken)
            {
                return KanbanWriteResult.Fail(T("Error.WriteBusy"));
            }

            using var source = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var document = ParseText(filePath, reader.ReadToEnd());
            var column = FindColumn(document, columnTitle);
            if (column is null)
            {
                return KanbanWriteResult.Fail(T("Error.ColumnMissing", columnTitle));
            }

            if (!string.Equals(column.RangeHash, expectedColumnHash, StringComparison.Ordinal))
            {
                return KanbanWriteResult.Fail(T("Error.ColumnChanged"));
            }

            var lines = document.Lines.ToList();
            var result = mutate(lines);
            if (!result.Success)
            {
                return result;
            }

            SaveLines(filePath, lines, document.NewLine, document.EndsWithNewLine);
            return KanbanWriteResult.Ok();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"Write board failed: {filePath}");
            return KanbanWriteResult.Fail(T("Error.WriteFailed", ex.Message));
        }
        finally
        {
            if (lockTaken)
            {
                writeMutex.ReleaseMutex();
            }
        }
    }

    private KanbanDocument BuildDocumentFromLines(string filePath, IReadOnlyList<string> lines)
    {
        var columns = ParseColumns(filePath, lines);
        return new KanbanDocument(filePath, "\n", true, "", lines, columns);
    }

    private static void SaveLines(string filePath, IReadOnlyList<string> lines, string newLine, bool endsWithNewLine)
    {
        var body = string.Join(newLine, lines);
        if (endsWithNewLine)
        {
            body += newLine;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath))
            ?? throw new IOException("The board path has no parent directory.");
        var temp = Path.Combine(directory, $".{Path.GetFileName(filePath)}.overlay-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true);
                writer.Write(body);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Replace(temp, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static Mutex CreateWriteMutex(string filePath)
    {
        var normalized = Path.GetFullPath(filePath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return new Mutex(initiallyOwned: false, $@"Local\GlassKanbanOverlay-{hash}");
    }

    private static bool ContainsLineBreak(string text)
    {
        return text.Contains('\r') || text.Contains('\n');
    }

    private static IReadOnlyList<KanbanColumn> ParseColumns(string filePath, IReadOnlyList<string> lines)
    {
        var headings = new List<(string Title, int Line, bool IsArchive)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var match = HeadingRegex().Match(lines[i]);
            if (match.Success)
            {
                var title = match.Groups["title"].Value.Trim();
                headings.Add((title, i, IsArchiveHeading(lines, i, title)));
            }
        }

        var columns = new List<KanbanColumn>();
        for (var i = 0; i < headings.Count; i++)
        {
            var title = headings[i].Title;
            var headingLine = headings[i].Line;
            if (headings[i].IsArchive)
            {
                continue;
            }

            var contentStart = headingLine + 1;
            var contentEnd = i + 1 < headings.Count ? headings[i + 1].Line : lines.Count;
            var rangeHash = HashRange(lines, headingLine, contentEnd);
            var tasks = ParseTasks(filePath, title, rangeHash, lines, contentStart, contentEnd);

            columns.Add(new KanbanColumn(
                title,
                headingLine,
                contentStart,
                contentEnd,
                rangeHash,
                tasks));
        }

        return columns;
    }

    private static Dictionary<string, JsonElement> ReadKanbanSettings(IReadOnlyList<string> lines)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Trim().Equals("%% kanban:settings", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fenceStart = -1;
            var fenceEnd = -1;
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (!lines[j].Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    continue;
                }

                if (fenceStart < 0)
                {
                    fenceStart = j;
                }
                else
                {
                    fenceEnd = j;
                    break;
                }
            }

            if (fenceStart < 0 || fenceEnd <= fenceStart + 1)
            {
                return result;
            }

            var json = string.Join("\n", lines.Skip(fenceStart + 1).Take(fenceEnd - fenceStart - 1));
            try
            {
                using var document = JsonDocument.Parse(json);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    result[property.Name] = property.Value.Clone();
                }
            }
            catch (JsonException ex)
            {
                LogService.Error(ex, "Kanban settings parse failed");
            }

            return result;
        }

        return result;
    }

    private static string BuildArchivedTaskLine(string originalLine, IReadOnlyDictionary<string, JsonElement> settings)
    {
        if (!GetBoolSetting(settings, "archive-with-date"))
        {
            return originalLine;
        }

        var match = TaskLineRegex().Match(originalLine);
        if (!match.Success)
        {
            return originalLine;
        }

        var body = match.Groups["body"].Value;
        var blockId = ExtractBlockId(body, out var text);
        var timestamp = FormatArchiveTimestamp(GetStringSetting(settings, "archive-date-format") ?? "yyyy-MM-dd HH:mm");
        var separator = GetStringSetting(settings, "archive-date-separator");
        var appendDate = GetBoolSetting(settings, "append-archive-date");
        var titleParts = new List<string>();
        if (appendDate)
        {
            titleParts.Add(text);
            if (!string.IsNullOrWhiteSpace(separator))
            {
                titleParts.Add(separator);
            }

            titleParts.Add(timestamp);
        }
        else
        {
            titleParts.Add(timestamp);
            if (!string.IsNullOrWhiteSpace(separator))
            {
                titleParts.Add(separator);
            }

            titleParts.Add(text);
        }

        var suffix = string.IsNullOrWhiteSpace(blockId) ? "" : $" {blockId}";
        return $"{match.Groups["prefix"].Value}[{match.Groups["state"].Value}]{match.Groups["after"].Value}{string.Join(" ", titleParts)}{suffix}";
    }

    private static string FormatArchiveTimestamp(string momentFormat)
    {
        var dotNetFormat = ConvertMomentFormat(momentFormat);
        try
        {
            var formatted = DateTime.Now.ToString(dotNetFormat, CultureInfo.InvariantCulture);
            return momentFormat.Contains('a', StringComparison.Ordinal) && !momentFormat.Contains('A', StringComparison.Ordinal)
                ? formatted.ToLowerInvariant()
                : formatted;
        }
        catch (FormatException)
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
    }

    private static string ConvertMomentFormat(string momentFormat)
    {
        if (string.IsNullOrWhiteSpace(momentFormat))
        {
            return "yyyy-MM-dd HH:mm";
        }

        var replacements = new (string Moment, string DotNet)[]
        {
            ("YYYY", "yyyy"),
            ("YY", "yy"),
            ("DD", "dd"),
            ("D", "d"),
            ("dddd", "dddd"),
            ("ddd", "ddd"),
            ("MMMM", "MMMM"),
            ("MMM", "MMM"),
            ("MM", "MM"),
            ("M", "M"),
            ("HH", "HH"),
            ("H", "H"),
            ("hh", "hh"),
            ("h", "h"),
            ("mm", "mm"),
            ("m", "m"),
            ("ss", "ss"),
            ("s", "s"),
            ("A", "tt"),
            ("a", "tt"),
        };

        var result = momentFormat;
        foreach (var (moment, dotNet) in replacements)
        {
            result = result.Replace(moment, dotNet, StringComparison.Ordinal);
        }

        return result;
    }

    private static (int RuleLine, int HeadingLine, int ContentStart, int ContentEndExclusive)? FindArchiveSection(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var match = HeadingRegex().Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var title = match.Groups["title"].Value.Trim();
            if (!IsArchiveHeading(lines, i, title))
            {
                continue;
            }

            var contentEnd = lines.Count;
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (lines[j].Trim().Equals("%% kanban:settings", StringComparison.OrdinalIgnoreCase))
                {
                    contentEnd = j;
                    break;
                }
            }

            var ruleLine = i > 0 && lines[i - 1].Trim().Equals("***", StringComparison.Ordinal) ? i - 1 : i;
            return (ruleLine, i, i + 1, contentEnd);
        }

        return null;
    }

    private static int EnsureArchiveSection(List<string> lines, (int RuleLine, int HeadingLine, int ContentStart, int ContentEndExclusive)? archive)
    {
        if (archive is { } existing)
        {
            return GetArchiveInsertLine(lines, existing.ContentStart, existing.ContentEndExclusive);
        }

        var settingsStart = FindSettingsStart(lines);
        var insertAt = settingsStart >= 0 ? settingsStart : lines.Count;
        while (insertAt > 0 && string.IsNullOrWhiteSpace(lines[insertAt - 1]))
        {
            insertAt--;
        }

        var section = new[]
        {
            "",
            "",
            "***",
            "",
            "## Archive",
            "",
        };
        lines.InsertRange(insertAt, section);
        return insertAt + section.Length;
    }

    private static int GetArchiveInsertLine(IReadOnlyList<string> lines, int start, int endExclusive)
    {
        var insertAt = endExclusive;
        while (insertAt > start && string.IsNullOrWhiteSpace(lines[insertAt - 1]))
        {
            insertAt--;
        }

        return insertAt;
    }

    private static void ApplyArchiveLimit(List<string> lines, IReadOnlyDictionary<string, JsonElement> settings)
    {
        var limit = GetIntSetting(settings, "max-archive-size");
        if (limit is null || limit < 0)
        {
            return;
        }

        var archive = FindArchiveSection(lines);
        if (archive is null)
        {
            return;
        }

        var taskLines = new List<int>();
        for (var i = archive.Value.ContentStart; i < archive.Value.ContentEndExclusive; i++)
        {
            if (TaskLineRegex().IsMatch(lines[i]))
            {
                taskLines.Add(i);
            }
        }

        var removeCount = taskLines.Count - limit.Value;
        if (removeCount <= 0)
        {
            return;
        }

        foreach (var index in taskLines.Take(removeCount).Reverse())
        {
            lines.RemoveAt(index);
        }
    }

    private static int FindSettingsStart(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals("%% kanban:settings", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsArchiveHeading(IReadOnlyList<string> lines, int headingLine, string title)
    {
        if (!IsArchiveTitle(title) || headingLine <= 0)
        {
            return false;
        }

        var previous = headingLine - 1;
        while (previous >= 0 && string.IsNullOrWhiteSpace(lines[previous]))
        {
            previous--;
        }

        return previous >= 0 && lines[previous].Trim().Equals("***", StringComparison.Ordinal);
    }

    private static bool IsArchiveTitle(string title)
    {
        return title.Equals("Archive", StringComparison.OrdinalIgnoreCase) ||
               title.Equals("归档", StringComparison.OrdinalIgnoreCase);
    }

    private static bool GetBoolSetting(IReadOnlyDictionary<string, JsonElement> settings, string key)
    {
        return settings.TryGetValue(key, out var value) && value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false,
        };
    }

    private static int? GetIntSetting(IReadOnlyDictionary<string, JsonElement> settings, string key)
    {
        if (!settings.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
            ? number
            : null;
    }

    private static string? GetStringSetting(IReadOnlyDictionary<string, JsonElement> settings, string key)
    {
        return settings.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyList<KanbanTask> ParseTasks(
        string filePath,
        string columnTitle,
        string rangeHash,
        IReadOnlyList<string> lines,
        int start,
        int endExclusive)
    {
        var tasks = new List<KanbanTask>();
        for (var i = start; i < endExclusive; i++)
        {
            var match = TaskLineRegex().Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            var rawBody = match.Groups["body"].Value;
            var blockId = ExtractBlockId(rawBody, out var text);
            var id = $"{filePath}|{columnTitle}|{i}|{blockId ?? text}";

            tasks.Add(new KanbanTask(
                id,
                filePath,
                columnTitle,
                i,
                $"{match.Groups["prefix"].Value}[{match.Groups["state"].Value}]{match.Groups["after"].Value}",
                text,
                blockId,
                string.Equals(match.Groups["state"].Value, "x", StringComparison.OrdinalIgnoreCase),
                lines[i],
                rangeHash));
        }

        return tasks;
    }

    private static string? ExtractBlockId(string body, out string text)
    {
        var match = BlockIdRegex().Match(body);
        if (!match.Success)
        {
            text = body.Trim();
            return null;
        }

        text = body[..match.Index].TrimEnd();
        return match.Groups["block"].Value;
    }

    private static KanbanColumn? FindColumn(KanbanDocument document, string title)
    {
        return document.Columns.FirstOrDefault(x => string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetInsertLine(IReadOnlyList<string> lines, KanbanColumn column)
    {
        var insertAt = column.ContentEndLineExclusive;
        for (var i = column.ContentStartLine; i < column.ContentEndLineExclusive; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Equals("%% kanban:settings", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("***", StringComparison.Ordinal))
            {
                insertAt = i;
                break;
            }
        }

        while (insertAt > column.ContentStartLine && string.IsNullOrWhiteSpace(lines[insertAt - 1]))
        {
            insertAt--;
        }

        return insertAt;
    }

    private static int GetFirstTaskLine(KanbanColumn column)
    {
        return column.Tasks.Count == 0 ? -1 : column.Tasks.Min(x => x.LineIndex);
    }

    private static bool IsValidLine(IReadOnlyList<string> lines, int index)
    {
        return index >= 0 && index < lines.Count;
    }

    private static string HashRange(IReadOnlyList<string> lines, int start, int endExclusive)
    {
        return Sha256(string.Join("\n", lines.Skip(start).Take(endExclusive - start)));
    }

    private static string Sha256(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [GeneratedRegex(@"^\s*##\s+(?<title>.+?)\s*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(?<prefix>\s*-\s*)\[(?<state>[ xX])\](?<after>\s*)(?<body>.*)$")]
    private static partial Regex TaskLineRegex();

    [GeneratedRegex(@"\s(?<block>\^[A-Za-z0-9_-]+)\s*$")]
    private static partial Regex BlockIdRegex();
}
