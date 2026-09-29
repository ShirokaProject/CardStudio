using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ShiroBot.CardStudio;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "ShiroBot Card Studio",
                Width = 1480,
                Height = 900,
                MinWidth = 1360,
                MinHeight = 700,
                Content = new CardStudioView()
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new CardStudioView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
