using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CardStudio;

internal static class RuntimeCodeCompiler
{
    public static (Assembly Assembly, Type ViewType) Compile(
        string axaml, string codeBehind, string? viewModel)
    {
        var className = XDocument.Parse(axaml).Root?.Attribute(
            XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value;
        if (string.IsNullOrWhiteSpace(className) || !className.Contains('.'))
        {
            throw new InvalidOperationException("使用 C# 代码时，AXAML 根节点需要 x:Class=\"命名空间.控件名\"。");
        }

        var separator = className.LastIndexOf('.');
        var namespaceName = className[..separator];
        var typeName = className[(separator + 1)..];
        var generated = $$"""
            namespace {{namespaceName}}
            {
                public partial class {{typeName}}
                {
                    private void InitializeComponent()
                    {
                        // Resolve the host at run time. Browser hosts can load a newer
                        // CardStudio build alongside Avalonia metadata from an older pack.
                        var host = System.Linq.Enumerable.First(
                            System.AppDomain.CurrentDomain.GetAssemblies(),
                            a => a.GetName().Name == "CardStudio");
                        host.GetType("CardStudio.RuntimeXamlContext", throwOnError: true)!
                            .GetMethod("Initialize")!
                            .Invoke(null, new object[] { this });
                    }
                }
            }
            """;

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var trees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(codeBehind, parseOptions, "Card.axaml.cs"),
            CSharpSyntaxTree.ParseText(generated, parseOptions, "Card.generated.cs")
        };
        if (!string.IsNullOrWhiteSpace(viewModel))
        {
            trees.Add(CSharpSyntaxTree.ParseText(viewModel, parseOptions, "CardViewModel.cs"));
        }

        var compilation = CSharpCompilation.Create(
            "CardStudio.Live." + Guid.NewGuid().ToString("N"),
            trees,
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(8)
                .Select(d => d.ToString());
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        output.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(output);
        var viewType = assembly.GetType(className, throwOnError: true)!;
        if (!typeof(Control).IsAssignableFrom(viewType))
        {
            throw new InvalidOperationException($"{className} 必须继承 Avalonia.Controls.Control。");
        }

        return (assembly, viewType);
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        // Browser assemblies have no file path. Their in-memory PE metadata is usable by Roslyn.
        foreach (var name in new[]
                 {
                     "System.Runtime", "System.Collections", "System.Linq", "System.ObjectModel",
                     "System.ComponentModel", "System.Private.CoreLib", "netstandard"
                 })
        {
            try { Assembly.Load(name); }
            catch { /* Optional framework assembly. */ }
        }

        var references = new List<MetadataReference>();
        // Compile against the executing assemblies. On-disk files may have changed
        // since startup; earlier live compilations are outputs, not dependencies.
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic &&
                        a != typeof(RuntimeCodeCompiler).Assembly &&
                        !(a.GetName().Name?.StartsWith("CardStudio.Live.", StringComparison.Ordinal) ?? false))
            .GroupBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(a => a.GetName().Version).First());
        foreach (var assembly in assemblies)
        {
            unsafe
            {
                if (assembly.TryGetRawMetadata(out var pointer, out var length))
                {
                    var module = ModuleMetadata.CreateFromMetadata((IntPtr)pointer, length);
                    references.Add(AssemblyMetadata.Create(module).GetReference());
                    continue;
                }
            }

            string? path;
            try { path = assembly.Location; }
            catch { path = null; }
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return references;
    }
}

public static class RuntimeXamlContext
{
    private static string? _axaml;
    public static bool Initialized { get; private set; }

    public static void Begin(string axaml)
    {
        _axaml = axaml;
        Initialized = false;
    }

    public static void Initialize(Control root)
    {
        if (_axaml is null) throw new InvalidOperationException("AXAML 初始化上下文不存在。");
        Load(root);
    }

    public static void Load(Control root)
    {
        if (_axaml is null) throw new InvalidOperationException("AXAML 初始化上下文不存在。");
        var document = XDocument.Parse(_axaml, LoadOptions.PreserveWhitespace);
        // The instance already has the compiled x:Class; resolving it again can pick an older live assembly.
        document.Root?.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Remove();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting)));
        AvaloniaRuntimeXamlLoader.Load(stream, root.GetType().Assembly, root);
        Initialized = true;
    }

    public static void End() => _axaml = null;
}
