// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork: speed numbers for the subtitle area, written to altyazi.log every 10 seconds while frames arrive:
// frames per second that reached the subtitle reader, OCR reads per second (frames whose subtitle area changed and was
// actually read; unchanged frames reuse the last reading) and the average time of one read.

using System.Globalization;

namespace CouchSubs.Core;

public static class ReadStats
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly object Gate = new();
    private static long frames, reads, readMs;
    private static DateTimeOffset windowStart = DateTimeOffset.UtcNow;
    private static Timer? timer;

    /// <summary>OCR reads per second over the last 10-second window (0 when no frames arrived).</summary>
    public static double LastReadsPerSecond { get; private set; }

    public static void Frame()
    {
        lock (Gate)
        {
            frames++;
            timer ??= new Timer(_ => Report(), null, Interval, Interval);
        }
    }

    public static void Read(int ms)
    {
        lock (Gate)
        {
            reads++;
            readMs += ms;
        }
    }

    private static void Report()
    {
        long f, r, ms;
        double seconds;
        lock (Gate)
        {
            var now = DateTimeOffset.UtcNow;
            seconds = Math.Max(0.001, (now - windowStart).TotalSeconds);
            (f, r, ms) = (frames, reads, readMs);
            frames = reads = readMs = 0;
            windowStart = now;
        }
        LastReadsPerSecond = r / seconds;
        if (f == 0) return;
        var c = CultureInfo.InvariantCulture;
        SubtitleLog.Write(string.Format(c, "stats: {0:0.0} frames/s, {1:0.0} OCR reads/s, OCR {2} ms avg",
            f / seconds, r / seconds, r == 0 ? 0 : ms / r));
    }
}
