using Avalonia;
using Avalonia.Browser;
using CardStudio;

internal static class Program
{
    private static Task Main(string[] args) =>
        AppBuilder.Configure<App>().StartBrowserAppAsync("out");
}
