// SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.RegularExpressions;

namespace CouchSubs.Core;

/// <summary>
/// Süslü yazı tiplerinde, solan ya da titreyen altyazıda yazı tanıma bazı sözcükleri harf, rakam ve simge
/// karışımı çöpe çevirir ("ou$3e", "waters2aljd"). Bunlar hiçbir dilde sözcük değildir; çevirmen onları
/// olduğu gibi taşır. Sözcük ortasında simge ya da harflerle rakamların iç içe geçtiği parçalar atılır;
/// gerçek bir sözcüğe yapışmış çöp ("waters2aljd") sözcükten ayrılır. "PS5", "R2", "2nd", "100m", "$500"
/// gibi olağan biçimler korunur (Mac'teki OcrNoise ile aynı kurallar).
/// </summary>
public static partial class OcrNoise
{
    private const string Symbols = "$@#%^*_=|\\~<>{}¢£¥§©®°±µ¶¤";

    public static string Clean(string line) => Clean(line, false);

    internal static string Clean(string line, bool keepIdentifiers)
    {
        var kept = new List<string>();
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            // Kumanda simgesinin yeri (ButtonIcons.Marker) çöp değildir.
            if (token[0] == ButtonIcons.Marker || (keepIdentifiers &&
                (GamePhoneText.ContainsIdentifier(token) || GamePhoneText.IsWrittenChatWord(token))))
            {
                kept.Add(token);
                continue;
            }
            var (lead, core, trail) = Split(token);
            // Tek başına "l" ya da "|" çoğu zaman yanlış okunmuş "I"dır ("l don't know", "|'ll go"); ardından küçük
            // harfle başlayan sözcük yoksa ve token ?/! ile bitmiyorsa altyazı kutusunun kenarı olabilir.
            if (MisreadI(core) is { } pronoun)
            {
                var next = index + 1 < tokens.Length ? tokens[index + 1].FirstOrDefault(char.IsLetter) : '\0';
                var endsQuestionOrExclamation = trail.IndexOfAny(['?', '!']) >= 0;
                if (pronoun != "I" || char.IsLower(next) || endsQuestionOrExclamation) kept.Add(lead + pronoun + trail);
                continue;
            }
            // Yalnız "?!", ".", "," gibi işaretlerden oluşan parça: satır başında ya da cümle bittikten sonra yazı
            // tanımanın uydurmasıdır ("?! Where", "car. ?!"); sözcükten ayrı düşmüşse sözcüğe yapışır ("going ?!").
            if (core.Length == 0 && IsStrayPunctuation(token))
            {
                if (kept.Count > 0 && !".?!…,;:".Contains(kept[^1][^1])) kept[^1] += token;
                continue;
            }
            // Harfsiz, rakamsız ve yalnız simgeden oluşan parça ("_", "~", "=") yazı tanımanın uydurmasıdır: renkli
            // sözcüğün açık zeminde kaybolan harfleri "_" okunuyordu ("Get in the _ eedboat", GitHub #854, GTA 6).
            if (IsSymbolOnly(core))
            {
                if (trail.Length > 0 && kept.Count > 0) kept[^1] += trail;
                continue;
            }
            if (CleanCore(core) is { } fixedCore)
                kept.Add(lead + (KnownWord is { } known ? Unglue(fixedCore, known) : fixedCore) + trail);
            else if (trail.Length > 0 && kept.Count > 0)
                // Atılan çöpün ardındaki noktalama cümleye aittir ("ou$3e," → virgül kalır).
                kept[^1] += trail;
        }
        // Altyazının yanındaki göstergenin sayısı satırın başına yapışıyor ("9? We'll lose", "100. Mac and John").
        // Mesafe işareti de öyle ("23m CESARE: Here!"). Tek başına sayı ve ardından büyük harfle başlayan cümle: sayı atılır.
        if (kept.Count >= 3 && LeadingNumber().IsMatch(kept[0]) && char.IsUpper(kept[1][0])) kept.RemoveAt(0);
        // Faz3a fork: a "continue" arrow or button drawn after the subtitle is read as one stray character after the
        // last sentence ("angry or not? B", "… not? 9"; seen on a Discord stream). A lone letter or digit after
        // . ? ! or … ends no English sentence (except "I" and "A", which are kept).
        if (kept.Count >= 3 && kept[^1].Length == 1 && char.IsLetterOrDigit(kept[^1][0]) && kept[^1] is not ("I" or "A" or "a")
            && string.Join(' ', kept.Take(kept.Count - 1)).TrimEnd(' ', '"', '”', '’', '\'') is { Length: > 0 } before
            && ".?!…".Contains(before[^1]))
            kept.RemoveAt(kept.Count - 1);
        return string.Join(' ', kept);
    }

    [GeneratedRegex(@"^\d+(?:m|km|ft|yd)?[.?!:;]?$")]
    private static partial Regex LeadingNumber();

    /// <summary>
    /// Faz3a fork: the English dictionary (Windows spell checker), set by the app when it is available; null leaves
    /// glued words as they are. Used by <see cref="Unglue"/>.
    /// </summary>
    public static Func<string, bool>? KnownWord { get; set; }

    /// <summary>Short words a capitalised glued word may start with ("Ifwe're" → "If we're"); other capitalised
    /// tokens are left alone because they are usually names ("Yanagihara", "Setsuna").</summary>
    private static readonly HashSet<string> SentenceStarts = new(StringComparer.Ordinal)
    {
        "I", "A", "If", "In", "It", "Is", "Its", "It's", "I'm", "I'll", "I've", "I'd", "We", "We're", "You", "You're", "He",
        "She", "They", "The", "This", "That", "And", "But", "So", "Do", "Don't", "Go", "No", "Not", "What", "When", "Where",
        "Why", "How", "Who", "Let's", "Get", "Got", "Come", "Look", "Oh", "Okay", "Yes", "Yeah", "Well", "Just", "All",
        "Are", "Can", "Did", "Have", "Was", "Will", "My", "Your", "Our", "To", "For", "On", "At", "Of", "Or", "As",
    };

    /// <summary>
    /// Faz3a fork: advanced recognition (PP-OCR) sometimes drops the space between two words ("bunchof", "trydoing",
    /// "you'redone", "Ifwe're allon"; seen on a Discord stream), and the offline model then mistranslates the line. A
    /// token that is not a dictionary word but splits into two dictionary words is split; a real word never is. Each half
    /// may be split once more ("Ifwe'reallon"). Single letters are only "a" and "I".
    /// </summary>
    internal static string Unglue(string word, Func<string, bool> known, int depth = 0)
    {
        if (word.Length < 4 || depth > 1 || !word.All(c => char.IsLetter(c) || c is '\'' or '’')) return word;
        if (known(word) || known(word.ToLowerInvariant())) return word;
        var capitalised = char.IsUpper(word[0]);
        if (capitalised && word.Length > 1 && word.Skip(1).All(char.IsUpper)) return word; // SHOUTING or an acronym
        bool Known(string part) =>
            part.Length == 1 ? part is "a" or "I" or "A" : part.Length >= 2 && (known(part) || known(part.ToLowerInvariant()));
        string? best = null;
        var bestScore = -1;
        for (var cut = 1; cut < word.Length; cut++)
        {
            var (left, right) = (word[..cut], word[cut..]);
            if (right[0] is '\'' or '’' || left[^1] is '\'' or '’') continue;
            if (capitalised && depth == 0 && !SentenceStarts.Contains(left)) continue;
            if (!Known(left)) continue;
            var rest = Known(right) ? right : Unglue(right, known, depth + 1);
            if (rest == right && !Known(right)) continue;
            // Prefer the most even cut: "allon" → "all on", not "a llon"; the longest-word split reads most naturally.
            var score = Math.Min(left.Length, rest.Split(' ')[0].Length);
            if (score > bestScore)
            {
                bestScore = score;
                best = left + " " + rest;
            }
        }
        return best ?? word;
    }

    /// <summary>
    /// Bir okumanın ne kadar sözcük gibi göründüğü: düzgün sözcüklerin harfleri artı, çöp ve karışık büyük-küçük
    /// harfli parçalar ("CESAREiVJütt'h") eksi sayılır. Aynı karenin iki okumasından iyisini seçmek için.
    /// </summary>
    public static int Score(string text)
    {
        var score = 0;
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var core = Split(token).Core;
            var letters = new string(core.Where(char.IsLetter).ToArray());
            if (letters.Length == 0) continue;
            // Sözcük ortasındaki parantez ("the[lil") de okuma karışıklığıdır.
            var clean = CleanCore(core) == core && NormalCase(core) && core.IndexOfAny(['[', ']', '(', ')']) < 0 && !Vowelless(letters);
            score += clean ? letters.Length : -2 * letters.Length;
        }
        return score;
    }

    /// <summary>
    /// Okuma çöp gibi mi: harflerinin altıda birinden çoğu bozuk sözcüklerde (karışık büyük-küçük harf, simge, ünlüsüz
    /// "Gtt"). Altyazılar geçişte birbirinin üstüne solarken yazı tanıma iki satırı karıştırıp ("Gtt solkolfege.")
    /// noktayla biten bir çöp okuyordu; nokta yüzünden ilk karede çevriliyor, TV'de anlamsız bir cümle beliriyordu (Mac ile aynı).
    /// </summary>
    public static bool LooksGarbled(string text)
    {
        int good = 0, bad = 0;
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var core = Split(token).Core;
            var letters = new string(core.Where(char.IsLetter).ToArray());
            if (letters.Length == 0) continue;
            var clean = CleanCore(core) == core && NormalCase(core) && core.IndexOfAny(['[', ']', '(', ')']) < 0 && !Vowelless(letters);
            if (clean) good += letters.Length; else bad += letters.Length;
        }
        return bad * 6 > good + bad;
    }

    /// <summary>Parça yalnız simgelerden mi oluşuyor (harf ve rakam yok, en az bir <see cref="Symbols"/> imi).</summary>
    private static bool IsSymbolOnly(string core) =>
        core.Length > 0 && !core.Any(c => char.IsLetter(c) || char.IsNumber(c)) && core.Any(c => Symbols.Contains(c));

    /// <summary>
    /// Satır bir okuma kırıntısı mı: altyazı ya da görev değil, ekrandaki küçük bir göstergenin ya da solan satırın yazı
    /// tanımadan kalan parçası ("one ready.", "ar.", "kulak"). En çok üç sözcüklü ve küçük harfle (ya da harf dışı bir
    /// imle) başlayan, ya da hiç gerçek sözcük (en az iki harfli, ünlü taşıyan) taşımayan, ya da çöp gibi okunan satır.
    /// Büyük harfle başlayan kısa konuşma ("Go!", "Okay.") kırıntı değildir. Kırıntı yanındaki uzun konuşmayı "eski"
    /// gösterip altyazıdan attırmaz, görev satırı da sayılmaz (<see cref="StickyRows"/>, <see cref="MissionLine"/>; GitHub
    /// #854, #882). Mac/iPhone (OcrNoise.isCrumb) ve Android (OcrNoise.isCrumb) aynı kuralı uygular.
    /// </summary>
    public static bool IsCrumb(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return true;
        if (LooksGarbled(trimmed)) return true;
        var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Any(char.IsLetter)).ToList();
        if (words.Count > 3) return false;
        var first = trimmed.FirstOrDefault(c => char.IsLetter(c) || char.IsNumber(c));
        if (first != '\0' && !(char.IsLetter(first) && char.IsUpper(first))) return true;
        return !words.Any(word =>
        {
            var letters = new string(word.Where(char.IsLetter).ToArray()).ToLowerInvariant();
            return letters.Length >= 2 && letters.IndexOfAny("aeiouyäöüıàáâèéêìíîòóôùúû".ToCharArray()) >= 0;
        });
    }

    /// <summary>
    /// Sözcük (harfleri) İngilizcede bir sözcük olamaz: ünlüsüz (<see cref="Vowelless"/>), İngilizcede hiç yan yana
    /// gelmeyen iki harf taşıyor ("ijz": "jz") ya da harf-rakam/simge karışık. Yalnız aynı satırın iki okumasından
    /// hangisinin bozuk olduğuna karar verirken kullanılır (<see cref="SubtitleStabilizer"/>); tek başına satır atmaz.
    /// </summary>
    public static bool IsNonWord(string word)
    {
        var core = Split(word).Core;
        var letters = new string(core.Where(char.IsLetter).ToArray());
        if (letters.Length < 2) return false;
        if (CleanCore(core) != core || Vowelless(letters)) return true;
        var lower = letters.ToLowerInvariant();
        for (var i = 0; i + 1 < lower.Length; i++)
            if (ImpossiblePairs.Contains(lower.Substring(i, 2))) return true;
        return false;
    }

    /// <summary>İngilizce sözcüklerde yan yana gelmeyen harf çiftleri (yazı tanımanın "lives" → "Ijz" gibi uydurmalarında çıkar).</summary>
    private static readonly HashSet<string> ImpossiblePairs =
    [
        "bx", "cj", "cv", "cx", "dx", "fq", "fx", "gq", "gx", "hx", "jc", "jf", "jg", "jq", "js", "jv", "jw", "jx", "jz",
        "kq", "kx", "mx", "px", "pz", "qb", "qc", "qd", "qf", "qg", "qh", "qj", "qk", "ql", "qm", "qn", "qp", "qr", "qs",
        "qt", "qv", "qw", "qx", "qy", "qz", "sx", "vb", "vf", "vh", "vj", "vm", "vp", "vq", "vw", "vx", "wx", "xj", "xx",
        "zj", "zq", "zx",
    ];

    /// <summary>Üç ve daha çok harfli, ünlüsüz ve kısaltma olmayan (tümü büyük harf değil) sözcük: İngilizcede olmaz ("Gtt", "rnnk").</summary>
    private static bool Vowelless(string letters) =>
        letters.Length >= 3 && !letters.All(char.IsUpper) && letters.ToLowerInvariant().IndexOfAny("aeiouyäöüıàáâèéêìíîòóôùúû".ToCharArray()) < 0
        && !Interjections.Contains(Collapsed(letters.ToLowerInvariant()));

    /// <summary>Ünlüsüz ama gerçek ünlemler ("Hmm", "Shh", "Brr", "Grr", "Psst", "Pfft", "Mmm", "Zzz", "Tsk"); art arda tekrarlar tek harf sayılır.</summary>
    private static readonly HashSet<string> Interjections = ["hm", "m", "sh", "br", "gr", "pst", "pft", "tsk", "hrm", "z", "pht"];

    private static string Collapsed(string text)
    {
        var result = new System.Text.StringBuilder();
        foreach (var character in text)
            if (result.Length == 0 || result[^1] != character) result.Append(character);
        return result.ToString();
    }

    /// <summary>"word", "WORD", "Word", "McDonald", "don't": büyük harf yalnız başta ya da baştan bir dizi.</summary>
    private static bool NormalCase(string core)
    {
        var letters = core.Where(char.IsLetter).ToArray();
        if (letters.All(char.IsUpper) || letters.Skip(1).All(char.IsLower)) return true;
        // Tek iç büyük harf ("McDonald", "iPhone") olağan; daha fazlası OCR karışıklığıdır.
        return letters.Skip(1).Count(char.IsUpper) == 1 && letters.Count(char.IsLower) >= 2;
    }

    /// <summary>"l", "|", "l'm", "|'ll" → "I", "I'm", "I'll" (null: böyle bir okuma değil).</summary>
    private static string? MisreadI(string core)
    {
        if (core.Length == 0 || core[0] is not ('l' or '|')) return null;
        if (core.Length == 1) return "I";
        if (core[1] is not ('\'' or '’') || core[2..] is not ("m" or "ll" or "ve" or "d")) return null;
        return "I" + core[1..];
    }

    /// <summary>Yalnız ? ! . , ; : işaretlerinden oluşan parça; üç nokta ("...") gerçek noktalamadır.</summary>
    private static bool IsStrayPunctuation(string token) =>
        token.Length > 0 && token.All(c => "?!.,;:".Contains(c)) && !(token.Length >= 2 && token.All(c => c == '.'));

    /// <summary>Sözcüğün kendisi; null ise tamamen çöptür.</summary>
    private static string? CleanCore(string core)
    {
        var letters = core.Count(char.IsLetter);
        var digits = core.Count(char.IsNumber);
        var hasSymbol = core.Any(c => Symbols.Contains(c));
        if (letters == 0) return core;  // "24", "$500", "…"
        if (!hasSymbol && digits == 0) return core;
        if (!hasSymbol && IsUsualMix(core)) return core;
        // Gerçek sözcüğe yapışmış çöp: baştaki en az 3 harf korunur ("waters2aljd" → "waters").
        var prefix = new string(core.TakeWhile(c => char.IsLetter(c) || c is '\'' or '’' or '-').ToArray());
        return prefix.Count(char.IsLetter) >= 3 ? prefix : null;
    }

    /// <summary>"PS5", "R2", "MK2", "2nd", "100m", "4K", "60fps", "x2", "1980s", "R2-D2": olağan harf-rakam karışımları.</summary>
    private static bool IsUsualMix(string core) =>
        core.Split('-').All(part => !part.Any(char.IsNumber) || Usual().IsMatch(part));

    [GeneratedRegex(@"^(?:[A-Za-z]{1,4}\d{1,4}|\d+(?:st|nd|rd|th|s|k|K|m|km|kg|cm|mm|p|x|fps|hp|HP|XP|xp|v|V|D|d)|[xX]\d{1,3}|\d+[.,]\d+[A-Za-z]{0,3}|\d{1,2}[:.]\d{2}(?:am|pm|AM|PM)?)$")]
    private static partial Regex Usual();

    private static bool IsEdge(char c) =>
        !Symbols.Contains(c) && (char.IsPunctuation(c) || char.IsSymbol(c) || "\"'“”‘’«»()[]".Contains(c));

    private static (string Lead, string Core, string Trail) Split(string token)
    {
        int start = 0, end = token.Length;
        while (start < end && IsEdge(token[start])) start++;
        while (end > start && IsEdge(token[end - 1])) end--;
        return (token[..start], token[start..end], token[end..]);
    }
}
