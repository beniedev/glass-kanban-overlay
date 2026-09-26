using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopOverlayBoard.Application;
using DesktopOverlayBoard.Services;
using DesktopOverlayBoard.UI;
using static DesktopOverlayBoard.Tests.TestAssert;

namespace DesktopOverlayBoard.Tests;

internal static class InlineDraftTests
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                using var source = new HwndSource(new HwndSourceParameters("Synthetic inline draft tests")
                {
                    Width = 320,
                    Height = 100,
                    WindowStyle = unchecked((int)0x80000000),
                });
                TestSubmissionLifecycle();
                TestLostFocusPolicy();
                TestControllerInstancesAreIndependent();
                TestInputAppearance(source);
                TestKeyboardAndLostFocus(source);
                TestButtonFocusSuppression(source);
                TestImeEventOrder(source);
                TestFailedCommitRetainsInputAndDeduplicates(source);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Inline draft regression failed", failure);
    }

    private static void TestSubmissionLifecycle()
    {
        var draft = new InlineDraftController();
        Assert(draft.CanFinish && draft.TryBeginSubmission(), "a new draft must allow its first submission");
        Assert(draft.IsSubmitting && !draft.TryBeginSubmission(), "an in-progress submission must not be entered twice");
        draft.ReleaseSubmission();
        Assert(draft.CanFinish && !draft.IsFinished && draft.TryBeginSubmission(), "a failed submission must remain retryable");
        draft.Complete();
        draft.ReleaseSubmission();
        Assert(draft.IsFinished && !draft.CanFinish && !draft.TryBeginSubmission(), "a completed draft must not reopen from a late event");
    }

    private static void TestLostFocusPolicy()
    {
        var draft = new InlineDraftController();
        Assert(draft.ShouldCommitOnLostFocus(false), "normal focus loss must request a commit");
        Assert(!draft.ShouldCommitOnLostFocus(true), "IME composition must defer focus-loss submission");
        draft.SuppressLostFocus();
        Assert(!draft.ShouldCommitOnLostFocus(false), "button focus transfer must not submit before its click action");
        draft.RestoreLostFocus();
        Assert(draft.ShouldCommitOnLostFocus(false), "failed or explicit button actions must be able to restore focus-loss submission");
        Assert(draft.TryBeginSubmission() && !draft.ShouldCommitOnLostFocus(false), "focus loss during submission must not submit again");
        draft.Complete();
        draft.ReleaseSubmission();
        Assert(!draft.ShouldCommitOnLostFocus(false), "a completed draft must ignore late focus loss");
    }

    private static void TestControllerInstancesAreIndependent()
    {
        var previous = new InlineDraftController();
        Assert(previous.TryBeginSubmission(), "previous draft must start");
        previous.Complete();
        var current = new InlineDraftController();
        Assert(current.TryBeginSubmission(), "a new draft must have its own submission state");
        previous.ReleaseSubmission();
        Assert(current.IsSubmitting, "the previous draft's finally must not release a new draft's submission");
        current.ReleaseSubmission();
    }

    private static void TestInputAppearance(HwndSource source)
    {
        var owner = new Border();
        owner.Resources["WidgetInk"] = Brushes.Navy;
        var input = InlineDraftEditor.CreateAddInput(owner);
        source.RootVisual = input;
        Assert(ReferenceEquals(input.Foreground, Brushes.Navy), "inline input must resolve ink from its window owner");
        Assert(!input.AcceptsReturn && input.TextWrapping == TextWrapping.Wrap && input.FontSize == 12.5,
            "inline input must retain its single-line editing and wrapped display");
        Assert(((SolidColorBrush)input.Background).Color == Color.FromArgb(28, 255, 255, 255) &&
            input.Margin == new Thickness(8, 0, 6, 0), "inline input appearance must remain unchanged");
        Assert(InputMethod.GetIsInputMethodEnabled(input) && input.InputScope is not null,
            "new inline input must enable the existing IME support");
    }

    private static void TestKeyboardAndLostFocus(HwndSource source)
    {
        var input = CreateInput(source);
        var draft = new InlineDraftController();
        var commits = 0;
        var cancels = 0;
        InlineDraftEditor.BindAddInput(input, draft,
            () => { commits++; return Task.CompletedTask; },
            () => { cancels++; return Task.CompletedTask; });
        Assert(RaiseKey(input, source, Key.Enter).Handled && commits == 1, "Enter must invoke the commit callback once and be handled");
        Assert(RaiseKey(input, source, Key.Escape).Handled && cancels == 1, "Escape must invoke the cancel callback once and be handled");
        Assert(!RaiseKey(input, source, Key.A).Handled && commits == 1 && cancels == 1, "ordinary keys must retain normal text entry");
        RaiseLostFocus(input);
        Assert(commits == 2, "normal keyboard focus loss must invoke commit once");
        draft.Complete();
        RaiseLostFocus(input);
        Assert(commits == 2, "a removed input must ignore late focus loss");
    }

    private static void TestButtonFocusSuppression(HwndSource source)
    {
        var input = CreateInput(source);
        var draft = new InlineDraftController();
        var commits = 0;
        InlineDraftEditor.BindAddInput(input, draft,
            () => { commits++; return Task.CompletedTask; }, () => Task.CompletedTask);
        foreach (var button in new[] { new Button(), new Button() })
        {
            InlineDraftEditor.SuppressLostFocusOnPress(button, draft);
            button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            });
            RaiseLostFocus(input);
            Assert(commits == 0 && draft.IsLostFocusSuppressed, "save/cancel mouse press must suppress the intervening focus loss");
            draft.RestoreLostFocus();
        }
        RaiseLostFocus(input);
        Assert(commits == 1, "restoring focus-loss handling must leave the draft usable");
    }

    private static void TestImeEventOrder(HwndSource source)
    {
        var input = CreateInput(source);
        var draft = new InlineDraftController();
        var commits = 0;
        var cancels = 0;
        InlineDraftEditor.BindAddInput(input, draft,
            () => { commits++; return Task.CompletedTask; },
            () => { cancels++; return Task.CompletedTask; });
        RaiseComposition(input, TextCompositionManager.PreviewTextInputStartEvent);
        Assert(TextInputService.IsImeComposing(input), "composition start must reach the existing IME tracker");
        Assert(!RaiseKey(input, source, Key.Enter).Handled && commits == 0, "IME Enter must not submit or consume the candidate key");
        RaiseLostFocus(input);
        Assert(commits == 0, "IME focus loss must not submit");
        RaiseComposition(input, TextCompositionManager.PreviewTextInputUpdateEvent);
        Assert(TextInputService.IsImeComposing(input), "composition updates must remain composing");
        Assert(RaiseKey(input, source, Key.Escape).Handled && cancels == 1, "Escape must retain the existing cancel action during composition");
        RaiseComposition(input, TextCompositionManager.PreviewTextInputEvent);
        Assert(!RaiseKey(input, source, Key.Enter).Handled && commits == 0,
            "the completion event must keep submission deferred until the existing background reset");
        PumpBackground();
        Assert(!TextInputService.IsImeComposing(input), "composition completion must clear the tracker after dispatch");
        Assert(RaiseKey(input, source, Key.Enter).Handled && commits == 1, "Enter after composition completes must submit normally");
    }

    private static void TestFailedCommitRetainsInputAndDeduplicates(HwndSource source)
    {
        var input = CreateInput(source);
        input.Text = "  synthetic draft input  ";
        var draft = new InlineDraftController();
        var writes = 0;
        var flight = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        InlineDraftEditor.BindAddInput(input, draft, CommitAsync, CancelAsync);
        async Task CommitAsync()
        {
            if (!draft.TryBeginSubmission()) return;
            try
            {
                writes++;
                if (await flight.Task) draft.Complete();
                else draft.RestoreLostFocus();
            }
            finally
            {
                draft.ReleaseSubmission();
                completion.SetResult(true);
            }
        }
        Task CancelAsync()
        {
            if (draft.CanFinish) draft.Complete();
            return Task.CompletedTask;
        }

        RaiseKey(input, source, Key.Enter);
        RaiseKey(input, source, Key.Enter);
        RaiseLostFocus(input);
        RaiseKey(input, source, Key.Escape);
        Assert(writes == 1 && draft.IsSubmitting && !draft.IsFinished, "repeated Enter/focus/cancel events must not start or finish an in-progress write");
        flight.SetResult(false);
        PumpUntil(completion.Task);
        Assert(!draft.IsSubmitting && !draft.IsFinished && input.Text == "  synthetic draft input  ",
            "a failed write must preserve the exact visible input and allow retry");
        flight = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RaiseKey(input, source, Key.Enter);
        Assert(writes == 2 && draft.IsSubmitting, "retry must enter exactly one new write");
        flight.SetResult(true);
        PumpUntil(completion.Task);
        Assert(draft.IsFinished && !draft.IsSubmitting, "a successful retry must finish only its draft");
        RaiseKey(input, source, Key.Enter);
        RaiseLostFocus(input);
        Assert(writes == 2, "late events after success must not write again");
    }

    private static TextBox CreateInput(HwndSource source)
    {
        var owner = new Border();
        owner.Resources["WidgetInk"] = Brushes.White;
        var input = InlineDraftEditor.CreateAddInput(owner);
        source.RootVisual = input;
        return input;
    }

    private static KeyEventArgs RaiseKey(TextBox input, HwndSource source, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        };
        input.RaiseEvent(args);
        return args;
    }

    private static void RaiseLostFocus(TextBox input) => input.RaiseEvent(
        new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, input, new TextBox())
        {
            RoutedEvent = Keyboard.LostKeyboardFocusEvent,
        });

    private static void RaiseComposition(TextBox input, RoutedEvent routedEvent) => input.RaiseEvent(
        new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, input, "synthetic"))
        {
            RoutedEvent = routedEvent,
        });

    private static void PumpBackground()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void PumpUntil(Task completion)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var timeout = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(10) };
        timeout.Tick += (_, _) => frame.Continue = false;
        completion.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        timeout.Start();
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timeout.Stop();
        }
        Assert(completion.IsCompletedSuccessfully, "the controlled draft submission must finish before the dispatcher timeout");
    }
}
