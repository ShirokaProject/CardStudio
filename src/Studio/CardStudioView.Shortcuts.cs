using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Search;

namespace CardStudio;

public sealed partial class CardStudioView
{
    private void InitializeEditorShortcuts()
    {
        foreach (var editor in new[] { _codeEditor, _csharpEditor, _viewModelEditor })
        {
            var search = SearchPanel.Install(editor);
            editor.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) ||
                    e.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
                    e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
                switch (e.Key)
                {
                    case Key.C: editor.Copy(); break;
                    case Key.V: editor.Paste(); break;
                    case Key.X: editor.Cut(); break;
                    case Key.A: editor.SelectAll(); break;
                    case Key.F: search.Open(); break;
                    default: return;
                }
                e.Handled = true;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }
}
