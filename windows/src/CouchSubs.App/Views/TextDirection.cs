// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork: Arabic subtitles must be laid out right to left. WPF already shapes and joins Arabic letters; what it
// needs is the paragraph direction, otherwise the final full stop, question mark or a name in Latin letters lands on
// the wrong side of the line.

using System.Windows;

namespace CouchSubs.App.Views;

internal static class TextDirection
{
    /// <summary>True when the text contains Arabic letters (Arabic, Arabic Supplement, presentation forms).</summary>
    public static bool IsArabic(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var c in text)
            if (c is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ࢠ' and <= 'ࣿ'
                or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿') return true;
        return false;
    }

    /// <summary>Right to left for Arabic text, left to right for anything else (English source lines).</summary>
    public static FlowDirection For(string? text) => IsArabic(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>Sets the element's direction from the text it shows; returns the element for chaining.</summary>
    public static T Apply<T>(T element, string? text) where T : FrameworkElement
    {
        element.FlowDirection = For(text);
        return element;
    }
}
