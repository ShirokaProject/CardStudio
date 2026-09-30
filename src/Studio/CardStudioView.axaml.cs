using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices.JavaScript;
using System.Text.RegularExpressions;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace CardStudio;

public sealed partial class CardStudioView : UserControl
{
    private readonly TextEditor _codeEditor;
    private readonly TextEditor _csharpEditor;
    private readonly TextEditor _viewModelEditor;
    private readonly TextBox _dpiBox;
    private readonly Button _themeButton;
    private readonly Button _refreshButton;
    private bool _darkTheme;
    private readonly List<TextMate.Installation> _textMateInstallations = [];
    private readonly Dictionary<HighlightingColor, HighlightingBrush?> _lightHighlightingColors = [];
    private RegistryOptions? _textMateGrammars;
    private readonly ComboBox _controlTypeCombo;
    private readonly Button _insertTypeButton;
    private readonly Button _copyButton;
    private readonly Button _downloadButton;
    private readonly ThemeVariantScope _cardThemeScope;
    private readonly ContentControl _cardHost;
    private Viewbox? _renderRoot;
    private Grid? _renderHost;
    private Control? _renderContent;
    private readonly TextBlock _previewMetaText;
    private readonly TextBlock _statusText;
    private readonly Border _errorPanel;
    private readonly TextBlock _errorText;
    private readonly Border _previewLoadingOverlay;
    private bool _previewBusy;
    private bool _copyInProgress;
    private readonly DispatcherTimer _previewTimer;
    private readonly List<string> _assemblyDirectories = [];
    private Assembly? _localAssembly;
    private Assembly? _compiledAssembly;
    private Type? _compiledViewType;
    private string? _compiledCodeKey;
    private RenderTargetBitmap? _previewBitmap;
    private string? _currentPath;
    private int _renderVersion;

    private IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)!.StorageProvider;

    public CardStudioView()
    {
        AvaloniaXamlLoader.Load(this);

        _codeEditor = this.FindControl<TextEditor>("CodeEditor")!;
        _csharpEditor = this.FindControl<TextEditor>("CSharpEditor")!;
        _viewModelEditor = this.FindControl<TextEditor>("ViewModelEditor")!;
        if (OperatingSystem.IsBrowser())
        {
            _codeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("XML");
            _csharpEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
            _viewModelEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
        }
        else
        {
            try
            {
                var grammars = _textMateGrammars = new RegistryOptions(ThemeName.LightPlus);
                var xmlInstallation = _codeEditor.InstallTextMate(grammars);
                _textMateInstallations.Add(xmlInstallation);
                xmlInstallation.SetGrammar(
                    grammars.GetScopeByLanguageId(grammars.GetLanguageByExtension(".xml").Id));
                foreach (var editor in new[] { _csharpEditor, _viewModelEditor })
                {
                    var installation = editor.InstallTextMate(grammars);
                    _textMateInstallations.Add(installation);
                    installation.SetGrammar(
                        grammars.GetScopeByLanguageId(grammars.GetLanguageByExtension(".cs").Id));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Card Studio: 语法高亮初始化失败：" + GetUsefulError(ex));
            }
        }
        _dpiBox = this.FindControl<TextBox>("DpiBox")!;
        _themeButton = this.FindControl<Button>("ThemeButton")!;
        _refreshButton = this.FindControl<Button>("RefreshButton")!;
        _controlTypeCombo = this.FindControl<ComboBox>("ControlTypeCombo")!;
        _insertTypeButton = this.FindControl<Button>("InsertTypeButton")!;
        _copyButton = this.FindControl<Button>("CopyButton")!;
        _downloadButton = this.FindControl<Button>("DownloadButton")!;
        _cardThemeScope = this.FindControl<ThemeVariantScope>("CardThemeScope")!;
        _cardHost = this.FindControl<ContentControl>("CardHost")!;
        _previewMetaText = this.FindControl<TextBlock>("PreviewMetaText")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        var version = typeof(CardStudioView).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? $"v{typeof(CardStudioView).Assembly.GetName().Version?.ToString(2)}";
        var revisionSeparator = version.IndexOf('+');
        if (revisionSeparator >= 0 && version.Length > revisionSeparator + 8)
            version = version[..(revisionSeparator + 8)];
        this.FindControl<TextBlock>("VersionText")!.Text = "CardStudio " + version;
        _errorPanel = this.FindControl<Border>("ErrorPanel")!;
        _errorText = this.FindControl<TextBlock>("ErrorText")!;
        _previewLoadingOverlay = this.FindControl<Border>("PreviewLoadingOverlay")!;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RenderPreview();
        };

        _codeEditor.TextChanged += (_, _) => SchedulePreview();
        _dpiBox.TextChanged += (_, _) => SchedulePreview();
        _themeButton.Click += (_, _) =>
        {
            Application.Current!.RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark
                ? ThemeVariant.Light : ThemeVariant.Dark;
        };
        _refreshButton.Click += async (_, _) => await RunPreviewAsync();
        this.FindControl<Button>("SampleButton")!.Click += (_, _) => LoadSample();
        this.FindControl<Button>("OpenButton")!.Click += async (_, _) => await OpenAxamlAsync();
        this.FindControl<Button>("SaveButton")!.Click += async (_, _) => await SaveAxamlAsync();
        _copyButton.Click += async (_, _) => await CopyPngAsync();
        _downloadButton.Click += async (_, _) => await ExportPngAsync();
        this.FindControl<Button>("LoadAssemblyButton")!.Click += async (_, _) => await LoadAssemblyAsync();
        _insertTypeButton.Click += (_, _) => InsertReflectedControl();
        _csharpEditor.TextChanged += (_, _) => _statusText.Text = "C# 已修改 · 点击运行以编译并预览";
        _viewModelEditor.TextChanged += (_, _) => _statusText.Text = "ViewModel 已修改 · 点击运行以编译并预览";

        AssemblyLoadContext.Default.Resolving += ResolveAssembly;
        DetachedFromVisualTree += (_, _) =>
        {
            _previewTimer.Stop();
            AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
            _previewBitmap?.Dispose();
        };

        InitializeImageAssets();
        InitializeEditorShortcuts();
        LoadSample();
        if (OperatingSystem.IsBrowser())
        {
            this.FindControl<Button>("LoadAssemblyButton")!.IsVisible = false;
            _controlTypeCombo.IsVisible = false;
            _insertTypeButton.IsVisible = false;
        }
        ActualThemeVariantChanged += (_, _) =>
        {
            UpdateEditorTheme();
            if (!_previewBusy) RenderPreview();
        };
        Loaded += async (_, _) =>
        {
            UpdateEditorTheme();
            await RestoreImageAssetsAsync();
            RenderPreview();
        };
    }

    private void UpdateEditorTheme()
    {
        _darkTheme = ActualThemeVariant == ThemeVariant.Dark;
        if (OperatingSystem.IsBrowser())
            JSHost.GlobalThis.SetProperty("cardStudioDarkTheme", _darkTheme);
        _themeButton.Content = _darkTheme ? "深色" : "浅色";
        if (OperatingSystem.IsBrowser())
        {
            foreach (var name in new[] { "XML", "C#" })
            {
                var definition = HighlightingManager.Instance.GetDefinition(name);
                foreach (var color in definition.NamedHighlightingColors)
                {
                    _lightHighlightingColors.TryAdd(color, color.Foreground);
                    var original = _lightHighlightingColors[color];
                    color.Foreground = _darkTheme && original is not null
                        ? new SimpleHighlightingBrush(Color.Parse(color.Name switch
                        {
                            "Comment" or "Preprocessor" => "#6A9955",
                            "AttributeName" => "#9CDCFE",
                            "AttributeValue" or "String" or "Char" => "#CE9178",
                            "MethodCall" => "#DCDCAA",
                            "NumberLiteral" => "#B5CEA8",
                            "StringInterpolation" => "#D4D4D4",
                            "XmlTag" or "Keywords" or "Visibility" or "TypeKeywords" => "#569CD6",
                            _ => "#C586C0"
                        })) : original;
                }
            }
            foreach (var editor in new[] { _codeEditor, _csharpEditor, _viewModelEditor })
            {
                var definition = editor == _codeEditor ? "XML" : "C#";
                editor.SyntaxHighlighting = null;
                editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(definition);
            }
        }
        else if (_textMateGrammars is not null)
        {
            var theme = _textMateGrammars.LoadTheme(_darkTheme ? ThemeName.DarkPlus : ThemeName.LightPlus);
            foreach (var installation in _textMateInstallations) installation.SetTheme(theme);
        }
    }

    private void SchedulePreview()
    {
        if (_previewBusy) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void LoadSample()
    {
        _codeEditor.Text = ReadSample("HelloCard.axaml.txt");
        _csharpEditor.Text = ReadSample("HelloCardCode.txt");
        _viewModelEditor.Text = ReadSample("HelloCardViewModel.txt");
        _currentPath = null;
        RenderPreview(forceCompile: true);
    }

    private static string ReadSample(string name)
    {
        using var stream = typeof(CardStudioView).Assembly.GetManifestResourceStream(
            "CardStudio.Samples." + name);
        if (stream is null) throw new InvalidOperationException($"示例文件 {name} 不存在。");
        return new StreamReader(stream).ReadToEnd();
    }

    private async Task RunPreviewAsync()
    {
        if (_previewBusy) return;

        _previewTimer.Stop();
        ++_renderVersion;
        var axaml = _codeEditor.Text ?? string.Empty;
        var code = _csharpEditor.Text ?? string.Empty;
        var viewModel = _viewModelEditor.Text ?? string.Empty;
        SetPreviewBusy(true);
        _statusText.Text = "正在编译并渲染预览…";

        // Let the pressed button and loading indicator paint before Roslyn uses the UI thread on WASM.
        await Task.Delay(OperatingSystem.IsBrowser() ? 320 : 50);

        try
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                var classMatch = Regex.Match(axaml, "x:Class=\\\"([^\\\"]+)\\\"").Value;
                var codeKey = classMatch + "\n" + code + "\n" + viewModel;
                var compiled = OperatingSystem.IsBrowser()
                    ? RuntimeCodeCompiler.Compile(axaml, code, viewModel)
                    : await Task.Run(() => RuntimeCodeCompiler.Compile(axaml, code, viewModel));

                if (axaml != _codeEditor.Text || code != _csharpEditor.Text ||
                    viewModel != _viewModelEditor.Text)
                {
                    _statusText.Text = "代码已修改 · 请再次点击运行";
                    SetPreviewBusy(false);
                    return;
                }

                (_compiledAssembly, _compiledViewType) = compiled;
                _compiledCodeKey = codeKey;
            }

            RenderPreview();
        }
        catch (Exception ex)
        {
            ShowError(GetUsefulError(ex));
        }
    }

    private void SetPreviewBusy(bool busy)
    {
        _previewBusy = busy;
        if (busy)
        {
            _copyButton.IsEnabled = false;
            _downloadButton.IsEnabled = false;
        }
        _previewLoadingOverlay.IsVisible = busy && !OperatingSystem.IsBrowser();
        if (OperatingSystem.IsBrowser())
            JSHost.GlobalThis.SetProperty("cardStudioPreviewBusy", busy);
    }

    private void RenderPreview(bool forceCompile = false)
    {
        _previewTimer.Stop();
        var version = ++_renderVersion;
        _copyButton.IsEnabled = false;
        _downloadButton.IsEnabled = false;
        var axaml = _codeEditor.Text;
        if (string.IsNullOrWhiteSpace(axaml))
        {
            ShowError("AXAML 内容不能为空。");
            return;
        }

        if (!double.TryParse(_dpiBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) ||
            dpi is < 72 or > 600)
        {
            ShowError("DPI 请输入 72 到 600 之间的数字。");
            return;
        }

        try
        {
            Control control;
            if (string.IsNullOrWhiteSpace(_csharpEditor.Text))
            {
                control = AvaloniaRuntimeXamlLoader.Load(axaml, _localAssembly) as Control
                    ?? throw new InvalidOperationException("AXAML 顶层节点必须是 Avalonia Control。");
            }
            else
            {
                var classMatch = Regex.Match(axaml, "x:Class=\\\"([^\\\"]+)\\\"").Value;
                var codeKey = classMatch + "\n" + _csharpEditor.Text + "\n" + _viewModelEditor.Text;
                if (_compiledAssembly is null || _compiledCodeKey != codeKey && forceCompile)
                {
                    (_compiledAssembly, _compiledViewType) = RuntimeCodeCompiler.Compile(
                        axaml, _csharpEditor.Text, _viewModelEditor.Text);
                    _compiledCodeKey = codeKey;
                }

                RuntimeXamlContext.Begin(axaml);
                try
                {
                    control = Activator.CreateInstance(_compiledViewType!) as Control
                        ?? throw new InvalidOperationException("无法创建 C# 控件实例。请提供公开无参构造函数。");
                    if (!RuntimeXamlContext.Initialized) RuntimeXamlContext.Load(control);
                }
                finally
                {
                    RuntimeXamlContext.End();
                }
            }

            if (control is null)
            {
                throw new InvalidOperationException("AXAML 顶层节点必须是 Avalonia Control。");
            }

            var host = new Grid
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                ClipToBounds = false
            };
            host.Children.Add(control);
            var renderRoot = new Viewbox { Stretch = Stretch.Fill, Child = host };
            _renderContent = control;
            _renderHost = host;
            _renderRoot = renderRoot;
            _cardHost.Content = renderRoot;
            _cardThemeScope.RequestedThemeVariant = _darkTheme
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
            _errorPanel.IsVisible = false;
            _statusText.Text = "正在渲染图片…";

            Dispatcher.UIThread.Post(() => CapturePreview(version, dpi), DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            ShowError(GetUsefulError(ex));
        }
    }

    private void CapturePreview(int version, double dpi)
    {
        if (version != _renderVersion) return;

        try
        {
            var content = _renderContent!;
            var host = _renderHost!;
            var renderRoot = _renderRoot!;

            content.Measure(ResolveMeasureConstraint(content));
            var desired = content.DesiredSize;
            if (!IsFinitePositive(desired.Width) || !IsFinitePositive(desired.Height))
            {
                throw new InvalidOperationException("无法自动确定渲染尺寸。请在 AXAML 根控件上设置有限的 Width/Height，或确保内容能计算出有限的 DesiredSize。");
            }

            var width = ResolveFinalAxisSize(content.Width, content.MinWidth, content.MaxWidth, desired.Width);
            var height = ResolveFinalAxisSize(content.Height, content.MinHeight, content.MaxHeight, desired.Height);
            content.Width = width;
            content.Height = height;
            host.Width = width;
            host.Height = height;
            var size = new Size(width, height);
            host.Measure(size);
            host.Arrange(new Rect(size));
            UpdateLayout();

            var pixelWidth = (int)Math.Ceiling(width * dpi / 96);
            var pixelHeight = (int)Math.Ceiling(height * dpi / 96);
            if (pixelWidth > 8192 || pixelHeight > 8192 || (long)pixelWidth * pixelHeight > 24_000_000)
            {
                throw new InvalidOperationException("预览图片过大，请减小卡片尺寸或 DPI。");
            }

            renderRoot.Width = pixelWidth;
            renderRoot.Height = pixelHeight;
            renderRoot.Measure(new Size(pixelWidth, pixelHeight));
            renderRoot.Arrange(new Rect(0, 0, pixelWidth, pixelHeight));
            UpdateLayout();

            var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
            try
            {
                bitmap.Render(renderRoot);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }

            var oldBitmap = _previewBitmap;
            _previewBitmap = bitmap;
            oldBitmap?.Dispose();

            _previewMetaText.Text = $"{pixelWidth} × {pixelHeight} px · {dpi:0.#} DPI";
            _statusText.Text = "预览已更新 · 编辑 AXAML 会自动刷新";
            _errorPanel.IsVisible = false;
            _copyButton.IsEnabled = true;
            _downloadButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ShowError(GetUsefulError(ex));
        }
        finally
        {
            if (version == _renderVersion) SetPreviewBusy(false);
        }
    }

    private static Size ResolveMeasureConstraint(Control control) => new(
        IsFinitePositive(control.Width) ? control.Width : IsFinitePositive(control.MaxWidth) ? control.MaxWidth : double.PositiveInfinity,
        IsFinitePositive(control.Height) ? control.Height : IsFinitePositive(control.MaxHeight) ? control.MaxHeight : double.PositiveInfinity);

    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;

    private static double ResolveFinalAxisSize(double value, double min, double max, double desired)
    {
        if (IsFinitePositive(value)) return value;
        var result = desired;
        if (IsFinitePositive(min)) result = Math.Max(result, min);
        if (IsFinitePositive(max)) result = Math.Min(result, max);
        return result;
    }

    private async Task OpenAxamlAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开 AXAML",
            AllowMultiple = OperatingSystem.IsBrowser(),
            FileTypeFilter = [new FilePickerFileType("卡片源文件") { Patterns = ["*.axaml", "*.xaml", "*.cs"] }]
        });
        var file = files.FirstOrDefault(f => f.Name.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
                                             f.Name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            _codeEditor.Text = await reader.ReadToEndAsync();
            _currentPath = file.TryGetLocalPath();
            var baseName = Path.GetFileNameWithoutExtension(file.Name);
            _csharpEditor.Text = await ReadCompanionAsync(files, _currentPath, baseName + ".axaml.cs");
            _viewModelEditor.Text = await ReadCompanionAsync(files, _currentPath, baseName + "ViewModel.cs");
            _statusText.Text = $"已打开 {file.Name} 及配套 C# 文件";
            RenderPreview(forceCompile: true);
        }
        catch (Exception ex)
        {
            ShowError(GetUsefulError(ex));
        }
    }

    private static async Task<string> ReadCompanionAsync(
        IReadOnlyList<IStorageFile> selectedFiles, string? axamlPath, string name)
    {
        var selected = selectedFiles.FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            await using var stream = await selected.OpenReadAsync();
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        if (axamlPath is not null)
        {
            var path = Path.Combine(Path.GetDirectoryName(axamlPath)!, name);
            if (File.Exists(path)) return await File.ReadAllTextAsync(path);
        }

        return string.Empty;
    }

    private async Task SaveAxamlAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存 AXAML",
            SuggestedFileName = Path.GetFileName(_currentPath ?? "card.axaml"),
            DefaultExtension = "axaml",
            FileTypeChoices = [new FilePickerFileType("AXAML 文件") { Patterns = ["*.axaml"] }]
        });
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_codeEditor.Text ?? string.Empty);
            await writer.FlushAsync();
            _currentPath = file.TryGetLocalPath();
            var baseName = Path.GetFileNameWithoutExtension(file.Name);
            if (_currentPath is not null)
            {
                var directory = Path.GetDirectoryName(_currentPath)!;
                await File.WriteAllTextAsync(Path.Combine(directory, baseName + ".axaml.cs"),
                    _csharpEditor.Text ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(_viewModelEditor.Text))
                {
                    await File.WriteAllTextAsync(Path.Combine(directory, baseName + "ViewModel.cs"),
                        _viewModelEditor.Text);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(_csharpEditor.Text))
                    await SaveCompanionAsync(baseName + ".axaml.cs", _csharpEditor.Text);
                if (!string.IsNullOrWhiteSpace(_viewModelEditor.Text))
                    await SaveCompanionAsync(baseName + "ViewModel.cs", _viewModelEditor.Text);
            }
            _statusText.Text = $"已保存 {file.Name} 及配套 C# 文件";
        }
        catch (Exception ex)
        {
            ShowError(GetUsefulError(ex));
        }
    }

    private async Task SaveCompanionAsync(string name, string text)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存 " + name,
            SuggestedFileName = name,
            DefaultExtension = "cs",
            FileTypeChoices = [new FilePickerFileType("C# 文件") { Patterns = ["*.cs"] }]
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
        await writer.FlushAsync();
    }

    private async Task ExportPngAsync()
    {
        if (_previewBitmap is null)
        {
            ShowError("请先修正 AXAML，生成可用预览后再导出。");
            return;
        }

        var suggestedName = Path.GetFileNameWithoutExtension(_currentPath ?? "card.axaml") + ".png";
        if (OperatingSystem.IsBrowser())
        {
            try
            {
                using var png = new MemoryStream();
                _previewBitmap.Save(png, PngBitmapEncoderOptions.Default);
                var payload = JsonSerializer.Serialize(new
                {
                    fileName = suggestedName,
                    base64 = Convert.ToBase64String(png.ToArray())
                });
                JSHost.GlobalThis.SetProperty("cardStudioPngDownload", payload);
                _statusText.Text = $"已下载 {suggestedName}";
            }
            catch (Exception ex)
            {
                ShowError(GetUsefulError(ex));
            }
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出 PNG",
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG 图片") { Patterns = ["*.png"] }]
        });
        if (file is null) return;

        try
        {
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            _previewBitmap.Save(stream, PngBitmapEncoderOptions.Default);
            await stream.FlushAsync();
            _statusText.Text = $"已导出 {file.Name}";
        }
        catch (Exception ex)
        {
            ShowError(GetUsefulError(ex));
        }
    }

    private async Task CopyPngAsync()
    {
        if (_previewBitmap is null || _previewBusy || _copyInProgress) return;

        _copyInProgress = true;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard
                ?? throw new InvalidOperationException("剪贴板不可用。");
            await clipboard.SetBitmapAsync(_previewBitmap);
            _statusText.Text = "图片已复制到剪贴板";
            _errorPanel.IsVisible = false;
        }
        catch (Exception ex)
        {
            _errorText.Text = "复制图片失败：" + GetUsefulError(ex);
            _errorPanel.IsVisible = true;
            _statusText.Text = "复制图片失败";
        }
        finally
        {
            _copyInProgress = false;
        }
    }

    private async Task LoadAssemblyAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "加载自定义控件程序集",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(".NET 程序集") { Patterns = ["*.dll"] }]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;

        try
        {
            var directory = Path.GetDirectoryName(path)!;
            if (!_assemblyDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                _assemblyDirectories.Add(directory);
            }

            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            _localAssembly = assembly;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.OfType<Type>().ToArray();
            }

            var controls = types
                .Where(type => type.IsPublic && !type.IsAbstract && !type.IsGenericType &&
                               typeof(Control).IsAssignableFrom(type) &&
                               !typeof(TopLevel).IsAssignableFrom(type) &&
                               type.GetConstructor(Type.EmptyTypes) is not null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .Select(type => new ControlTypeOption(type))
                .ToArray();
            _controlTypeCombo.ItemsSource = controls;
            _controlTypeCombo.SelectedIndex = controls.Length > 0 ? 0 : -1;
            _controlTypeCombo.IsEnabled = controls.Length > 0;
            _insertTypeButton.IsEnabled = controls.Length > 0;
            _statusText.Text = $"已通过反射加载 {assembly.GetName().Name} · 发现 {controls.Length} 个可预览控件";
            _errorPanel.IsVisible = false;
            RenderPreview();
        }
        catch (Exception ex)
        {
            ShowError(GetUsefulError(ex));
        }
    }

    private Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name is null) return null;
        foreach (var directory in _assemblyDirectories)
        {
            var path = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
        }

        return null;
    }

    private void InsertReflectedControl()
    {
        if (_controlTypeCombo.SelectedItem is not ControlTypeOption option) return;

        var type = option.Type;
        _codeEditor.Text = $"""
            <Border xmlns="https://github.com/avaloniaui"
                    xmlns:local="clr-namespace:{type.Namespace};assembly={type.Assembly.GetName().Name}"
                    Width="720" Height="420" Background="#F8FAFD">
              <local:{type.Name} />
            </Border>
            """;
        _statusText.Text = $"已插入 {type.FullName}";
        RenderPreview();
    }

    private void ShowError(string message)
    {
        SetPreviewBusy(false);
        Console.Error.WriteLine("Card Studio: " + message);
        _copyButton.IsEnabled = false;
        _downloadButton.IsEnabled = false;
        _errorText.Text = message;
        _errorPanel.IsVisible = true;
        _statusText.Text = "渲染失败 · 修改 AXAML 后会自动重试";
    }

    private static string GetUsefulError(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: { } inner })
        {
            exception = inner;
        }

        return exception.Message;
    }

    private sealed record ControlTypeOption(Type Type)
    {
        public override string ToString() => Type.FullName ?? Type.Name;
    }
}
