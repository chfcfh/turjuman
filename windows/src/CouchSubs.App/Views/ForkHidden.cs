// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork: parts of the upstream interface that belong to PS5, Xbox, TV, game packs and Turkish dubbing are kept
// hidden. The upstream code still sets their visibility in many places; this keeps them collapsed whatever it sets.

using System.ComponentModel;
using System.Windows;

namespace CouchSubs.App.Views;

internal static class ForkHidden
{
    private static readonly DependencyPropertyDescriptor Visibility =
        DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement));

    public static void Keep(params UIElement?[] elements)
    {
        foreach (var element in elements)
        {
            if (element is null) continue;
            element.Visibility = System.Windows.Visibility.Collapsed;
            Visibility.AddValueChanged(element, Collapse);
        }
    }

    private static void Collapse(object? sender, EventArgs e)
    {
        if (sender is UIElement { Visibility: not System.Windows.Visibility.Collapsed } element)
            element.Visibility = System.Windows.Visibility.Collapsed;
    }
}
