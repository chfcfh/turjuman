// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork: the offline English→Arabic engine (OnnxMarianEngine) against the reference output of the exporter's own
// Python runner (live-draft/draft_int8.txt, same int8 model, greedy decoding). The model is not in the repository: set
// FAZ3A_SUBS_AR_MODEL to its folder; without it these tests return early.

using System.Diagnostics;
using CouchSubs.Core;
using CouchSubs.Core.Dubbing;
using Xunit.Abstractions;

namespace CouchSubs.Translation.Tests;

public sealed class ArabicOfflineTranslatorTests(ITestOutputHelper output)
{
    /// <summary>English line → Arabic from the Python reference runner (onnxruntime + sentencepiece, CPU, int8).</summary>
    private static readonly (string English, string Arabic)[] Reference =
    [
        ("The northern gate has been sealed since the war ended.", "البوابة الشمالية مغلقة منذ انتهاء الحرب"),
        ("If you want to pass, you will need the captain's seal.", "إذا أردت المرور، ستحتاج لختم القبطان"),
        ("Nobody leaves the city tonight. Orders from the king.", "لا أحد يغادر المدينة الليلة أوامر من الملك"),
        ("Bring me three wolf pelts and I might change my mind.", "أحضر لي ثلاثة من جلد الذئب و ربما أغير رأيي"),
        ("Continue", "تابع"),
        ("Inventory", "المخزون"),
        ("Settings", "الإعدادات"),
        ("New Game", "لعبة جديدة"),
        ("Load Game", "تحميل لعبة"),
        ("Press E to open the chest", "اضغط على E لفتح الصدر"),
        ("Your inventory is full.", "مخزونك ممتلئ"),
        ("You don't have enough gold.", "ليس لديك ما يكفي من الذهب."),
        ("I heard strange noises coming from the mine last night.", "سمعت أصوات غريبة قادمة من المنجم الليلة الماضية."),
    ];

    private static string? ModelFolder => ArabicModelFiles.IsComplete(Environment.GetEnvironmentVariable("FAZ3A_SUBS_AR_MODEL"))
        ? Environment.GetEnvironmentVariable("FAZ3A_SUBS_AR_MODEL") : null;

    [Fact]
    public void EngineMatchesTheReferenceRunner()
    {
        if (ModelFolder is not { } folder) return;
        var load = Stopwatch.StartNew();
        using var engine = new OnnxMarianEngine(folder, threads: 4);
        output.WriteLine($"load: {load.ElapsedMilliseconds} ms");
        engine.Translate("warm up", CancellationToken.None);
        var times = new List<double>();
        var same = 0;
        foreach (var (english, arabic) in Reference)
        {
            var clock = Stopwatch.StartNew();
            var got = engine.Translate(english, CancellationToken.None);
            times.Add(clock.Elapsed.TotalMilliseconds);
            if (got == arabic) same++;
            else output.WriteLine($"DIFF  {english}\n  got: {got}\n  ref: {arabic}");
        }
        times.Sort();
        output.WriteLine($"same as reference: {same}/{Reference.Length}; median {times[times.Count / 2]:0} ms, max {times[^1]:0} ms");
        // Tokenizer details (SentencePiece library vs. Python) may differ on a rare line; the bulk must be identical.
        Assert.True(same >= Reference.Length - 2, $"only {same}/{Reference.Length} lines match the reference");
    }

    [Fact]
    public async Task SpeakerLabelsStayAndSentencesAreCached()
    {
        var engine = new FakeEngine();
        var translator = new ArabicOfflineTranslator(() => engine);
        var result = await translator.TranslateAsync("ARTHUR: Stay close. Keep moving.");
        Assert.StartsWith("ARTHUR: ", result);
        Assert.Contains("[Stay close.]", result);
        Assert.Contains("[Keep moving.]", result);
        await translator.TranslateAsync("ARTHUR: Stay close. Keep moving.");
        Assert.Equal(2, engine.Calls);
    }

    [Theory]
    [InlineData("Turn off the lights.", "أطفئ الأنوار", "أطفئ الأنوار.")]
    [InlineData("Where are you going?", "إلى أين أنت ذاهب", "إلى أين أنت ذاهب؟")]
    [InlineData("Watch out!", "احترس!", "احترس!")]
    [InlineData("Continue", "تابع", "تابع")]
    [InlineData("Not again.", "ليس مجدداً", "ليس مجدداً.")]
    public void MissingEndMarkIsAdded(string english, string arabic, string expected) =>
        Assert.Equal(expected, ArabicOfflineTranslator.EndLike(english, arabic));

    [Fact]
    public void DeepSeekRequestUsesItsEndpointAndTurnsThinkingOff()
    {
        var model = AiProvider.DeepSeek.DefaultModel();
        Assert.Equal("deepseek-flash", model.Id);
        using var request = AiRequest.Build(AiProvider.DeepSeek, " sk-test ", model, ["Get in the car."], []);
        Assert.Equal("https://api.deepseek.com/chat/completions", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer sk-test", string.Join("", request.Headers.GetValues("Authorization")));
        using var body = System.Text.Json.JsonDocument.Parse(request.Content!.ReadAsStringAsync().Result);
        var root = body.RootElement;
        Assert.Equal("deepseek-flash", root.GetProperty("model").GetString());
        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Contains("Arabic", root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Contains("into Arabic", root.GetProperty("messages")[1].GetProperty("content").GetString());
        // The answer is read like OpenAI's.
        Assert.Equal("[\"اركب السيارة.\"]", AiRequest.Text(AiProvider.DeepSeek,
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"[\\\"اركب السيارة.\\\"]\"}}]}"));
    }

    private sealed class FakeEngine : ISentenceEngine
    {
        public int Calls;
        public string Translate(string sentence, CancellationToken cancellationToken)
        {
            Calls++;
            return $"[{sentence}]";
        }
        public void Dispose() { }
    }
}
