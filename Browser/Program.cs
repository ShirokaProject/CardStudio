using Avalonia;
using Avalonia.Browser;
using ShiroBot.CardStudio;

internal static class Program
{
    private static Task Main(string[] args) =>
        AppBuilder.Configure<App>().StartBrowserAppAsync("out");
}
