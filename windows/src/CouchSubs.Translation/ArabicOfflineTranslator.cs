// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork (November 2026): offline English→Arabic translation with opus-mt-tc-big-en-ar (CC-BY-4.0, see
// THIRD-PARTY-NOTICES.md). Modeled on the upstream MarianTranslator, without its Turkish glossary and Turkish
// post-edits: speaker labels ("ARTHUR: ") stay as they are, ALL-CAPS subtitles are put in sentence case first (the model
// translates "WHERE ARE YOU GOING?" badly), every sentence is translated once and kept in a disk cache.

using System.Collections.Concurrent;
using System.Text;
using CouchSubs.Core;

namespace CouchSubs.Translation;

public sealed class ArabicOfflineTranslator : ITextTranslator, IDisposable
{
    /// <summary>Engine name in the persistent cache; changes whenever the model or the text steps change.</summary>
    public const string EngineId = "opus-mt-tc-big-en-ar-onnx-int8-v1";

    private readonly Func<ISentenceEngine> engineFactory;
    private readonly TranslationCache cache;
    private readonly ConcurrentDictionary<string, byte> names = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1, 1);
    private ISentenceEngine? engine;
    private Exception? engineFailure;
    private bool disposed;

    /// <summary>Raised once, from a background thread, when the model cannot be opened.</summary>
    public event Action<Exception>? EngineFailed;

    public ArabicOfflineTranslator(string modelFolder, TranslationCache? cache = null, int threads = 4)
        : this(() => new OnnxMarianEngine(modelFolder, threads), cache) { }

    internal ArabicOfflineTranslator(Func<ISentenceEngine> engineFactory, TranslationCache? cache = null)
    {
        this.engineFactory = engineFactory;
        this.cache = cache ?? new TranslationCache();
    }

    /// <summary>Loads the model and translates one word, so the first subtitle does not wait for loading.</summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        try { await TranslateSentenceAsync("Hello.", cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException) { /* EngineFailed already reported it */ }
    }

    public void NoteSpeaker(string name)
    {
        foreach (var word in name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2))
            names[word.ToLowerInvariant()] = 0;
    }

    public string? CachedTranslation(string text)
    {
        var pieces = SubtitleSegmenter.Split(text);
        var builder = new StringBuilder();
        foreach (var piece in pieces)
        {
            if (!piece.Translate) { builder.Append(piece.Text); continue; }
            if (!cache.TryGet(piece.Text, out var known)) return null;
            builder.Append(known);
        }
        return builder.ToString();
    }

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var pieces = SubtitleSegmenter.Split(text);
        foreach (var name in pieces.Select(SubtitleSegmenter.LabelName).OfType<string>()) NoteSpeaker(name);
        var builder = new StringBuilder();
        foreach (var piece in pieces)
        {
            if (!piece.Translate)
            {
                builder.Append(piece.Text);
                continue;
            }
            // The segmenter's own pieces carry the spaces, line breaks and labels between sentences.
            builder.Append(await TranslateSentenceAsync(piece.Text, cancellationToken).ConfigureAwait(false));
        }
        return builder.ToString().Trim();
    }

    private async Task<string> TranslateSentenceAsync(string sentence, CancellationToken cancellationToken)
    {
        if (cache.TryGet(sentence, out var known)) return known;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cache.TryGet(sentence, out var cached)) return cached;
            if (engineFailure is { } failed) throw new TranslatorException(TranslatorStatus.EngineFailed, failed);
            var current = engine ??= CreateEngine();
            var knownNames = names.Keys.ToHashSet(StringComparer.Ordinal);
            var input = GameEnglish.Plain(SubtitleSegmenter.NormalizeCase(sentence, knownNames));
            var translation = await Task.Run(() => current.Translate(input, cancellationToken), cancellationToken).ConfigureAwait(false);
            cache.Store(sentence, translation);
            return translation;
        }
        finally
        {
            gate.Release();
        }
    }

    private ISentenceEngine CreateEngine()
    {
        try
        {
            return engineFactory();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Volatile.Write(ref engineFailure, error);
            EngineFailed?.Invoke(error);
            throw new TranslatorException(TranslatorStatus.EngineFailed, error);
        }
    }

    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            engine?.Dispose();
            engine = null;
        }
        finally
        {
            gate.Release();
        }
    }
}
