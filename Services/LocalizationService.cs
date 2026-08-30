using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DesktopOverlayBoard.Services;

public sealed record LanguageOption(string Code, string DisplayName);

public static class LocalizationService
{
    public const string AutoCode = "auto";

    private static readonly string[] SupportedCodes =
    [
        "en",
        "zh",
    ];

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new(AutoCode, "Auto / 跟随 Windows"),
        new("en", "English"),
        new("zh", "简体中文"),
    ];

    public static string CurrentCode { get; private set; } = ResolveCode(AutoCode);
    public static bool IsRightToLeft => false;

    public static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return AutoCode;
        }

        var normalized = code.Trim().Replace('_', '-').ToLowerInvariant();
        if (normalized == AutoCode)
        {
            return AutoCode;
        }

        if (normalized is "zh-hant" or "zh-tw" or "zh-hk" or "zh-mo")
        {
            return "zh";
        }

        var neutral = normalized.Split('-')[0];
        return SupportedCodes.Contains(neutral, StringComparer.OrdinalIgnoreCase) ? neutral : AutoCode;
    }

    public static void Use(string? code)
    {
        CurrentCode = ResolveCode(NormalizeCode(code));
    }

    public static string Text(string key, params object?[] args)
    {
        var format = TryText(key, out var value) ? value : key;
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }

    public static bool TryText(string key, out string value)
    {
        if (Dictionaries.TryGetValue(CurrentCode, out var current) && current.TryGetValue(key, out value!))
        {
            return true;
        }

        if (Dictionaries["en"].TryGetValue(key, out value!))
        {
            return true;
        }

        value = "";
        return false;
    }

    public static void ApplyTo(DependencyObject root)
    {
        if (root is FrameworkElement rootElement)
        {
            rootElement.FlowDirection = IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            if (rootElement.ContextMenu != null)
            {
                ApplyTo(rootElement.ContextMenu);
            }
        }

        ApplyElement(root);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            ApplyTo(child);
        }

        if (root is ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items.OfType<DependencyObject>())
            {
                ApplyTo(item);
            }
        }
    }

    private static void ApplyElement(DependencyObject element)
    {
        if (element is FrameworkElement { Tag: string key } frameworkElement && TryText(key, out var text))
        {
            switch (element)
            {
                case Window window:
                    window.Title = text;
                    break;
                case TextBlock textBlock:
                    textBlock.Text = text;
                    break;
                case HeaderedItemsControl headered:
                    headered.Header = text;
                    break;
                case ContentControl contentControl:
                    contentControl.Content = text;
                    break;
            }

            if (TryText($"{key}.ToolTip", out var tooltip))
            {
                frameworkElement.ToolTip = tooltip;
            }
        }
    }

    private static string ResolveCode(string code)
    {
        if (code != AutoCode)
        {
            return code;
        }

        var neutral = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        if (neutral == "zh")
        {
            return "zh";
        }

        return SupportedCodes.Contains(neutral, StringComparer.OrdinalIgnoreCase) ? neutral : "en";
    }

    private static Dictionary<string, string> WithEnglish(params (string Key, string Value)[] overrides)
    {
        var result = new Dictionary<string, string>(English, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides)
        {
            result[key] = value;
        }

        return result;
    }

    private static readonly Dictionary<string, string> English = new(StringComparer.OrdinalIgnoreCase)
    {
        ["App.Name"] = "Glass Kanban",
        ["App.Title"] = "Glass Kanban",
        ["App.Board"] = "Board",
        ["Action.ShowSummary"] = "Show summary",
        ["Action.SplitToDesktop"] = "Split to desktop",
        ["Action.SplitAll"] = "Split all to desktop",
        ["Action.ConfigureBoards"] = "Configure boards",
        ["Action.Settings"] = "Settings",
        ["Action.Refresh"] = "Refresh",
        ["Action.OpenFirstSource"] = "Open first source file",
        ["Action.OpenSource"] = "Open source Markdown file",
        ["Action.BoardMenu"] = "Board actions",
        ["Action.ConfigureWindow"] = "Configure window",
        ["Action.RemoveBoard"] = "Remove board",
        ["Action.HideToTray"] = "Hide to tray",
        ["Action.Exit"] = "Exit Glass Kanban",
        ["Action.Topmost"] = "Always on top",
        ["Action.NormalWindow"] = "Normal window",
        ["Action.DesktopWidget"] = "Stay on desktop",
        ["Action.LockPosition"] = "Lock position",
        ["Action.UnlockPosition"] = "Unlock position",
        ["Action.OpenAsWindow"] = "Open as widget",
        ["Action.CloseWindow"] = "Close this window",
        ["Action.AddBoard"] = "Add board",
        ["Action.NewBoard"] = "New board",
        ["Action.AddExistingBoard"] = "Add existing",
        ["Action.AddCard"] = "+ Add card",
        ["Action.NewTask"] = "New task",
        ["Action.EditCard"] = "Edit card",
        ["Action.MoveTop"] = "Move to top",
        ["Action.Archive"] = "Archive",
        ["Action.Delete"] = "Delete",
        ["Action.Save"] = "Save",
        ["Action.Cancel"] = "Cancel",
        ["Action.Ok"] = "OK",
        ["Action.Close"] = "Close",
        ["Action.Know"] = "Got it",
        ["Action.Source"] = "Source",
        ["Action.Add"] = "Add",
        ["Action.Remove"] = "Remove",
        ["Action.ReselectColumn"] = "Choose another column",
        ["Action.CreateMissingColumn"] = "Create missing column",
        ["Action.RemoveFromSummary"] = "Remove from summary",
        ["Action.Theme"] = "Appearance",
        ["Action.WindowMode"] = "Window mode",
        ["Action.Ink"] = "Ink",
        ["Action.Blue"] = "Blue",
        ["Action.Green"] = "Green",
        ["Action.Plum"] = "Plum",
        ["Action.Amber"] = "Amber",
        ["Label.Opacity"] = "Opacity",
        ["Label.Pinned"] = "Pinned",
        ["Label.Normal"] = "Normal",
        ["Label.Desktop"] = "Desktop",
        ["Label.DisplayName"] = "Display name",
        ["Label.Vault"] = "Vault",
        ["Label.DefaultColumn"] = "Default column",
        ["Label.EnableBoard"] = "Enable this board",
        ["Label.StartMinimized"] = "Start minimized to tray",
        ["Label.StartWithWindows"] = "Start with Windows",
        ["Label.Language"] = "Language",
        ["Label.NoOpenTasks"] = "No open tasks",
        ["ToolTip.Close"] = "Close",
        ["ToolTip.EditTitle"] = "Double-click to edit title",
        ["ToolTip.EditNote"] = "Double-click to edit note",
        ["ToolTip.BoardActions"] = "Board actions",
        ["ToolTip.NewTask"] = "New task",
        ["Empty.NoBoards"] = "No boards yet",
        ["Empty.AddBoardPrompt"] = "Add a Markdown board file to begin.",
        ["Status.Ready"] = "Ready",
        ["Status.Refreshing"] = "Refreshing...",
        ["Status.RefreshedAt"] = "Refreshed {0:HH:mm:ss}",
        ["Status.Topmost"] = "Mode: always on top",
        ["Status.Normal"] = "Mode: normal window",
        ["Status.Desktop"] = "Mode: desktop widget, behind regular apps",
        ["Dialog.SettingsTitle"] = "Glass Kanban settings",
        ["Dialog.SelectColumnTitle"] = "Select column",
        ["Dialog.SelectColumnPrompt"] = "Select the default Kanban column",
        ["Dialog.TaskTitle"] = "Task",
        ["Dialog.EditTask"] = "Edit task",
        ["Dialog.EditWindowTitle"] = "Edit window title",
        ["Dialog.EditWindowNote"] = "Edit note",
        ["Dialog.DeleteCard"] = "Delete card",
        ["Dialog.DeleteTaskPrompt"] = "Delete this task?",
        ["Dialog.WriteFailed"] = "Write failed",
        ["Dialog.UpdateFailed"] = "Update failed",
        ["Dialog.RejectAdd"] = "Cannot add board",
        ["Dialog.AlreadyAdded"] = "Already added",
        ["Dialog.ReadFailed"] = "Read failed",
        ["Dialog.NoColumns"] = "No columns found",
        ["Dialog.NewBoard"] = "New Kanban board",
        ["Dialog.MissingColumn"] = "Recover missing column",
        ["Dialog.CreateMissingColumn"] = "Create missing column",
        ["Dialog.RemoveFromSummary"] = "Remove from summary",
        ["Message.SelectColumn"] = "Please select a column.",
        ["Message.EmptyTask"] = "Task text cannot be empty.",
        ["Message.BlockedPath"] = "This looks like an archive or backup path, so it is blocked by default.",
        ["Message.AlreadyAdded"] = "This board has already been added.",
        ["Message.ReadFailed"] = "Read failed: {0}",
        ["Message.NoColumns"] = "No `## Column name` headings were found in this file.",
        ["Message.ChooseBoardTemplate"] = "Choose a template for this new board.",
        ["Message.ReselectColumnPrompt"] = "Choose an existing column for this board.",
        ["Message.CreateMissingColumnPrompt"] = "Create the missing column \u201c{0}\u201d in the source file?",
        ["Message.RemoveFromSummaryPrompt"] = "Remove \u201c{0}\u201d from the summary? The source file will not be deleted or changed.",
        ["FileDialog.SelectBoard"] = "Select a Kanban / Markdown board",
        ["FileDialog.CreateBoard"] = "Create a Kanban / Markdown board",
        ["Error.ColumnMissing"] = "Column not found: {0}",
        ["Error.ColumnAlreadyExists"] = "Column already exists: {0}",
        ["Error.InvalidColumn"] = "Column title must be a non-empty single line.",
        ["Error.FileExists"] = "A file already exists at that path; refusing to overwrite it.",
        ["Error.InvalidBoardPath"] = "Choose a writable .md file in an existing folder.",
        ["Error.EmptyTask"] = "Task text cannot be empty.",
        ["Error.SourceChanged"] = "The source file changed; refusing to overwrite.",
        ["Error.SameColumnOnly"] = "Only same-column reordering is supported.",
        ["Error.BlockedWritePath"] = "This path looks like an archive or backup file; refusing to write.",
        ["Error.ColumnChanged"] = "The current column changed; refusing to overwrite.",
        ["Error.SingleLineTask"] = "Task text must stay on one line.",
        ["Error.WriteBusy"] = "Another Glass Kanban process is writing this board; please retry.",
        ["Error.WriteFailed"] = "Write failed: {0}",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Dictionaries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = English,
        ["zh"] = WithEnglish(
            ("App.Name", "透明看板"),
            ("App.Title", "透明看板"),
            ("App.Board", "看板"),
            ("Action.ShowSummary", "显示汇总窗"),
            ("Action.SplitToDesktop", "分窗到桌面"),
            ("Action.SplitAll", "全部分窗到桌面"),
            ("Action.ConfigureBoards", "配置看板"),
            ("Action.Settings", "设置"),
            ("Action.Refresh", "刷新"),
            ("Action.OpenFirstSource", "打开第一个源文件"),
            ("Action.OpenSource", "打开原 Markdown 文件"),
            ("Action.BoardMenu", "看板操作"),
            ("Action.ConfigureWindow", "配置窗口"),
            ("Action.RemoveBoard", "移除看板"),
            ("Action.HideToTray", "隐藏到托盘"),
            ("Action.Exit", "退出透明看板"),
            ("Action.Topmost", "悬浮在顶端"),
            ("Action.NormalWindow", "普通窗口"),
            ("Action.DesktopWidget", "停在桌面"),
            ("Action.LockPosition", "锁定位置"),
            ("Action.UnlockPosition", "解锁位置"),
            ("Action.OpenAsWindow", "打开为分窗"),
            ("Action.CloseWindow", "关闭此窗口"),
            ("Action.AddBoard", "添加看板"),
            ("Action.NewBoard", "新建看板"),
            ("Action.AddExistingBoard", "添加现有看板"),
            ("Action.AddCard", "+ 添加卡片"),
            ("Action.NewTask", "新增任务"),
            ("Action.EditCard", "编辑卡片"),
            ("Action.MoveTop", "置顶"),
            ("Action.Archive", "归档"),
            ("Action.Delete", "删除"),
            ("Action.Save", "保存"),
            ("Action.Cancel", "取消"),
            ("Action.Ok", "确定"),
            ("Action.Close", "关闭"),
            ("Action.Know", "知道了"),
            ("Action.Source", "源文件"),
            ("Action.Add", "添加"),
            ("Action.Remove", "移除"),
            ("Action.ReselectColumn", "重新选择列"),
            ("Action.CreateMissingColumn", "创建缺失列"),
            ("Action.RemoveFromSummary", "从汇总移除"),
            ("Action.Theme", "外观"),
            ("Action.WindowMode", "窗口模式"),
            ("Action.Ink", "墨黑"),
            ("Action.Blue", "蓝色"),
            ("Action.Green", "绿色"),
            ("Action.Plum", "梅紫"),
            ("Action.Amber", "琥珀"),
            ("Label.Opacity", "透明度"),
            ("Label.Pinned", "置顶"),
            ("Label.Normal", "普通"),
            ("Label.Desktop", "桌面"),
            ("Label.DisplayName", "显示名"),
            ("Label.Vault", "仓库"),
            ("Label.DefaultColumn", "默认列"),
            ("Label.EnableBoard", "启用这个看板"),
            ("Label.StartMinimized", "启动时默认最小化到托盘"),
            ("Label.StartWithWindows", "开机自启动"),
            ("Label.Language", "语言"),
            ("Label.NoOpenTasks", "没有未完成任务"),
            ("ToolTip.Close", "关闭"),
            ("ToolTip.EditTitle", "双击编辑标题"),
            ("ToolTip.EditNote", "双击编辑说明"),
            ("ToolTip.BoardActions", "看板操作"),
            ("ToolTip.NewTask", "新增任务"),
            ("Empty.NoBoards", "还没有看板"),
            ("Empty.AddBoardPrompt", "添加一个 Markdown 看板文件开始使用。"),
            ("Status.Ready", "就绪"),
            ("Status.Refreshing", "刷新中..."),
            ("Status.RefreshedAt", "已刷新 {0:HH:mm:ss}"),
            ("Status.Topmost", "模式：悬浮在顶端"),
            ("Status.Normal", "模式：普通窗口"),
            ("Status.Desktop", "模式：停在桌面，不压住其他软件"),
            ("Dialog.SettingsTitle", "透明看板设置"),
            ("Dialog.SelectColumnTitle", "选择列"),
            ("Dialog.SelectColumnPrompt", "选择默认显示的 Kanban 列"),
            ("Dialog.TaskTitle", "任务"),
            ("Dialog.EditTask", "编辑任务"),
            ("Dialog.EditWindowTitle", "编辑窗口标题"),
            ("Dialog.EditWindowNote", "编辑备注"),
            ("Dialog.DeleteCard", "删除卡片"),
            ("Dialog.DeleteTaskPrompt", "删除这条任务？"),
            ("Dialog.WriteFailed", "写入失败"),
            ("Dialog.UpdateFailed", "更新失败"),
            ("Dialog.RejectAdd", "无法添加看板"),
            ("Dialog.AlreadyAdded", "已添加"),
            ("Dialog.ReadFailed", "读取失败"),
            ("Dialog.NoColumns", "没有找到列"),
            ("Dialog.NewBoard", "新建 Kanban 看板"),
            ("Dialog.MissingColumn", "恢复缺失列"),
            ("Dialog.CreateMissingColumn", "创建缺失列"),
            ("Dialog.RemoveFromSummary", "从汇总移除"),
            ("Message.SelectColumn", "请选择一个列。"),
            ("Message.EmptyTask", "任务文字不能为空。"),
            ("Message.BlockedPath", "这个路径像归档或备份文件，默认拒绝添加。"),
            ("Message.AlreadyAdded", "这个看板已经添加过了。"),
            ("Message.ReadFailed", "读取失败：{0}"),
            ("Message.NoColumns", "这个文件里没有找到 `## 列名`。"),
            ("Message.ChooseBoardTemplate", "请选择新看板模板。"),
            ("Message.ReselectColumnPrompt", "请为这个看板选择一个现有列。"),
            ("Message.CreateMissingColumnPrompt", "要在源文件中创建缺失列“{0}”吗？"),
            ("Message.RemoveFromSummaryPrompt", "要从汇总中移除“{0}”吗？不会删除或修改源文件。"),
            ("FileDialog.SelectBoard", "选择 Kanban / Markdown 看板"),
            ("FileDialog.CreateBoard", "创建 Kanban / Markdown 看板"),
            ("Error.ColumnMissing", "列不存在：{0}"),
            ("Error.ColumnAlreadyExists", "列已经存在：{0}"),
            ("Error.InvalidColumn", "列标题不能为空，且必须保持为单行。"),
            ("Error.FileExists", "该路径已有文件，已拒绝覆盖。"),
            ("Error.InvalidBoardPath", "请选择已有文件夹中的可写 .md 文件。"),
            ("Error.EmptyTask", "任务文字不能为空。"),
            ("Error.SourceChanged", "源文件已经变化，已拒绝覆盖。"),
            ("Error.SameColumnOnly", "只支持同一列内调整顺序。"),
            ("Error.BlockedWritePath", "该路径像归档/备份文件，已拒绝写入。"),
            ("Error.ColumnChanged", "源文件当前列已经变化，已拒绝覆盖。"),
            ("Error.SingleLineTask", "任务文字必须保持为单行。"),
            ("Error.WriteBusy", "另一个透明看板进程正在写入该文件，请重试。"),
            ("Error.WriteFailed", "写入失败：{0}")),
    };
}
