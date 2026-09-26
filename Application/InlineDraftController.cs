namespace DesktopOverlayBoard.Application;

internal sealed class InlineDraftController
{
    public bool IsSubmitting { get; private set; }
    public bool IsFinished { get; private set; }
    public bool IsLostFocusSuppressed { get; private set; }
    public bool CanFinish => !IsFinished && !IsSubmitting;

    public bool TryBeginSubmission()
    {
        if (!CanFinish) return false;
        IsSubmitting = true;
        return true;
    }

    public void ReleaseSubmission() => IsSubmitting = false;

    public void Complete()
    {
        IsFinished = true;
        IsLostFocusSuppressed = false;
    }

    public void SuppressLostFocus() => IsLostFocusSuppressed = true;

    public void RestoreLostFocus() => IsLostFocusSuppressed = false;

    public bool ShouldCommitOnLostFocus(bool isImeComposing) =>
        CanFinish && !IsLostFocusSuppressed && !isImeComposing;
}
