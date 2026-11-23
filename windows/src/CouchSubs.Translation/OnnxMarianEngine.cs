// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork (November 2026). Runs the offline English→Arabic model opus-mt-tc-big-en-ar (Helsinki-NLP / OPUS-MT,
// Jörg Tiedemann et al., CC-BY-4.0) in the ONNX layout Faz3a exported it to: encoder.onnx turns a sentence into the
// cross-attention keys/values of every decoder layer once, step.onnx predicts one target token at a time with a
// key/value cache. Greedy decoding, the same as the export's reference runner (live-draft/draft_rt.py).

using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace CouchSubs.Translation;

/// <summary>Where the offline Arabic model lives and whether all of its files are there.</summary>
public static class ArabicModelFiles
{
    public const string FolderName = "opus-mt-en-ar";
    public static readonly string[] Required = ["encoder.onnx", "step.onnx", "meta.json", "source.spm", "vocab.json"];

    public static bool IsComplete(string? folder) =>
        folder is not null && Required.All(name => File.Exists(Path.Combine(folder, name)));

    /// <summary>
    /// First complete copy among: the <c>FAZ3A_SUBS_AR_MODEL</c> environment variable, <c>models\opus-mt-en-ar</c> next
    /// to the app, and the user's data folder. Null when none is complete.
    /// </summary>
    public static string? Find(string dataModelsFolder)
    {
        IEnumerable<string?> candidates =
        [
            Environment.GetEnvironmentVariable("FAZ3A_SUBS_AR_MODEL"),
            Path.Combine(AppContext.BaseDirectory, "models", FolderName),
            Path.Combine(dataModelsFolder, FolderName),
        ];
        return candidates.FirstOrDefault(IsComplete);
    }
}

/// <summary>Model constants written by the exporter (meta.json).</summary>
internal sealed record ArabicModelMeta(int Layers, int Heads, int HeadDim, long Start, long Eos, long Pad, long[] Bad)
{
    public static ArabicModelMeta Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        return new ArabicModelMeta(root.GetProperty("layers").GetInt32(), root.GetProperty("heads").GetInt32(),
            root.GetProperty("head_dim").GetInt32(), root.GetProperty("start").GetInt64(), root.GetProperty("eos").GetInt64(),
            root.GetProperty("pad").GetInt64(),
            root.TryGetProperty("bad", out var bad) ? [.. bad.EnumerateArray().Select(v => v.GetInt64())] : []);
    }
}

/// <summary>
/// One English sentence in, one Arabic sentence out. Not thread-safe: the translator calls it under its own lock (the
/// model already uses several CPU threads per sentence; running two at once only slows both down).
/// </summary>
internal sealed class OnnxMarianEngine : ISentenceEngine
{
    /// <summary>OPUS-MT's word-start mark; becomes a space when decoding.</summary>
    private const char WordStart = '▁';
    /// <summary>Target-language token the multilingual tc-big model needs at the start of the source.</summary>
    private const string ArabicTag = ">>ara<<";
    /// <summary>Source length limit (tokens), as in the upstream engines.</summary>
    private const int MaxSourceTokens = 256;

    private readonly InferenceSession encoder;
    private readonly InferenceSession step;
    private readonly ArabicModelMeta meta;
    private readonly SentencePieceTokenizer pieces;
    private readonly Dictionary<string, int> vocab;
    private readonly string?[] idToPiece;
    private readonly long unknownId;
    private readonly long arabicId;
    private readonly string[] stepInputNames;
    private readonly string[] stepOutputNames;
    private readonly string[] encoderOutputNames;
    private bool disposed;

    public OnnxMarianEngine(string folder, int threads)
    {
        meta = ArabicModelMeta.Load(Path.Combine(folder, "meta.json"));
        using (var spm = File.OpenRead(Path.Combine(folder, "source.spm")))
            pieces = SentencePieceTokenizer.Create(spm, addBeginningOfSentence: false, addEndOfSentence: false);
        vocab = ReadVocab(Path.Combine(folder, "vocab.json"));
        idToPiece = new string?[vocab.Values.Max() + 1];
        foreach (var (piece, id) in vocab) idToPiece[id] = piece;
        unknownId = vocab.TryGetValue("<unk>", out var unk) ? unk : 1;
        arabicId = vocab.TryGetValue(ArabicTag, out var tag) ? tag
            : throw new InvalidDataException("vocab.json has no " + ArabicTag + " token");

        using var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(threads, 1, Environment.ProcessorCount),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };
        encoder = new InferenceSession(Path.Combine(folder, "encoder.onnx"), options);
        try { step = new InferenceSession(Path.Combine(folder, "step.onnx"), options); }
        catch { encoder.Dispose(); throw; }

        var layers = Enumerable.Range(0, meta.Layers).ToArray();
        encoderOutputNames = [.. layers.SelectMany(i => new[] { $"ck{i}", $"cv{i}" })];
        stepInputNames = ["token", "position", "src_mask", .. layers.SelectMany(i => new[] { $"sk{i}", $"sv{i}", $"ck{i}", $"cv{i}" })];
        stepOutputNames = ["logits", .. layers.SelectMany(i => new[] { $"new_sk{i}", $"new_sv{i}" })];
    }

    private static Dictionary<string, int> ReadVocab(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in doc.RootElement.EnumerateObject()) result[entry.Name] = entry.Value.GetInt32();
        if (result.Count == 0) throw new InvalidDataException("vocab.json is empty");
        return result;
    }

    /// <summary>Source ids: <c>&gt;&gt;ara&lt;&lt;</c>, the sentence's pieces, end of sentence.</summary>
    internal long[] Encode(string text)
    {
        var tokens = pieces.EncodeToTokens(text.Trim(), out _, addBeginningOfSentence: false, addEndOfSentence: false,
            considerPreTokenization: true, considerNormalization: true);
        var ids = new List<long>(tokens.Count + 2) { arabicId };
        var unknown = pieces.UnknownId;
        for (var i = 0; i < tokens.Count; i++)
        {
            var piece = tokens[i].Value;
            if (tokens[i].Id == unknown)
            {
                // SentencePiece joins a run of unknown characters into one piece; this library splits them. The joined
                // run is looked up in vocab.json (same rule as the upstream MarianTokenizer).
                var run = new StringBuilder(piece);
                while (i + 1 < tokens.Count && tokens[i + 1].Id == unknown) run.Append(tokens[++i].Value);
                piece = run.ToString();
            }
            ids.Add(vocab.TryGetValue(piece, out var id) ? id : unknownId);
        }
        if (ids.Count > MaxSourceTokens) ids.RemoveRange(MaxSourceTokens - 1, ids.Count - (MaxSourceTokens - 1));
        ids.Add(meta.Eos);
        return [.. ids];
    }

    /// <summary>Target ids to text: special pieces (&lt;…&gt;) are dropped, word-start marks become spaces.</summary>
    internal string Decode(IEnumerable<long> ids)
    {
        var text = new StringBuilder();
        foreach (var id in ids)
        {
            if (id < 0 || id >= idToPiece.Length || idToPiece[id] is not { } piece || piece.StartsWith('<')) continue;
            text.Append(piece);
        }
        text.Replace(WordStart, ' ');
        return text.ToString().Trim();
    }

    public string Translate(string sentence, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var source = Encode(sentence);
        var length = source.Length;
        var mask = new long[length];
        Array.Fill(mask, 1L);
        long[] shape = [1, length];

        using var ids = OrtValue.CreateTensorValueFromMemory(source, shape);
        using var maskValue = OrtValue.CreateTensorValueFromMemory(mask, shape);
        using var runOptions = new RunOptions();
        using var cross = encoder.Run(runOptions, ["input_ids", "attention_mask"], [ids, maskValue], encoderOutputNames);

        // Self-attention cache starts empty ([1, heads, 0, head_dim]) and grows by one position per step.
        var empty = new List<OrtValue>();
        var cache = new OrtValue[2 * meta.Layers];
        for (var i = 0; i < cache.Length; i++)
        {
            cache[i] = OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, TensorElementType.Float,
                [1, meta.Heads, 0, meta.HeadDim]);
            empty.Add(cache[i]);
        }
        IDisposableReadOnlyCollection<OrtValue>? previous = null;
        var output = new List<long>();
        var token = new long[] { meta.Start };
        var position = new long[] { 0 };
        // A subtitle sentence rarely needs more than twice its source length; the cap also stops a looping decode.
        var maxSteps = Math.Min(200, 2 * length + 16);
        try
        {
            using var tokenValue = OrtValue.CreateTensorValueFromMemory(token, [1, 1]);
            using var positionValue = OrtValue.CreateTensorValueFromMemory(position, [1]);
            for (var t = 0; t < maxSteps; t++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                token[0] = output.Count == 0 ? meta.Start : output[^1];
                position[0] = t;
                var inputs = new List<OrtValue>(stepInputNames.Length) { tokenValue, positionValue, maskValue };
                for (var layer = 0; layer < meta.Layers; layer++)
                    inputs.AddRange([cache[2 * layer], cache[2 * layer + 1], cross[2 * layer], cross[2 * layer + 1]]);
                var results = step.Run(runOptions, stepInputNames, inputs, stepOutputNames);
                var next = ArgMax(results[0].GetTensorDataAsSpan<float>());
                for (var i = 0; i < cache.Length; i++) cache[i] = results[1 + i];
                // The previous step's outputs were this step's inputs; they can go now.
                previous?.Dispose();
                previous = results;
                if (next == meta.Eos) break;
                output.Add(next);
            }
        }
        finally
        {
            previous?.Dispose();
            foreach (var value in empty) value.Dispose();
        }
        return Decode(output);
    }

    private long ArgMax(ReadOnlySpan<float> logits)
    {
        var best = -1;
        var bestValue = float.NegativeInfinity;
        for (var i = 0; i < logits.Length; i++)
        {
            var value = logits[i];
            if (value > bestValue && !IsBad(i))
            {
                bestValue = value;
                best = i;
            }
        }
        return best < 0 ? meta.Eos : best;
    }

    private bool IsBad(long id)
    {
        foreach (var bad in meta.Bad) if (bad == id) return true;
        return false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        encoder.Dispose();
        step.Dispose();
    }
}
