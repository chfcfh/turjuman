// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork: keeps the live whole-screen translations steady. The whole frame is read again and again (LiveScreen in the
// shell) and every text block gets one translation over its own text. A friend's test of the first in-place version found
// two translations stacked on one text (separate readers each translating it) and translations that kept vanishing (a
// slow or missed read removed them). Here one board holds them all: a new translation replaces whatever lay at its place
// or showed the same text, and a translation stays until its text has been missing from several reads in a row.

namespace CouchSubs.Core;

public sealed class LiveScreenBoard
{
    /// <summary>Region id of the live screen translations (an automatic area: never drawn in the subtitle bar).</summary>
    public const string Id = ScreenTextAreas.Prefix + "live";
    /// <summary>A translation stays at least this long after its text was last read…</summary>
    public const int HoldMs = 1500;
    /// <summary>…and through at least this many complete reads that did not find it.</summary>
    public const int HoldReads = 2;
    /// <summary>Two reads of one line: their boxes overlap at least this much of the smaller one…</summary>
    public const double SamePlace = 0.5;
    /// <summary>…and their texts are at least this similar. The earlier spelling is kept, so a wobbly read is not translated again.</summary>
    public const double SameText = 0.85;
    /// <summary>Spellings are remembered this long after they were last read.</summary>
    public const int SpellingMs = 5000;

    private sealed class Entry(ScreenTranslation.Item item, long seen)
    {
        public ScreenTranslation.Item Item { get; } = item;
        public long Seen { get; } = seen;
        public int Misses { get; set; }
    }

    private readonly object gate = new();
    private readonly List<Entry> shown = [];
    private readonly List<(string Text, TextBox Box, long Seen)> spellings = [];

    /// <summary>The translations on screen now, top to bottom.</summary>
    public IReadOnlyList<ScreenTranslation.Item> Items
    {
        get { lock (gate) return Ordered(); }
    }

    /// <summary>
    /// The same line read a little differently (one letter, a dropped full stop) keeps its earlier spelling, so it is not
    /// sent to the translator again and its translation does not change on screen. Lines whose numbers differ are left
    /// as read (a price, a counter).
    /// </summary>
    public Reading Steady(Reading reading, long now)
    {
        lock (gate)
        {
            spellings.RemoveAll(item => now - item.Seen > SpellingMs);
            var lines = new List<TextLine>(reading.Lines.Count);
            foreach (var line in reading.Lines)
            {
                var kept = line;
                if (line.Box is { } box && line.Icons is not { Count: > 0 })
                {
                    var text = Key(line.Text);
                    var earlier = spellings.FindIndex(item => item.Text != line.Text && ScreenTranslation.Overlap(item.Box, box) >= SamePlace
                        && Digits(item.Text) == Digits(line.Text) && TextMatch.IsSimilar(Key(item.Text), text, SameText));
                    if (earlier >= 0) kept = Respelled(line, spellings[earlier].Text);
                }
                lines.Add(kept);
            }
            foreach (var line in lines)
            {
                if (line.Box is not { } box) continue;
                spellings.RemoveAll(item => item.Text == line.Text && ScreenTranslation.Overlap(item.Box, box) >= SamePlace);
                spellings.Add((line.Text, box, now));
            }
            return new Reading(lines);
        }
    }

    /// <summary>
    /// Puts a read's translations on the board and returns everything to draw. Each new translation replaces the ones at
    /// its place and the ones showing the same text elsewhere (moved or scrolled text). <paramref name="complete"/>: the
    /// whole read is translated; only then does a translation whose text was not found count a miss. While a read is
    /// still being translated the earlier translations stay, so nothing blinks while waiting for the translator.
    /// </summary>
    public IReadOnlyList<ScreenTranslation.Item> Merge(IReadOnlyList<ScreenTranslation.Item> fresh, long now, bool complete)
    {
        lock (gate)
        {
            var added = new HashSet<Entry>();
            foreach (var item in fresh)
            {
                var card = ScreenTranslation.IsCardPart(item);
                var source = Key(item.Source);
                shown.RemoveAll(old => !added.Contains(old)
                    && (ScreenTranslation.IsCardPart(old.Item) ? card
                        : !card && (ScreenTranslation.Overlap(old.Item.Box, item.Box) >= ScreenTranslation.CoverShare
                                    || (source.Length > 0 && Key(old.Item.Source) == source))));
                var entry = new Entry(item, now);
                shown.Add(entry);
                added.Add(entry);
            }
            if (complete)
            {
                foreach (var old in shown.Where(old => !added.Contains(old))) old.Misses++;
                shown.RemoveAll(old => !added.Contains(old) && old.Misses >= HoldReads && now - old.Seen >= HoldMs);
            }
            return Ordered();
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            shown.Clear();
            spellings.Clear();
        }
    }

    private List<ScreenTranslation.Item> Ordered() =>
        [.. shown.Select(entry => entry.Item).OrderBy(item => item.Box.Top).ThenBy(item => item.Box.Left)];

    private static string Key(string text) => TextMatch.Normalize(IconText.Plain(text));

    private static string Digits(string text) => new([.. text.Where(char.IsDigit)]);

    /// <summary>The line with the earlier spelling; word places are kept when the word count is the same.</summary>
    private static TextLine Respelled(TextLine line, string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var placed = line.Words is { } read && read.Count == words.Length
            ? read.Select((word, i) => word with { Text = words[i] }).ToList()
            : null;
        return line with { Text = text, Words = placed };
    }
}
