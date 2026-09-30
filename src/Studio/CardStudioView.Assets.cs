using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace CardStudio;

public sealed partial class CardStudioView
{
    private sealed record ImageAsset(string Id, string Name, string Base64)
    {
        public override string ToString() => Name;
    }

    private readonly List<ImageAsset> _imageAssets = [];
    private readonly Dictionary<string, Bitmap> _assetBitmaps = [];
    private ListBox _imageAssetsList = null!;
    private bool _assetsRestored;
    private static string AssetFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CardStudio", "images.json");

    private void InitializeImageAssets()
    {
        _imageAssetsList = this.FindControl<ListBox>("ImageAssetsList")!;
        this.FindControl<TextBlock>("ImageAssetsHint")!.Text = OperatingSystem.IsBrowser()
            ? "保存在此浏览器中，刷新后仍可使用。"
            : "保存在本机，下次打开仍可使用。";
        this.FindControl<Button>("ImportImagesButton")!.Click += async (_, _) => await ImportImagesAsync();
        this.FindControl<Button>("ClearImagesButton")!.Click += async (_, _) =>
        {
            try
            {
                await SaveImageAssetsAsync([]);
                foreach (var (key, bitmap) in _assetBitmaps)
                {
                    Application.Current!.Resources.Remove(key);
                    bitmap.Dispose();
                }
                _assetBitmaps.Clear();
                _imageAssets.Clear();
                RefreshAssetList();
                RenderPreview();
                _statusText.Text = "图片资源已清空";
            }
            catch (Exception ex) { _statusText.Text = "清理失败：" + GetUsefulError(ex); }
        };
        this.FindControl<Button>("InsertImageButton")!.Click += (_, _) =>
        {
            if (_imageAssetsList.SelectedItem is not ImageAsset asset) return;
            this.FindControl<TabControl>("EditorTabs")!.SelectedIndex = 0;
            _codeEditor.Document.Insert(_codeEditor.CaretOffset,
                $"<Image Source=\"{{DynamicResource {asset.Id}}}\" Stretch=\"Uniform\" />");
            _codeEditor.Focus();
        };
    }

    private async Task RestoreImageAssetsAsync()
    {
        if (_assetsRestored) return;
        _assetsRestored = true;
        try
        {
            var json = OperatingSystem.IsBrowser() ? await BrowserAssetStorage.LoadAsync()
                : File.Exists(AssetFile) ? await File.ReadAllTextAsync(AssetFile) : "[]";
            foreach (var asset in JsonSerializer.Deserialize<List<ImageAsset>>(json) ?? [])
                RegisterImageAsset(asset);
            RefreshAssetList();
        }
        catch (Exception ex) { _statusText.Text = "恢复图片失败：" + GetUsefulError(ex); }
    }

    private async Task SaveImageAssetsAsync(IEnumerable<ImageAsset> assets)
    {
        var json = JsonSerializer.Serialize(assets);
        if (OperatingSystem.IsBrowser()) await BrowserAssetStorage.SaveAsync(json);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AssetFile)!);
            await File.WriteAllTextAsync(AssetFile, json);
        }
    }

    private void RegisterImageAsset(ImageAsset asset)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(asset.Base64));
        var bitmap = new Bitmap(stream);
        Application.Current!.Resources[asset.Id] = bitmap;
        _assetBitmaps.Add(asset.Id, bitmap);
        _imageAssets.Add(asset);
    }

    private void RefreshAssetList()
    {
        _imageAssetsList.ItemsSource = _imageAssets.ToArray();
        if (_imageAssets.Count > 0) _imageAssetsList.SelectedIndex = _imageAssets.Count - 1;
    }

    private async Task ImportImagesAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入图片", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("图片")
                { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"], MimeTypes = ["image/*"] }]
            });
            foreach (var file in files)
            {
                await using var source = await file.OpenReadAsync();
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer);
                RegisterImageAsset(new ImageAsset("CardStudioImage_" + Guid.NewGuid().ToString("N"),
                    file.Name, Convert.ToBase64String(buffer.ToArray())));
            }
            RefreshAssetList();
            await SaveImageAssetsAsync(_imageAssets);
            _statusText.Text = $"已保存 {_imageAssets.Count} 个图片资源 · 选择图片后插入 AXAML";
        }
        catch (Exception ex)
        {
            RefreshAssetList();
            _statusText.Text = "图片导入或保存失败：" + GetUsefulError(ex);
        }
    }
}
