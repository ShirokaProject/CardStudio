using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace CardStudio;

public sealed partial class CardStudioView
{
    private CompletionWindow? _axamlCompletionWindow;

    private void InitializeAxamlCompletion()
    {
        _codeEditor.TextArea.TextEntered += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            var trigger = e.Text[^1];
            if (!char.IsLetterOrDigit(trigger) && trigger is not ('<' or '/' or ' ' or '=' or '"' or '\'' or '.' or ':' or '{'))
                return;
            if (_axamlCompletionWindow is not null &&
                trigger is not (' ' or '=' or '"' or '\'' or '<' or '/' or '.' or ':' or '{')) return;
            ShowAxamlCompletion();
        };
        _codeEditor.TextArea.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Space &&
                (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                ShowAxamlCompletion();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) CloseAxamlCompletion();
        };
    }

    private void ShowAxamlCompletion()
    {
        if (_codeEditor.Document is null) return;
        var result = AxamlCompletionService.Get(_codeEditor.Document.Text, _codeEditor.CaretOffset);
        if (result is null) { CloseAxamlCompletion(); return; }
        CloseAxamlCompletion();
        var window = new CompletionWindow(_codeEditor.TextArea)
        {
            StartOffset = result.Start,
            EndOffset = result.End,
            CloseAutomatically = true,
            CloseWhenCaretAtBeginning = false
        };
        var theme = ActualThemeVariant;
        Application.Current!.TryGetResource("StudioCompletionSurface", theme, out var surface);
        Application.Current.TryGetResource("StudioOutline", theme, out var outline);
        Application.Current.TryGetResource("StudioText", theme, out var foreground);
        window.Child = null;
        window.Child = new Border
        {
            Background = surface as IBrush,
            BorderBrush = outline as IBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(3),
            Child = window.CompletionList
        };
        window.CompletionList.Foreground = foreground as IBrush;
        window.CompletionList.IsFiltering = true;
        foreach (var suggestion in result.Items)
            window.CompletionList.CompletionData.Add(new AxamlCompletionData(suggestion));
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_axamlCompletionWindow, window)) _axamlCompletionWindow = null;
        };
        _axamlCompletionWindow = window;
        window.Show();
    }

    private void CloseAxamlCompletion()
    {
        _axamlCompletionWindow?.Hide();
        _axamlCompletionWindow = null;
    }

    private sealed class AxamlCompletionData(AxamlSuggestion suggestion) : ICompletionData
    {
        public IImage? Image => null;
        public string Text => suggestion.Label;
        public object Content => suggestion.Label;
        public object Description => suggestion.Detail;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment segment, EventArgs args)
        {
            var document = textArea.Document;
            var offset = Math.Clamp(segment.Offset, 0, document.TextLength);
            var length = Math.Clamp(segment.Length, 0, document.TextLength - offset);
            document.Replace(offset, length, suggestion.Insertion);
            textArea.Caret.Offset = offset + (suggestion.CaretOffset ?? suggestion.Insertion.Length);
        }
    }
}
