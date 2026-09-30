using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace CardStudio;

[SupportedOSPlatform("browser")]
internal static partial class BrowserAssetStorage
{
    [JSImport("globalThis.cardStudioAssets.load")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> LoadAsync();

    [JSImport("globalThis.cardStudioAssets.save")]
    [return: JSMarshalAs<JSType.Promise<JSType.Void>>]
    internal static partial Task SaveAsync(string json);
}
