using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CardStudio;

internal enum AxamlCompletionKind { Element, ClosingTag, Attribute, Value }

internal sealed record AxamlSuggestion(string Label, string Insertion, string Detail = "", int? CaretOffset = null);
internal sealed record AxamlCompletion(int Start, int End, IReadOnlyList<AxamlSuggestion> Items);

// The editor supplies the caret position; this class has no UI state, so the same
// language suggestions work in desktop and WebAssembly.
internal static partial class AxamlCompletionService
{
    private const string AvaloniaNamespace = "https://github.com/avaloniaui";
    private static readonly object CatalogLock = new();
    private static Type[]? _types;
    private static int _assemblyCount;
    private static readonly HashSet<Assembly> ExtraAssemblies = [];
    private static readonly string[] Colors = ["Transparent", "Black", "White", "Red", "Green", "Blue", "Gray", "Orange", "Purple", "DodgerBlue"];

    internal static AxamlCompletion? Get(string text, int position)
    {
        position = Math.Clamp(position, 0, text.Length);
        var lt = text.LastIndexOf('<', Math.Max(0, position - 1));
        var gt = text.LastIndexOf('>', Math.Max(0, position - 1));
        if (lt < 0 || lt <= gt || text.AsSpan(lt, position - lt).StartsWith("<!--")) return null;

        var before = text[lt..position];
        var start = position;
        while (start > lt && IsName(text[start - 1])) start--;
        var end = position;
        while (end < text.Length && IsName(text[end])) end++;
        var prefix = text[start..position];

        var quoted = FindOpenAttributeValue(text, lt, position);
        AxamlCompletionKind kind;
        string? attribute = null;
        if (quoted is { } value)
        {
            kind = AxamlCompletionKind.Value;
            attribute = value.Attribute;
            start = value.ValueStart;
            end = position;
            while (end < text.Length && text[end] != value.Quote) end++;
            prefix = text[start..position];
        }
        else if (before.StartsWith("</", StringComparison.Ordinal)) kind = AxamlCompletionKind.ClosingTag;
        else if (Regex.IsMatch(before, "^<\\s*[\\w:.-]*$")) kind = AxamlCompletionKind.Element;
        else kind = AxamlCompletionKind.Attribute;

        var tag = Regex.Match(before, "^</?\\s*([\\w:.-]+)").Groups[1].Value;
        var namespaces = GetNamespaces(text);
        IEnumerable<AxamlSuggestion> items = kind switch
        {
            AxamlCompletionKind.Element => ElementSuggestions(text, lt, namespaces),
            AxamlCompletionKind.ClosingTag => ClosingTags(text, lt).Select(name => new AxamlSuggestion(name, name, "结束标签")),
            AxamlCompletionKind.Attribute => AttributeSuggestions(tag, namespaces),
            AxamlCompletionKind.Value => ValueSuggestions(text, tag, attribute!, namespaces),
            _ => []
        };
        var filtered = items.Where(item => item.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(item => item.Label).ToArray();
        return filtered.Length == 0 ? null : new AxamlCompletion(start, end, filtered);
    }

    private static IEnumerable<AxamlSuggestion> ElementSuggestions(string text, int tagStart,
        Dictionary<string, string> namespaces)
    {
        var parent = ClosingTags(text, tagStart).FirstOrDefault();
        if (parent is not null && ResolveType(parent, namespaces) is { } parentType)
            foreach (var property in parentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.SetMethod is not null && p.GetIndexParameters().Length == 0))
                yield return new AxamlSuggestion($"{parentType.Name}.{property.Name}",
                    $"{parentType.Name}.{property.Name}", property.PropertyType.Name);

        foreach (var (nsPrefix, xmlNamespace) in namespaces)
        {
            foreach (var type in GetTypes().Where(t => MatchesNamespace(t, xmlNamespace)))
            {
                var name = string.IsNullOrEmpty(nsPrefix) ? type.Name : nsPrefix + ":" + type.Name;
                yield return new AxamlSuggestion(name, name, type.FullName ?? type.Name);
            }
        }
    }

    private static IEnumerable<AxamlSuggestion> AttributeSuggestions(string tag, Dictionary<string, string> namespaces)
    {
        foreach (var directive in new[] { "Name", "Classes", "x:Name", "x:Key", "x:Class", "x:DataType" })
            yield return Attribute(directive, "XAML");
        if (ResolveType(tag, namespaces) is not { } type) yield break;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.SetMethod is not null && p.GetIndexParameters().Length == 0))
            yield return Attribute(property.Name, property.PropertyType.Name);
        foreach (var evt in type.GetEvents(BindingFlags.Public | BindingFlags.Instance))
            yield return Attribute(evt.Name, "事件");
        foreach (var owner in GetTypes())
        {
            if (owner != typeof(Grid) && owner != typeof(DockPanel) && owner != typeof(Canvas)) continue;
            foreach (var field in owner.GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.Name.EndsWith("Property", StringComparison.Ordinal) &&
                    typeof(AvaloniaProperty).IsAssignableFrom(field.FieldType))
                    yield return Attribute(owner.Name + "." + field.Name[..^8], "附加属性");
        }
    }

    private static AxamlSuggestion Attribute(string name, string detail) =>
        new(name, name + "=\"\"", detail, name.Length + 2);

    private static IEnumerable<AxamlSuggestion> ValueSuggestions(string text, string tag, string attribute,
        Dictionary<string, string> namespaces)
    {
        if (attribute is "Source" or "Background" or "Foreground" or "BorderBrush" or "Fill" or "Stroke")
        {
            foreach (Match match in ResourceKeyRegex().Matches(text))
            {
                var key = match.Groups[1].Value;
                yield return new AxamlSuggestion("{DynamicResource " + key + "}",
                    "{DynamicResource " + key + "}", "本地资源");
            }
            if (Application.Current is { } app)
                foreach (var key in app.Resources.Keys.OfType<string>().Where(k => k.StartsWith("CardStudioImage_", StringComparison.Ordinal)))
                    yield return new AxamlSuggestion("{DynamicResource " + key + "}",
                        "{DynamicResource " + key + "}", "导入的图片");
        }
        foreach (var markup in new[] { "{Binding }", "{DynamicResource }", "{StaticResource }" })
            yield return new AxamlSuggestion(markup, markup, "标记扩展", markup.Length - 1);
        if (ResolveType(tag, namespaces)?.GetProperty(attribute,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is not { } property) yield break;
        var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (valueType == typeof(bool))
        {
            yield return new AxamlSuggestion("True", "True");
            yield return new AxamlSuggestion("False", "False");
        }
        else if (valueType.IsEnum)
            foreach (var name in Enum.GetNames(valueType)) yield return new AxamlSuggestion(name, name, valueType.Name);
        else if (typeof(IBrush).IsAssignableFrom(valueType) || valueType == typeof(Color))
            foreach (var color in Colors) yield return new AxamlSuggestion(color, color, "颜色");
    }

    private static IEnumerable<string> ClosingTags(string text, int position)
    {
        var stack = new Stack<string>();
        foreach (Match match in TagRegex().Matches(text[..position]))
        {
            var name = match.Groups[2].Value;
            if (match.Groups[1].Value == "/")
            {
                if (stack.Count > 0 && stack.Peek() == name) stack.Pop();
            }
            else if (match.Groups[3].Value != "/") stack.Push(name);
        }
        return stack;
    }

    private static (string Attribute, int ValueStart, char Quote)? FindOpenAttributeValue(string text, int lt, int position)
    {
        char quote = '\0';
        var quoteStart = -1;
        for (var i = lt + 1; i < position; i++)
        {
            if (text[i] != '"' && text[i] != '\'') continue;
            if (quote == '\0') { quote = text[i]; quoteStart = i; }
            else if (quote == text[i]) { quote = '\0'; quoteStart = -1; }
        }
        if (quoteStart < 0) return null;
        var match = Regex.Match(text[lt..quoteStart], "([\\w:.-]+)\\s*=\\s*$");
        return match.Success ? (match.Groups[1].Value, quoteStart + 1, quote) : null;
    }

    private static Dictionary<string, string> GetNamespaces(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal) { [""] = AvaloniaNamespace };
        foreach (Match match in NamespaceRegex().Matches(text)) result[match.Groups[1].Value] = match.Groups[2].Value;
        return result;
    }

    private static Type? ResolveType(string name, Dictionary<string, string> namespaces)
    {
        var separator = name.IndexOf(':');
        var prefix = separator < 0 ? "" : name[..separator];
        if (!namespaces.TryGetValue(prefix, out var xmlNamespace)) return null;
        var local = separator < 0 ? name : name[(separator + 1)..];
        return GetTypes().FirstOrDefault(t => t.Name == local && MatchesNamespace(t, xmlNamespace));
    }

    private static bool MatchesNamespace(Type type, string xmlNamespace)
    {
        if (xmlNamespace == AvaloniaNamespace) return type.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true;
        if (xmlNamespace.StartsWith("using:", StringComparison.Ordinal)) return type.Namespace == xmlNamespace[6..];
        if (!xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal)) return false;
        var parts = xmlNamespace[14..].Split(";assembly=", 2, StringSplitOptions.None);
        return type.Namespace == parts[0] && (parts.Length == 1 || type.Assembly.GetName().Name == parts[1]);
    }

    private static Type[] GetTypes()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic &&
                (a.GetName().Name?.StartsWith("Avalonia", StringComparison.Ordinal) == true ||
                 a.GetName().Name?.StartsWith("Material3", StringComparison.Ordinal) == true ||
                 a == typeof(AxamlCompletionService).Assembly || ExtraAssemblies.Contains(a)))
            .ToArray();
        if (_types is not null && _assemblyCount == assemblies.Length) return _types;
        lock (CatalogLock)
        {
            if (_types is not null && _assemblyCount == assemblies.Length) return _types;
            _assemblyCount = assemblies.Length;
            _types = assemblies.SelectMany(LoadTypes)
                .Where(t => t.IsPublic && !t.IsAbstract && !t.IsGenericTypeDefinition &&
                    (typeof(Control).IsAssignableFrom(t) || typeof(IBrush).IsAssignableFrom(t) ||
                     t == typeof(ResourceDictionary)))
                .OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
            return _types;
        }
    }

    internal static void RegisterAssembly(Assembly assembly)
    {
        lock (CatalogLock)
        {
            ExtraAssemblies.Add(assembly);
            _types = null;
        }
    }

    private static IEnumerable<Type> LoadTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
        catch { return []; }
    }

    private static bool IsName(char ch) => char.IsLetterOrDigit(ch) || ch is '_' or ':' or '.' or '-';

    [GeneratedRegex("xmlns(?::([\\w.-]+))?\\s*=\\s*[\"']([^\"']+)[\"']")]
    private static partial Regex NamespaceRegex();
    [GeneratedRegex("x:Key\\s*=\\s*[\"']([^\"']+)[\"']")]
    private static partial Regex ResourceKeyRegex();
    [GeneratedRegex("<(/?)([\\w:.-]+)[^>]*?(/?)>")]
    private static partial Regex TagRegex();
}
