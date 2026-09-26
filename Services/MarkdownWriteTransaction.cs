using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DesktopOverlayBoard.Models;

namespace DesktopOverlayBoard.Services;

internal static class MarkdownWriteTransaction
{
    internal static KanbanWriteResult Execute(
        string filePath,
        string failureLogMessage,
        Func<string, KanbanDocument> parse,
        Func<KanbanDocument, KanbanWriteResult> operation)
    {
        using var writeMutex = CreateMutex(filePath);
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
                return KanbanWriteResult.Fail(LocalizationService.Text("Error.WriteBusy"));
            }

            using var source = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var document = parse(reader.ReadToEnd());
            return operation(document);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, failureLogMessage);
            return KanbanWriteResult.Fail(LocalizationService.Text("Error.WriteFailed", ex.Message));
        }
        finally
        {
            if (lockTaken)
            {
                writeMutex.ReleaseMutex();
            }
        }
    }

    internal static Mutex CreateMutex(string filePath)
    {
        var normalized = Path.GetFullPath(filePath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return new Mutex(initiallyOwned: false, $@"Local\GlassKanbanOverlay-{hash}");
    }
}
