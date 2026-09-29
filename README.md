# CardStudio

独立的 Avalonia 卡片工具，提供桌面版和 WebAssembly 浏览器版。左侧可编辑 AXAML、C# 代码隐藏文件和可选的 ViewModel，右侧实时预览并导出 PNG。两个版本的编辑器都使用 Material3.Avalonia 主题和带行号的 AvaloniaEdit；桌面版还支持 TextMate 语法着色。卡片预览使用与 ShiroBot 宿主相同的 FluentTheme。AXAML 在停止输入约 450 毫秒后自动更新；修改 C# 后点击「运行」重新编译。

## 运行

需要 .NET 10 SDK。在本仓库根目录运行桌面版：

```bash
dotnet run --project src/Desktop/CardStudio.Desktop.csproj
```

运行浏览器版（先安装 `dotnet workload install wasm-tools`）：

```bash
dotnet run --project src/Browser/CardStudio.Browser.csproj
```

打开命令输出的本地 HTTP 地址。发布静态站点可执行 `dotnet publish src/Browser/CardStudio.Browser.csproj -c Release`，然后部署 `src/Browser/bin/Release/net10.0-browser/publish/wwwroot`。推送 `main` 分支的 `src/` 变更时，GitHub Actions 会自动构建并部署浏览器版到 [GitHub Pages](https://shirokaproject.github.io/CardStudio/)；也可从 Actions 手动运行部署工作流。

浏览器版内嵌 [Noto Sans SC](https://github.com/notofonts/noto-cjk/tree/main/Sans/SubsetOTF/SC) 常规和粗体字形，以显示中文界面和卡片内容；字体遵循 [SIL Open Font License 1.1](src/Browser/wwwroot/OFL-NotoSansSC.txt)。这会增加 WASM 下载体积。桌面版仍使用系统字体，因此两端导出的字形可能略有差异。

启动后会打开 `src/Studio/Samples` 中的三文件示例。卡片根节点最好显式设置 `Width` 和 `Height`；DPI 影响导出 PNG 的像素尺寸，右侧预览会按可用空间缩放显示。截图采用与 ShiroBot 宿主相同的尺寸解析、`Viewbox.Stretch.Fill` 放大和 96 DPI 位图渲染流程。宿主使用 Avalonia Headless，桌面和浏览器使用各自平台后端；系统字体不同仍可能导致像素差异。

## 使用自己的 AXAML 和控件

- **打开 / 保存卡片**：桌面版选择 `BiliArticleDocument.axaml` 后会自动读取同目录的 `BiliArticleDocument.axaml.cs` 和可选的 `BiliArticleDocumentViewModel.cs`；保存时写出同名的配套文件。浏览器版打开时可一次选中这几个文件，保存时浏览器会分别弹出下载/保存对话框。
- **C# 页签**：使用 Roslyn 在运行时编译代码隐藏文件及可选 ViewModel。AXAML 根控件需要 `x:Class`，代码隐藏类需要公开无参构造函数，并可在构造函数中调用 `InitializeComponent()`。修改 C# 后点击「运行」。不需要代码隐藏时可清空 C# 页签，仅使用 AXAML。
- **加载控件 DLL（桌面版）**：通过反射加载程序集，列出有公开无参构造函数的 Avalonia 控件。选中类型后点击“插入控件”，编辑器会生成可继续修改的 AXAML 引用。浏览器版无法从本机路径动态加载 .NET DLL，因此不显示此功能。
- **主题 / DPI**：切换卡片的浅色或深色主题，并设置 PNG 输出精度。
- **导出 PNG**：导出当前成功渲染的图片。

自定义控件 DLL 所依赖的程序集可放在同一目录；工具会从该目录解析依赖。请使用与本项目相同的 Avalonia 主版本，并避免携带另一份 Avalonia 运行时。DLL 加载进当前进程后不能热替换，更新 DLL 时请重启工具。

可以用工具自身的 `src/Studio/bin/Debug/net10.0/CardStudio.dll` 测试反射功能；其中包含 `CardStudio.Samples.SampleBadgeControl`。
