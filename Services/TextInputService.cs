using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

namespace DesktopOverlayBoard.Services;

public static class TextInputService
{
    private static readonly DependencyProperty HandlersAttachedProperty =
        DependencyProperty.RegisterAttached("HandlersAttached", typeof(bool), typeof(TextInputService), new PropertyMetadata(false));

    private static readonly DependencyProperty IsComposingProperty =
        DependencyProperty.RegisterAttached("IsComposing", typeof(bool), typeof(TextInputService), new PropertyMetadata(false));

    public static void EnableIme(TextBox textBox)
    {
        InputMethod.SetIsInputMethodEnabled(textBox, true);
        InputMethod.SetPreferredImeState(textBox, InputMethodState.DoNotCare);
        textBox.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        if (textBox.InputScope is null)
        {
            var scope = new InputScope();
            scope.Names.Add(new InputScopeName(InputScopeNameValue.Default));
            textBox.InputScope = scope;
        }

        if (textBox.GetValue(HandlersAttachedProperty) is true)
        {
            return;
        }

        TextCompositionManager.AddPreviewTextInputStartHandler(textBox, OnTextInputStart);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(textBox, OnTextInputUpdate);
        TextCompositionManager.AddPreviewTextInputHandler(textBox, OnTextInput);
        textBox.Unloaded += TextBox_Unloaded;
        textBox.SetValue(HandlersAttachedProperty, true);
    }

    public static bool IsImeComposing(TextBox textBox)
    {
        return textBox.GetValue(IsComposingProperty) is true;
    }

    private static void OnTextInputStart(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SetValue(IsComposingProperty, true);
        }
    }

    private static void OnTextInputUpdate(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SetValue(IsComposingProperty, true);
        }
    }

    private static void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        textBox.Dispatcher.BeginInvoke(
            new Action(() => textBox.SetValue(IsComposingProperty, false)),
            DispatcherPriority.Background);
    }

    private static void TextBox_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        TextCompositionManager.RemovePreviewTextInputStartHandler(textBox, OnTextInputStart);
        TextCompositionManager.RemovePreviewTextInputUpdateHandler(textBox, OnTextInputUpdate);
        TextCompositionManager.RemovePreviewTextInputHandler(textBox, OnTextInput);
        textBox.Unloaded -= TextBox_Unloaded;
        textBox.SetValue(IsComposingProperty, false);
        textBox.SetValue(HandlersAttachedProperty, false);
    }
}
