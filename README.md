<img src="src/Browser/wwwroot/icon.svg" alt="CardStudio" width="56" height="56">

# CardStudio

Avalonia 卡片调试器：编写 AXAML、C# 和可选 ViewModel，实时预览并导出 PNG , 给ShiroBot开发者提供一个快速调试卡片样式的工具。

[浏览器中在线使用 - card.shiroka.org](https://card.shiroka.org)

图片菜单可导入、插入和清空图片资源；浏览器在本机保存资源，刷新后可继续使用。macOS 编辑器支持 ⌘C / ⌘V / ⌘F。

## 本地运行

需要 .NET 10 SDK。

```bash
dotnet run --project src/Desktop/CardStudio.Desktop.csproj
```

浏览器版还需安装 WASM 工具：

```bash
dotnet workload install wasm-tools
dotnet run --project src/Browser/CardStudio.Browser.csproj
```


浏览器版使用 [Noto Sans SC（OFL 1.1）](src/Browser/wwwroot/OFL-NotoSansSC.txt) 和 [GSAP（Standard License）](https://gsap.com/standard-license/)。
