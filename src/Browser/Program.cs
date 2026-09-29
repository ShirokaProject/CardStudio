using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using CardStudio;

internal static class Program
{
    private const string ChineseFont = "avares://CardStudio.Browser/Assets/Fonts#Noto Sans SC";

    private static Task Main(string[] args) =>
        AppBuilder.Configure<App>()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = ChineseFont,
                FontFallbacks =
                [
                    new FontFallback
                    {
                        FontFamily = new FontFamily(ChineseFont),
                        UnicodeRange = UnicodeRange.Parse(
                            "U+2600-27BF,U+2E80-9FFF,U+F900-FAFF,U+FF00-FFEF")
                    }
                ]
            })
            .StartBrowserAppAsync("out");
}
