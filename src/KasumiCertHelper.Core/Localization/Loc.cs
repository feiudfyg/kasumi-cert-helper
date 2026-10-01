using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace KasumiCertHelper.Core.Localization;

/// <summary>
/// Every user visible string in the application and in Core lives in an embedded JSON table
/// (<c>Localization/Strings.&lt;culture&gt;.json</c>). <see cref="Get"/> resolves a key through
/// culture fallback (exact culture, then the neutral table), so an <c>en-US</c> display language
/// falls back to the neutral <c>en</c> table without any extra work.
/// </summary>
public static class Loc
{
    /// <summary>Culture every other table falls back to.</summary>
    public const string NeutralCulture = "en";

    private const string ResourcePrefix = "KasumiCertHelper.Core.Localization.Strings.";
    private const string ResourceSuffix = ".json";

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = LoadTables();

    private static readonly string[] CultureOrder =
    {
        NeutralCulture,
        "zh-CN",
    };

    private static string _culture = NeutralCulture;

    /// <summary>Raised after <see cref="SetCulture"/> actually changed the active language.</summary>
    public static event EventHandler? CultureChanged;

    /// <summary>Cultures that ship with a table, neutral first.</summary>
    public static IReadOnlyList<string> SupportedCultures { get; } = CultureOrder;

    /// <summary>The active culture, always one of <see cref="SupportedCultures"/>.</summary>
    public static string Culture => _culture;

    /// <summary>
    /// The language Windows would pick for this app, reduced to a culture we ship a table for.
    /// </summary>
    public static string SystemCulture => ResolveCulture(CultureInfo.CurrentUICulture.Name);

    /// <summary>
    /// Maps any culture name onto a culture we have a table for: exact match, then the parent
    /// language, then the neutral table.
    /// </summary>
    public static string ResolveCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return NeutralCulture;
        }

        string name = culture.Trim().Replace('_', '-');

        if (Tables.ContainsKey(name))
        {
            return name;
        }

        int separator = name.IndexOf('-');
        while (separator > 0)
        {
            name = name[..separator];
            if (Tables.ContainsKey(name))
            {
                return name;
            }

            separator = name.IndexOf('-');
        }

        return NeutralCulture;
    }

    /// <summary>Switches the active language. Unknown or unsupported cultures resolve to English.</summary>
    public static void SetCulture(string? culture)
    {
        string resolved = ResolveCulture(culture);
        if (string.Equals(resolved, _culture, StringComparison.Ordinal))
        {
            return;
        }

        _culture = resolved;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(_culture);
        CultureChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Applies the language Windows would choose for this app.</summary>
    public static void UseSystemCulture() => SetCulture(SystemCulture);

    /// <summary>
    /// The localized text for <paramref name="key"/>. Unknown keys return the key itself so a
    /// missing translation is obvious in the UI instead of blank.
    /// </summary>
    public static string Get(string key) => GetFor(_culture, key);

    /// <summary>Localized text looked up in a specific culture (used by tests).</summary>
    public static string GetFor(string culture, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        foreach (string candidate in Fallbacks(culture))
        {
            if (Tables[candidate].TryGetValue(key, out string? value))
            {
                return value;
            }
        }

        return key;
    }

    /// <summary>The localized text for <paramref name="key"/> with <see cref="string.Format(string, object?[])"/> applied.</summary>
    public static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.GetCultureInfo(_culture), Get(key), arguments);

    /// <summary>Every key known to the given culture, sorted.</summary>
    public static IReadOnlyList<string> Keys(string culture)
    {
        string resolved = ResolveCulture(culture);
        return Tables[resolved].Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Human readable name of a culture, for the language picker.</summary>
    public static string DisplayName(string culture) => ResolveCulture(culture) switch
    {
        "zh-CN" => "中文（简体）",
        "en" => "English",
        _ => culture,
    };

    /// <summary>
    /// Indexer facade that lets XAML write <c>{x:Bind loc:Loc.Table['Key']}</c>. WinUI 3 does not
    /// support custom markup extensions, so this is how XAML and code-behind share one table.
    /// </summary>
    public static LocTable Table { get; } = new();

    private static IEnumerable<string> Fallbacks(string culture)
    {
        string resolved = ResolveCulture(culture);
        yield return resolved;

        if (!string.Equals(resolved, NeutralCulture, StringComparison.Ordinal))
        {
            yield return NeutralCulture;
        }
    }

    private static Dictionary<string, Dictionary<string, string>> LoadTables()    {
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Assembly assembly = typeof(Loc).Assembly;

        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resource.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            string culture = resource[ResourcePrefix.Length..^ResourceSuffix.Length];
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            Dictionary<string, string>? entries =
                JsonSerializer.Deserialize<Dictionary<string, string>>(stream);

            if (entries is not null)
            {
                tables[culture] = new Dictionary<string, string>(entries, StringComparer.Ordinal);
            }
        }

        if (!tables.ContainsKey(NeutralCulture))
        {
            tables[NeutralCulture] = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return tables;
    }
}

/// <summary>String indexer over the active language, see <see cref="Loc.Table"/>.</summary>
public sealed class LocTable
{
    public string this[string key] => Loc.Get(key);
}
