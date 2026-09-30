using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Search;

namespace CardStudio;

public sealed partial class CardStudioView
{
    private void InitializeEditorShortcuts()
    {
        foreach (var editor in new[] { _codeEditor, _csharpEditor, _viewModelEditor })
        {
            var search = SearchPanel.Install(editor);
            search.TemplateApplied += (_, args) =>
            {
                var input = args.NameScope.Find("PART_searchTextBox") as Avalonia.Controls.TextBox;
                if (input?.InnerRightContent is not Avalonia.Controls.StackPanel options) return;
                foreach (var toggle in options.Children.OfType<Avalonia.Controls.Primitives.ToggleButton>())
                {
                    if (toggle.Content is not Avalonia.Controls.Shapes.Path icon) continue;
                    // The upstream template sets Stretch locally, which takes precedence over styles.
                    icon.Stretch = Stretch.Uniform;
                    icon.Width = 14;
                    icon.Height = 14;
                }
            };
            var animationVersion = 0;
            var slide = new TranslateTransform(0, 0)
            {
                Transitions = new Transitions
                {
                    new DoubleTransition
                    {
                        Property = TranslateTransform.YProperty,
                        Duration = TimeSpan.FromMilliseconds(220),
                        Easing = new CubicEaseOut()
                    }
                }
            };
            search.RenderTransform = slide;
            search.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(220),
                    Easing = new CubicEaseOut()
                }
            };
            void CloseSearch()
            {
                if (search.IsClosed) return;
                var version = ++animationVersion;
                search.Opacity = 0;
                slide.Y = -10;
                DispatcherTimer.RunOnce(() =>
                {
                    if (version != animationVersion || search.IsClosed) return;
                    search.Close();
                }, TimeSpan.FromMilliseconds(220));
            }

            var closeBinding = search.CommandBindings.First(binding => binding.Command == SearchCommands.CloseSearchPanel);
            search.CommandBindings.Remove(closeBinding);
            search.CommandBindings.Add(new RoutedCommandBinding(SearchCommands.CloseSearchPanel, (_, e) =>
            {
                CloseSearch();
                e.Handled = true;
            }));
            search.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                CloseSearch();
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            editor.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.F &&
                    (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)) &&
                    !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                {
                    ++animationVersion;
                    var opening = search.IsClosed;
                    var reveal = opening || search.Opacity < 1;
                    if (opening)
                    {
                        search.Opacity = 0;
                        slide.Y = -10;
                    }
                    search.Open();
                    search.ApplyTemplate();
                    Dispatcher.UIThread.Post(() =>
                    {
                        search.Reactivate();
                        if (reveal)
                        {
                            search.Opacity = 1;
                            slide.Y = 0;
                        }
                    }, DispatcherPriority.Loaded);
                    e.Handled = true;
                    return;
                }
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) ||
                    e.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
                    e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
                switch (e.Key)
                {
                    case Key.C: editor.Copy(); break;
                    case Key.V: editor.Paste(); break;
                    case Key.X: editor.Cut(); break;
                    case Key.A: editor.SelectAll(); break;
                    case Key.Z:
                        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) editor.Redo();
                        else editor.Undo();
                        break;
                    default: return;
                }
                e.Handled = true;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }
}
