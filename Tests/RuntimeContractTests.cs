using DesktopOverlayBoard.Models;
using DesktopOverlayBoard.Services;
using System.Windows;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class RuntimeContractTests
{
    public static void Run(string root)
    {
        TestSingleInstanceSignal();
        TestLocalization();
    }

    private static void TestSingleInstanceSignal()
    {
        var suffix = Guid.NewGuid().ToString("n");
        var firstActivated = new ManualResetEventSlim(false);
        using var first = new SingleInstanceService($"Local\\GlassKanbanOverlay.Tests.{suffix}", $"Local\\GlassKanbanOverlay.Tests.Activate.{suffix}");
        Assert(first.TryAcquire(firstActivated.Set), "first instance should acquire the mutex");

        using var secondCompleted = new ManualResetEventSlim(false);
        var secondRejected = false;
        var secondTask = Task.Run(() =>
        {
            using var second = new SingleInstanceService($"Local\\GlassKanbanOverlay.Tests.{suffix}", $"Local\\GlassKanbanOverlay.Tests.Activate.{suffix}");
            secondRejected = !second.TryAcquire(() => { });
            secondCompleted.Set();
        });
        Assert(secondCompleted.Wait(TimeSpan.FromSeconds(2)), "second instance attempt should complete");
        secondTask.GetAwaiter().GetResult();
        Assert(secondRejected, "second instance should be rejected");
        Assert(firstActivated.Wait(TimeSpan.FromSeconds(2)), "second instance should signal the first instance");
    }

    private static void TestLocalization()
    {
        var recoveryKeys = new[]
        {
            "Action.ReselectColumn",
            "Action.CreateMissingColumn",
            "Action.RemoveFromSummary",
            "Action.BoardMenu",
            "Action.ConfigureWindow",
            "Action.RemoveBoard",
            "Dialog.MissingColumn",
            "Dialog.CreateMissingColumn",
            "Dialog.RemoveFromSummary",
            "Message.ReselectColumnPrompt",
            "Message.CreateMissingColumnPrompt",
            "Message.RemoveFromSummaryPrompt",
        };

        Assert(
            LocalizationService.SupportedLanguages.Select(option => option.Code).SequenceEqual(new[] { "auto", "en", "zh" }),
            "only automatic, English, and Simplified Chinese UI options should remain");

        foreach (var language in new[] { "en", "zh" })
        {
            LocalizationService.Use(language);
            var addCard = LocalizationService.Text("Action.AddCard");
            Assert(!string.IsNullOrWhiteSpace(addCard), $"{language} add-card label should exist");
            Assert(addCard != "Action.AddCard", $"{language} add-card label should be translated");
            Assert(LocalizationService.Text("Error.WriteFailed", "test").Contains("test", StringComparison.Ordinal), $"{language} write error should format details");
            foreach (var key in recoveryKeys)
            {
                var text = LocalizationService.Text(key);
                Assert(!string.IsNullOrWhiteSpace(text) && text != key, $"{language} {key} should be available");
            }

            Assert(LocalizationService.Text("Message.CreateMissingColumnPrompt", "DOING").Contains("DOING", StringComparison.Ordinal), $"{language} missing-column prompt should format the title");
            Assert(LocalizationService.Text("Message.RemoveFromSummaryPrompt", "Reading").Contains("Reading", StringComparison.Ordinal), $"{language} remove prompt should format the board");
        }

        LocalizationService.Use("zh");
        Assert(LocalizationService.Text("Action.ReselectColumn") == "重新选择列", "zh reselect-column label mismatch");
        Assert(LocalizationService.Text("Action.CreateMissingColumn") == "创建缺失列", "zh create-column label mismatch");
        Assert(LocalizationService.Text("Action.RemoveFromSummary") == "从汇总移除", "zh remove-summary label mismatch");
        Assert(LocalizationService.Text("Action.NewBoard") == "新建看板", "zh new-board label mismatch");
        Assert(LocalizationService.Text("Action.AddExistingBoard") == "添加现有看板", "zh add-existing label mismatch");
        Assert(LocalizationService.Text("Action.Refresh") == "刷新看板", "zh refresh-boards label mismatch");
        Assert(LocalizationService.Text("Action.ConfigureBoards") == "配置看板", "zh configure-boards label mismatch");
        Assert(LocalizationService.Text("Action.SplitToDesktop") == "分窗到桌面", "zh split-to-desktop label mismatch");
        Assert(LocalizationService.Text("Action.OpenSource") == "打开原 Markdown 文件", "zh open-source label mismatch");
        Assert(LocalizationService.Text("Action.ConfigureWindow") == "配置窗口", "zh configure-window label mismatch");
        Assert(LocalizationService.Text("Action.RemoveBoard") == "移除看板", "zh remove-board label mismatch");
        Assert(LocalizationService.Text("Action.Ink") == "墨黑", "zh ink-theme label mismatch");
        Assert(LocalizationService.Text("Label.Vault") == "仓库", "zh vault label mismatch");
        Assert(LocalizationService.NormalizeCode("zh-TW") == "zh", "legacy Traditional Chinese codes should normalize to Simplified Chinese");
        Assert(LocalizationService.NormalizeCode("ja") == "auto", "removed languages should normalize to automatic selection");
        LocalizationService.Use("zh-TW");
        Assert(LocalizationService.CurrentCode == "zh", "legacy Traditional Chinese codes should resolve to Simplified Chinese");
        Assert(!LocalizationService.IsRightToLeft, "the English/Chinese UI should stay left-to-right");
        LocalizationService.Use("auto");
    }
}
