// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CouchSubs.Core;

/// <summary>
/// Ekran metinleri. Mac uygulamasının Localizable.xcstrings dosyası gömülüdür ve doğrudan okunur;
/// böylece iki uygulama aynı çevirileri kullanır. Anahtar İngilizce metnin kendisidir.
/// </summary>
public sealed class Localizer
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Catalog = new(LoadCatalog);
    /// <summary>
    /// Faz3a fork: Arabic interface text (Resources/ar.json, English key → Arabic). "{app}" in a value is the app's
    /// name (<see cref="Fork.Name"/>), so renaming the app does not mean editing every string.
    /// </summary>
    private static readonly Lazy<Dictionary<string, string>> Arabic = new(LoadArabic);
    private readonly string language;

    private Localizer(string language) => this.language = language;

    public static Localizer ForLanguage(string language)
    {
        var code = language.ToLowerInvariant();
        return new(code.StartsWith("ar") ? "ar" : code.StartsWith("tr") ? "tr" : "en");
    }

    /// <summary>Faz3a fork: the interface is always Arabic (English for any string not translated yet).</summary>
    public static Localizer Current { get; } = ForLanguage("ar");

    public string Language => language;

    public string this[string key] =>
        language == "ar" ? Arabic.Value.TryGetValue(key, out var arabic) ? arabic : key
        : Catalog.Value.TryGetValue(key, out var values) && values.TryGetValue(language, out var value) ? value : key;

    /// <summary>xcstrings biçim belirteçlerini (%@, %lld, %1$@ …) verilen değerlerle doldurur.</summary>
    public string Format(string key, params object[] args) => Fill(this[key], args);

    /// <summary>Türkçesi (ya da verilen dili) eksik anahtarlar.</summary>
    public static IReadOnlyList<string> MissingTranslations(string language) =>
        [.. Catalog.Value.Where(entry => !entry.Value.ContainsKey(language)).Select(entry => entry.Key)];

    internal static string Fill(string format, object[] args)
    {
        var result = new StringBuilder();
        var next = 0;
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length) { result.Append(format[i]); continue; }
            if (format[i + 1] == '%') { result.Append('%'); i++; continue; }
            var j = i + 1;
            var position = -1;
            var digits = j;
            while (digits < format.Length && char.IsDigit(format[digits])) digits++;
            if (digits > j && digits < format.Length && format[digits] == '$')
            {
                position = int.Parse(format.AsSpan(j, digits - j), CultureInfo.InvariantCulture) - 1;
                j = digits + 1;
            }
            // Uzunluk belirteçleri (l, ll, h) atlanır; ardından gelen harf türdür.
            while (j < format.Length && format[j] is 'l' or 'h' or 'q' or 'z') j++;
            if (j >= format.Length || !"@dDiuUxXfgs".Contains(format[j])) { result.Append(format[i]); continue; }
            var index = position >= 0 ? position : next++;
            result.Append(index < args.Length ? Convert.ToString(args[index], CultureInfo.CurrentCulture) : "");
            i = j;
        }
        return result.ToString();
    }

    private static Dictionary<string, string> LoadArabic()
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream("ar.json");
        if (stream is null) return [];
        using var document = JsonDocument.Parse(stream);
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.EnumerateObject())
            table[entry.Name] = (entry.Value.GetString() ?? entry.Name).Replace("{app}", Fork.Name, StringComparison.Ordinal);
        return table;
    }

    private static Dictionary<string, Dictionary<string, string>> LoadCatalog()
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream("Localizable.xcstrings")
            ?? throw new InvalidOperationException("Localizable.xcstrings uygulamaya gömülmemiş");
        using var document = JsonDocument.Parse(stream);
        var catalog = new Dictionary<string, Dictionary<string, string>>();
        foreach (var entry in document.RootElement.GetProperty("strings").EnumerateObject())
        {
            var values = new Dictionary<string, string>();
            if (entry.Value.TryGetProperty("localizations", out var localizations))
                foreach (var localization in localizations.EnumerateObject())
                    if (localization.Value.TryGetProperty("stringUnit", out var unit) && unit.TryGetProperty("value", out var value))
                        values[localization.Name] = value.GetString() ?? entry.Name;
            catalog[entry.Name] = values;
        }
        return catalog;
    }
}
