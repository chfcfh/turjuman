// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork: the live whole-screen translations stay put and never stack (LiveScreenBoard).

using CouchSubs.Core;
using Xunit;

namespace CouchSubs.Core.Tests;

public class LiveScreenBoardTests
{
    private static ScreenTranslation.Item Item(string source, string translation, double left, double top, double right = -1, double bottom = -1) =>
        new(source, translation, new TextBox(left, top, right < 0 ? left + 0.2 : right, bottom < 0 ? top + 0.04 : bottom), 0.03);

    [Fact]
    public void TranslationStaysThroughAMissedRead()
    {
        var board = new LiveScreenBoard();
        board.Merge([Item("Hood", "غطاء المحرك", 0.1, 0.2)], 0, complete: true);
        // One read that did not find it, soon after: it stays.
        Assert.Single(board.Merge([], 400, complete: true));
        // A second read without it, but not yet HoldMs: it stays.
        Assert.Single(board.Merge([], 800, complete: true));
        // Two reads without it and HoldMs gone: it goes.
        Assert.Empty(board.Merge([], 1600, complete: true));
    }

    [Fact]
    public void FoundAgainStartsOver()
    {
        var board = new LiveScreenBoard();
        board.Merge([Item("Hood", "غطاء المحرك", 0.1, 0.2)], 0, complete: true);
        board.Merge([], 400, complete: true);
        board.Merge([Item("Hood", "غطاء المحرك", 0.1, 0.2)], 1000, complete: true);
        Assert.Single(board.Merge([], 1600, complete: true));
        Assert.Single(board.Merge([], 2000, complete: true));
        Assert.Empty(board.Merge([], 2600, complete: true));
    }

    [Fact]
    public void ReadStillBeingTranslatedRemovesNothing()
    {
        var board = new LiveScreenBoard();
        board.Merge([Item("Hood", "غطاء المحرك", 0.1, 0.2)], 0, complete: true);
        for (var time = 400; time <= 4000; time += 400) board.Merge([], time, complete: false);
        Assert.Single(board.Items);
    }

    [Fact]
    public void NewTextAtThePlaceReplacesTheOld()
    {
        var board = new LiveScreenBoard();
        board.Merge([Item("REPAIR VEHICLE FOR $110?", "إصلاح المركبة مقابل 110$؟", 0.05, 0.1)], 0, complete: true);
        var shown = board.Merge([Item("RESTORE PAINT JOB FOR $185?", "استعادة الطلاء مقابل 185$؟", 0.05, 0.1)], 500, complete: true);
        Assert.Equal("استعادة الطلاء مقابل 185$؟", Assert.Single(shown).Translation);
    }

    [Fact]
    public void MovedTextIsNotDrawnTwice()
    {
        var board = new LiveScreenBoard();
        board.Merge([Item("Full Kits", "أطقم كاملة", 0.1, 0.30)], 0, complete: true);
        // The menu scrolled: the same text one row lower, no longer overlapping its old place.
        var shown = board.Merge([Item("Full Kits", "أطقم كاملة", 0.1, 0.40)], 400, complete: true);
        Assert.Equal(0.40, Assert.Single(shown).Box.Top, 3);
    }

    [Fact]
    public void WaitingTranslationKeepsTheOldOneAndOthersAppear()
    {
        var board = new LiveScreenBoard();
        board.Merge([Item("Hood", "غطاء المحرك", 0.1, 0.2), Item("Roof", "السقف", 0.1, 0.3)], 0, complete: true);
        // Partial: "Roof" is known again, a new text at Hood's place is still with the translator.
        var shown = board.Merge([Item("Roof", "السقف", 0.1, 0.3)], 400, complete: false);
        Assert.Equal(2, shown.Count);
        Assert.Contains(shown, item => item.Translation == "غطاء المحرك");
    }

    [Fact]
    public void WobblyReadKeepsItsEarlierSpelling()
    {
        var board = new LiveScreenBoard();
        var box = new TextBox(0.1, 0.2, 0.3, 0.24);
        board.Steady(new Reading([new TextLine("Purchased", box)]), 0);
        var again = board.Steady(new Reading([new TextLine("Purdhased", box)]), 300);
        Assert.Equal("Purchased", again.Lines[0].Text);
    }

    [Fact]
    public void ChangedNumbersAreNotRespelled()
    {
        var board = new LiveScreenBoard();
        var box = new TextBox(0.1, 0.2, 0.3, 0.24);
        board.Steady(new Reading([new TextLine("0/33 Boosts", box)]), 0);
        var again = board.Steady(new Reading([new TextLine("1/33 Boosts", box)]), 300);
        Assert.Equal("1/33 Boosts", again.Lines[0].Text);
    }

    [Fact]
    public void DifferentTextAtThePlaceIsNotRespelled()
    {
        var board = new LiveScreenBoard();
        var box = new TextBox(0.1, 0.2, 0.3, 0.24);
        board.Steady(new Reading([new TextLine("Front Bumper", box)]), 0);
        var again = board.Steady(new Reading([new TextLine("Rear Bumper", box)]), 300);
        Assert.Equal("Rear Bumper", again.Lines[0].Text);
    }
}
