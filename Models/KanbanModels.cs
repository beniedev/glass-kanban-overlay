namespace DesktopOverlayBoard.Models;

public sealed record KanbanDocument(
    string FilePath,
    string NewLine,
    bool EndsWithNewLine,
    string FullHash,
    IReadOnlyList<string> Lines,
    IReadOnlyList<KanbanColumn> Columns);

public sealed record KanbanColumn(
    string Title,
    int HeadingLineIndex,
    int ContentStartLine,
    int ContentEndLineExclusive,
    string RangeHash,
    IReadOnlyList<KanbanTask> Tasks);

public sealed record KanbanTask(
    string Id,
    string FilePath,
    string ColumnTitle,
    int LineIndex,
    string Prefix,
    string Text,
    string? BlockId,
    bool Done,
    string OriginalLine,
    string ColumnRangeHash);

public sealed class BoardGroup
{
    public required BoardConfig Board { get; init; }
    public required string ColumnTitle { get; init; }
    public required string ColumnRangeHash { get; init; }
    public List<KanbanTask> Tasks { get; init; } = new();
    public string? Error { get; init; }
}

public sealed class KanbanWriteResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static KanbanWriteResult Ok() => new() { Success = true };
    public static KanbanWriteResult Fail(string error) => new() { Success = false, Error = error };
}
