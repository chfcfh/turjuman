// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork: live translation of all the text on the screen, each block over its own text. Upstream translates the whole
// screen only when a key is pressed ("Translate the screen") and otherwise reads the subtitle plus a few kinds of on-screen
// text, each with its own reader; a friend's test found text left untranslated, two translations stacked on one text and
// translations vanishing. While the translation is drawn over the original text, this reads the whole frame again and again
// (with its own Windows text reader, so the subtitle's advanced reader is never kept waiting; one read at a time, at most
// every IntervalMs) and keeps the results on one LiveScreenBoard.
// Translations come from the same translator as the subtitles; texts read before come from memory and the disk cache, and
// all new texts of a read go to an AI translator in one request.

using CouchSubs.Core;

namespace CouchSubs.Shell;

internal sealed class LiveScreen
{
    /// <summary>A new read starts at most this often (the read before it must also be done and translated).</summary>
    public const int IntervalMs = 400;
    private const int StatsMs = 10_000;

    private readonly ITextReader reader;
    private readonly ITextTranslator translator;
    private readonly SubtitleServer server;
    private readonly Func<(bool PhoneCard, double PanelScale)> phone;
    private readonly LiveScreenBoard board = new();
    private readonly PhoneChatMemory memory = new();
    private readonly CancellationTokenSource stop = new();
    private readonly object sendGate = new();
    private int busy;
    private long lastStart = long.MinValue / 2;
    private bool sent;
    private long statsSince = Environment.TickCount64;
    private int reads;
    private long readMs, translateMs;
    private long lastError = long.MinValue / 2;

    public LiveScreen(ITextReader reader, ITextTranslator translator, SubtitleServer server, Func<(bool PhoneCard, double PanelScale)> phone)
    {
        this.reader = reader;
        this.translator = translator;
        this.server = server;
        this.phone = phone;
    }

    /// <summary>Called for every frame; starts a read in the background when one is due and none is running.</summary>
    public void Offer(Frame frame, long now)
    {
        if (stop.IsCancellationRequested || now - Volatile.Read(ref lastStart) < IntervalMs) return;
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        Volatile.Write(ref lastStart, now);
        _ = Task.Run(() => ReadAsync(frame));
    }

    /// <summary>The session stopped or the setting was turned off: the translations go away.</summary>
    public void Stop()
    {
        stop.Cancel();
        Clear();
    }

    /// <summary>The picture was lost: the translations go away; reading goes on with the next frame.</summary>
    public void Clear()
    {
        board.Clear();
        lock (sendGate)
        {
            if (sent) server.SendLiveScreen([]);
            sent = false;
        }
    }

    private async Task ReadAsync(Frame frame)
    {
        var token = stop.Token;
        try
        {
            var started = Environment.TickCount64;
            var reading = await ScreenTranslation.ReadAsync(frame, reader, null, token).ConfigureAwait(false);
            var read = Environment.TickCount64;
            reading = board.Steady(reading, read);
            var (card, scale) = phone();
            var items = await ScreenTranslation.TranslateAsync(reading, translator,
                partial => Show(board.Merge(partial, Environment.TickCount64, complete: false)),
                token, frame, memory, phoneCard: card, panelScale: scale).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Show(board.Merge(items, Environment.TickCount64, complete: true));
            Count(read - started, Environment.TickCount64 - read);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            // The translator may be offline for a moment: the translations on screen stay, the next read tries again.
            var now = Environment.TickCount64;
            if (now - lastError >= 60_000)
            {
                lastError = now;
                SubtitleLog.Write($"canlı ekran: okunamadı ya da çevrilemedi ({error.GetType().Name}: {error.Message})");
            }
        }
        finally
        {
            Volatile.Write(ref busy, 0);
        }
    }

    private void Show(IReadOnlyList<ScreenTranslation.Item> items)
    {
        lock (sendGate)
        {
            if (stop.IsCancellationRequested || (items.Count == 0 && !sent)) return;
            server.SendLiveScreen(items);
            sent = items.Count > 0;
        }
    }

    private void Count(long read, long translate)
    {
        reads++;
        readMs += read;
        translateMs += translate;
        var now = Environment.TickCount64;
        if (now - statsSince < StatsMs) return;
        SubtitleLog.Write($"canlı ekran: {reads} okuma / {(now - statsSince) / 1000} sn, okuma {readMs / Math.Max(1, reads)} ms, "
                          + $"çeviri {translateMs / Math.Max(1, reads)} ms ort., ekranda {board.Items.Count} çeviri");
        statsSince = now;
        reads = 0;
        readMs = translateMs = 0;
    }
}
