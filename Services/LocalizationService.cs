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
        "zh-Hant",
        "ja",
        "ko",
        "fr",
        "es",
        "ru",
        "ar",
    ];

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new(AutoCode, "Auto / Windows"),
        new("en", "English"),
        new("zh", "简体中文"),
        new("zh-Hant", "繁體中文"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("fr", "Français"),
        new("es", "Español"),
        new("ru", "Русский"),
        new("ar", "العربية"),
    ];

    public static string CurrentCode { get; private set; } = ResolveCode(AutoCode);
    public static bool IsRightToLeft => CurrentCode == "ar";

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
            return "zh-Hant";
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

        var culture = CultureInfo.CurrentUICulture.Name.ToLowerInvariant();
        if (culture is "zh-hant" or "zh-tw" or "zh-hk" or "zh-mo")
        {
            return "zh-Hant";
        }

        var neutral = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
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
        ["Action.SplitAll"] = "Split all",
        ["Action.ConfigureBoards"] = "Configure boards",
        ["Action.Settings"] = "Settings",
        ["Action.Refresh"] = "Refresh",
        ["Action.OpenFirstSource"] = "Open first source file",
        ["Action.OpenSource"] = "Open source Markdown",
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
            ("Action.SplitAll", "分窗全部"),
            ("Action.ConfigureBoards", "配置看板"),
            ("Action.Settings", "设置"),
            ("Action.Refresh", "刷新"),
            ("Action.OpenFirstSource", "打开第一个源文件"),
            ("Action.OpenSource", "打开源 Markdown"),
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
            ("Action.Know", "知道了"),
            ("Action.Source", "源"),
            ("Action.Add", "添加"),
            ("Action.Remove", "移除"),
            ("Action.ReselectColumn", "重新选择列"),
            ("Action.CreateMissingColumn", "创建缺失列"),
            ("Action.RemoveFromSummary", "从汇总移除"),
            ("Action.Theme", "外观"),
            ("Action.WindowMode", "窗口模式"),
            ("Label.Opacity", "透明度"),
            ("Label.Pinned", "置顶"),
            ("Label.Normal", "普通"),
            ("Label.Desktop", "桌面"),
            ("Label.DisplayName", "显示名"),
            ("Label.DefaultColumn", "默认列"),
            ("Label.EnableBoard", "启用这个看板"),
            ("Label.StartMinimized", "启动时默认最小化到托盘"),
            ("Label.StartWithWindows", "开机自启动"),
            ("Label.Language", "语言"),
            ("Label.NoOpenTasks", "没有未完成任务"),
            ("Empty.NoBoards", "还没有看板"),
            ("Empty.AddBoardPrompt", "添加一个 Markdown 看板文件开始使用。"),
            ("Status.Ready", "准备中"),
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
            ("Dialog.RejectAdd", "拒绝添加"),
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
        ["zh-Hant"] = WithEnglish(
            ("App.Name", "透明看板"),
            ("App.Title", "透明看板"),
            ("App.Board", "看板"),
            ("Action.ShowSummary", "顯示彙總窗"),
            ("Action.SplitToDesktop", "分窗到桌面"),
            ("Action.SplitAll", "全部分窗"),
            ("Action.ConfigureBoards", "設定看板"),
            ("Action.Settings", "設定"),
            ("Action.Refresh", "重新整理"),
            ("Action.OpenFirstSource", "開啟第一個來源檔案"),
            ("Action.OpenSource", "開啟來源 Markdown"),
            ("Action.HideToTray", "隱藏到系統匣"),
            ("Action.Exit", "退出透明看板"),
            ("Action.Topmost", "永遠置頂"),
            ("Action.NormalWindow", "普通視窗"),
            ("Action.DesktopWidget", "停在桌面"),
            ("Action.LockPosition", "鎖定位置"),
            ("Action.OpenAsWindow", "開啟為分窗"),
            ("Action.CloseWindow", "關閉此視窗"),
            ("Action.AddBoard", "新增看板"),
            ("Action.AddCard", "+ 新增卡片"),
            ("Action.NewTask", "新增任務"),
            ("Action.EditCard", "編輯卡片"),
            ("Action.MoveTop", "置頂"),
            ("Action.Archive", "封存"),
            ("Action.Delete", "刪除"),
            ("Action.Save", "儲存"),
            ("Action.Cancel", "取消"),
            ("Action.Ok", "確定"),
            ("Action.Know", "知道了"),
            ("Action.Add", "新增"),
            ("Action.Remove", "移除"),
            ("Action.Theme", "外觀"),
            ("Action.WindowMode", "視窗模式"),
            ("Label.Opacity", "透明度"),
            ("Label.Pinned", "置頂"),
            ("Label.Normal", "普通"),
            ("Label.Desktop", "桌面"),
            ("Label.DisplayName", "顯示名稱"),
            ("Label.DefaultColumn", "預設欄"),
            ("Label.EnableBoard", "啟用這個看板"),
            ("Label.StartMinimized", "啟動時最小化到系統匣"),
            ("Label.StartWithWindows", "開機自動啟動"),
            ("Label.Language", "語言"),
            ("Empty.NoBoards", "尚未新增看板"),
            ("Empty.AddBoardPrompt", "新增一個 Markdown 看板檔案即可開始。"),
            ("Status.Refreshing", "重新整理中..."),
            ("Status.RefreshedAt", "已重新整理 {0:HH:mm:ss}"),
            ("Dialog.SettingsTitle", "透明看板設定"),
            ("Dialog.SelectColumnTitle", "選擇欄位"),
            ("Dialog.SelectColumnPrompt", "選擇預設顯示的 Kanban 欄"),
            ("Dialog.EditTask", "編輯任務"),
            ("Dialog.EditWindowTitle", "編輯視窗標題"),
            ("Dialog.EditWindowNote", "編輯備註"),
            ("Dialog.DeleteTaskPrompt", "刪除這條任務？"),
            ("Message.EmptyTask", "任務文字不能為空。"),
            ("Message.BlockedPath", "這個路徑像封存或備份檔，預設拒絕新增。"),
            ("FileDialog.SelectBoard", "選擇 Kanban / Markdown 看板"),
            ("Error.SourceChanged", "來源檔案已變更，已拒絕覆蓋。"),
            ("Error.SingleLineTask", "任務文字必須保持為單行。"),
            ("Error.WriteBusy", "另一個透明看板程序正在寫入此檔案，請重試。"),
            ("Error.WriteFailed", "寫入失敗：{0}")),
        ["ja"] = WithEnglish(
            ("App.Name", "Glass Kanban"),
            ("App.Board", "ボード"),
            ("Action.ShowSummary", "サマリーを表示"),
            ("Action.SplitToDesktop", "デスクトップに分割"),
            ("Action.SplitAll", "すべて分割"),
            ("Action.ConfigureBoards", "ボード設定"),
            ("Action.Settings", "設定"),
            ("Action.Refresh", "更新"),
            ("Action.OpenFirstSource", "最初のソースを開く"),
            ("Action.OpenSource", "ソース Markdown を開く"),
            ("Action.HideToTray", "トレイに隠す"),
            ("Action.Exit", "終了"),
            ("Action.Topmost", "常に手前"),
            ("Action.NormalWindow", "通常ウィンドウ"),
            ("Action.DesktopWidget", "デスクトップに固定"),
            ("Action.LockPosition", "位置をロック"),
            ("Action.OpenAsWindow", "ウィジェットで開く"),
            ("Action.CloseWindow", "このウィンドウを閉じる"),
            ("Action.AddBoard", "ボードを追加"),
            ("Action.AddCard", "+ カードを追加"),
            ("Action.NewTask", "新しいタスク"),
            ("Action.EditCard", "カードを編集"),
            ("Action.MoveTop", "一番上へ"),
            ("Action.Archive", "アーカイブ"),
            ("Action.Delete", "削除"),
            ("Action.Save", "保存"),
            ("Action.Cancel", "キャンセル"),
            ("Action.Ok", "OK"),
            ("Action.Know", "了解"),
            ("Action.Add", "追加"),
            ("Action.Remove", "削除"),
            ("Action.Theme", "外観"),
            ("Action.WindowMode", "ウィンドウモード"),
            ("Label.Opacity", "透明度"),
            ("Label.Pinned", "固定"),
            ("Label.Normal", "通常"),
            ("Label.Desktop", "デスクトップ"),
            ("Label.DisplayName", "表示名"),
            ("Label.DefaultColumn", "既定の列"),
            ("Label.EnableBoard", "このボードを有効にする"),
            ("Label.StartMinimized", "起動時にトレイへ最小化"),
            ("Label.StartWithWindows", "Windows と同時に起動"),
            ("Label.Language", "言語"),
            ("Empty.NoBoards", "ボードがありません"),
            ("Empty.AddBoardPrompt", "Markdown ボードファイルを追加してください。"),
            ("Status.Refreshing", "更新中..."),
            ("Status.RefreshedAt", "更新済み {0:HH:mm:ss}"),
            ("Dialog.SettingsTitle", "Glass Kanban 設定"),
            ("Dialog.SelectColumnTitle", "列を選択"),
            ("Dialog.SelectColumnPrompt", "既定で表示する Kanban 列を選択"),
            ("Dialog.EditTask", "タスクを編集"),
            ("Dialog.DeleteTaskPrompt", "このタスクを削除しますか？"),
            ("Message.EmptyTask", "タスク本文は空にできません。"),
            ("FileDialog.SelectBoard", "Kanban / Markdown ボードを選択"),
            ("Error.SourceChanged", "ソースファイルが変更されたため、上書きを拒否しました。")),
        ["ko"] = WithEnglish(
            ("App.Name", "Glass Kanban"),
            ("App.Board", "보드"),
            ("Action.ShowSummary", "요약 창 보기"),
            ("Action.SplitToDesktop", "데스크톱으로 분리"),
            ("Action.SplitAll", "모두 분리"),
            ("Action.ConfigureBoards", "보드 설정"),
            ("Action.Settings", "설정"),
            ("Action.Refresh", "새로고침"),
            ("Action.OpenFirstSource", "첫 원본 파일 열기"),
            ("Action.OpenSource", "원본 Markdown 열기"),
            ("Action.HideToTray", "트레이로 숨기기"),
            ("Action.Exit", "종료"),
            ("Action.Topmost", "항상 위"),
            ("Action.NormalWindow", "일반 창"),
            ("Action.DesktopWidget", "데스크톱에 두기"),
            ("Action.LockPosition", "위치 잠금"),
            ("Action.OpenAsWindow", "위젯으로 열기"),
            ("Action.CloseWindow", "이 창 닫기"),
            ("Action.AddBoard", "보드 추가"),
            ("Action.AddCard", "+ 카드 추가"),
            ("Action.NewTask", "새 작업"),
            ("Action.EditCard", "카드 편집"),
            ("Action.MoveTop", "맨 위로"),
            ("Action.Archive", "보관"),
            ("Action.Delete", "삭제"),
            ("Action.Save", "저장"),
            ("Action.Cancel", "취소"),
            ("Action.Ok", "확인"),
            ("Action.Know", "알겠습니다"),
            ("Action.Add", "추가"),
            ("Action.Remove", "제거"),
            ("Action.Theme", "모양"),
            ("Action.WindowMode", "창 모드"),
            ("Label.Opacity", "투명도"),
            ("Label.Pinned", "고정"),
            ("Label.Normal", "일반"),
            ("Label.Desktop", "데스크톱"),
            ("Label.DisplayName", "표시 이름"),
            ("Label.DefaultColumn", "기본 열"),
            ("Label.EnableBoard", "이 보드 사용"),
            ("Label.StartMinimized", "시작 시 트레이로 최소화"),
            ("Label.StartWithWindows", "Windows 시작 시 실행"),
            ("Label.Language", "언어"),
            ("Empty.NoBoards", "아직 보드가 없습니다"),
            ("Empty.AddBoardPrompt", "Markdown 보드 파일을 추가하세요."),
            ("Status.Refreshing", "새로고침 중..."),
            ("Status.RefreshedAt", "새로고침됨 {0:HH:mm:ss}"),
            ("Dialog.SettingsTitle", "Glass Kanban 설정"),
            ("Dialog.SelectColumnTitle", "열 선택"),
            ("Dialog.SelectColumnPrompt", "기본으로 표시할 Kanban 열 선택"),
            ("Dialog.EditTask", "작업 편집"),
            ("Dialog.DeleteTaskPrompt", "이 작업을 삭제할까요?"),
            ("Message.EmptyTask", "작업 내용은 비워둘 수 없습니다."),
            ("FileDialog.SelectBoard", "Kanban / Markdown 보드 선택"),
            ("Error.SourceChanged", "원본 파일이 변경되어 덮어쓰기를 거부했습니다.")),
        ["fr"] = WithEnglish(
            ("App.Name", "Kanban Verre"),
            ("App.Title", "Kanban Verre"),
            ("App.Board", "Tableau"),
            ("Action.Settings", "Paramètres"),
            ("Action.Refresh", "Actualiser"),
            ("Action.AddBoard", "Ajouter un tableau"),
            ("Action.AddCard", "+ Ajouter une carte"),
            ("Action.Save", "Enregistrer"),
            ("Action.Cancel", "Annuler"),
            ("Action.Delete", "Supprimer"),
            ("Action.Archive", "Archiver"),
            ("Action.Theme", "Apparence"),
            ("Label.Language", "Langue"),
            ("Empty.NoBoards", "Aucun tableau"),
            ("Empty.AddBoardPrompt", "Ajoutez un fichier Markdown de tableau pour commencer."),
            ("Dialog.SettingsTitle", "Paramètres de Kanban Verre"),
            ("Dialog.DeleteTaskPrompt", "Supprimer cette tâche ?"),
            ("Message.EmptyTask", "Le texte de la tâche ne peut pas être vide.")),
        ["es"] = WithEnglish(
            ("App.Name", "Kanban Cristal"),
            ("App.Title", "Kanban Cristal"),
            ("App.Board", "Tablero"),
            ("Action.Settings", "Ajustes"),
            ("Action.Refresh", "Actualizar"),
            ("Action.AddBoard", "Añadir tablero"),
            ("Action.AddCard", "+ Añadir tarjeta"),
            ("Action.Save", "Guardar"),
            ("Action.Cancel", "Cancelar"),
            ("Action.Delete", "Eliminar"),
            ("Action.Archive", "Archivar"),
            ("Action.Theme", "Apariencia"),
            ("Label.Language", "Idioma"),
            ("Empty.NoBoards", "No hay tableros"),
            ("Empty.AddBoardPrompt", "Añade un archivo Markdown de tablero para empezar."),
            ("Dialog.SettingsTitle", "Ajustes de Kanban Cristal"),
            ("Dialog.DeleteTaskPrompt", "¿Eliminar esta tarea?"),
            ("Message.EmptyTask", "El texto de la tarea no puede estar vacío.")),
        ["ru"] = WithEnglish(
            ("App.Name", "Стеклянный Kanban"),
            ("App.Title", "Стеклянный Kanban"),
            ("App.Board", "Доска"),
            ("Action.Settings", "Настройки"),
            ("Action.Refresh", "Обновить"),
            ("Action.AddBoard", "Добавить доску"),
            ("Action.AddCard", "+ Добавить карточку"),
            ("Action.Save", "Сохранить"),
            ("Action.Cancel", "Отмена"),
            ("Action.Delete", "Удалить"),
            ("Action.Archive", "Архивировать"),
            ("Action.Theme", "Вид"),
            ("Label.Language", "Язык"),
            ("Empty.NoBoards", "Досок пока нет"),
            ("Empty.AddBoardPrompt", "Добавьте Markdown-файл доски, чтобы начать."),
            ("Dialog.SettingsTitle", "Настройки"),
            ("Dialog.DeleteTaskPrompt", "Удалить эту задачу?"),
            ("Message.EmptyTask", "Текст задачи не может быть пустым.")),
        ["ar"] = WithEnglish(
            ("App.Name", "كانبان زجاجي"),
            ("App.Title", "كانبان زجاجي"),
            ("App.Board", "لوحة"),
            ("Action.Settings", "الإعدادات"),
            ("Action.Refresh", "تحديث"),
            ("Action.AddBoard", "إضافة لوحة"),
            ("Action.AddCard", "+ إضافة بطاقة"),
            ("Action.Save", "حفظ"),
            ("Action.Cancel", "إلغاء"),
            ("Action.Delete", "حذف"),
            ("Action.Archive", "أرشفة"),
            ("Action.Theme", "المظهر"),
            ("Label.Language", "اللغة"),
            ("Empty.NoBoards", "لا توجد لوحات بعد"),
            ("Empty.AddBoardPrompt", "أضف ملف لوحة Markdown للبدء."),
            ("Dialog.SettingsTitle", "إعدادات كانبان زجاجي"),
            ("Dialog.DeleteTaskPrompt", "حذف هذه المهمة؟"),
            ("Message.EmptyTask", "لا يمكن أن يكون نص المهمة فارغا.")),
    };
}
