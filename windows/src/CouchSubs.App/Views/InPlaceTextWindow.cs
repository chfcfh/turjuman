// SPDX-License-Identifier: AGPL-3.0-only
//
// Faz3a fork: translations drawn over the very text they translate. A transparent, click-through, topmost layer lies
// exactly over the window that is being read (the game, or Discord showing a stream; CaptureTarget). The subtitle line
// covers the game's own subtitle; phone messages, extra areas and on-screen text sit on their bubbles and boxes. The
// owner asked for it after a stream test: phone text was translated into the bar at the bottom centre instead of over
// the phone. Positions are fractions of the captured frame, so they map straight onto the window.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CouchSubs.App.Services;
using CouchSubs.Core;
using CouchSubs.Shell;
using TextBox = CouchSubs.Core.TextBox;

namespace CouchSubs.App.Views;

internal sealed class InPlaceTextWindow : Window
{
    private readonly Canvas lineLayer = new() { IsHitTestVisible = false };
    private readonly PhoneBlocksCanvas blocksLayer = new() { IsHitTestVisible = false };
    private readonly DispatcherTimer follow = new() { Interval = TimeSpan.FromMilliseconds(250) };
    /// <summary>Back on top when a borderless fullscreen game or another topmost window comes forward (as the bar).</summary>
    private readonly TopmostKeeper topmost;
    private IntPtr handle;
    private (string Text, TextBox Box, SubtitleStyle Style, Action<TextBlock>? Fill)? line;
    private IReadOnlyList<ScreenTranslation.Item> items = [];
    private CaptureTarget.Bounds placed;

    public InPlaceTextWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Title = Fork.Name;
        Left = -10000;
        Top = -10000;
        Width = 10;
        Height = 10;
        var root = new Grid();
        root.Children.Add(blocksLayer);
        root.Children.Add(lineLayer);
        Content = root;
        lineLayer.SizeChanged += (_, _) => DrawLine();
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            var style = (long)GetWindowLongPtr(handle, GwlExStyle);
            // No focus, not in Alt+Tab, every click goes through to the game, and never part of a screen capture (so
            // reading the screen cannot read our own Arabic back).
            SetWindowLongPtr(handle, GwlExStyle, (IntPtr)(style | WsExNoActivate | WsExToolWindow | WsExTransparent | WsExLayered));
            SetWindowDisplayAffinity(handle, WdaExcludeFromCapture);
        };
        follow.Tick += (_, _) => Follow();
        topmost = new TopmostKeeper(this);
    }

    /// <summary>Whether there is a window to draw over (a window is being read and is not minimized).</summary>
    public static bool Available => CaptureTarget.Active;

    /// <summary>The subtitle line over the game's own subtitle; null removes it. <paramref name="fill"/> colours speaker names as in the bar.</summary>
    public void ShowLine(string? text, TextBox? box, SubtitleStyle style, Action<TextBlock>? fill = null)
    {
        line = text is { Length: > 0 } && box is { } where ? (text, where, style, fill) : null;
        Update();
    }

    /// <summary>Phone messages, extra areas and on-screen text, each on its own place.</summary>
    public void ShowItems(IReadOnlyList<ScreenTranslation.Item> shown)
    {
        items = shown;
        Update();
    }

    public void HideAll()
    {
        line = null;
        items = [];
        Update();
    }

    private void Update()
    {
        if (line is null && items.Count == 0)
        {
            follow.Stop();
            if (IsVisible) Hide();
            return;
        }
        if (!Follow()) return;
        blocksLayer.Show(items);
        DrawLine();
        follow.Start();
        topmost.RaiseThrottled();
    }

    /// <summary>Lies over the captured window; hidden while there is none (closed, minimized).</summary>
    private bool Follow()
    {
        if (!CaptureTarget.TryGetBounds(out var bounds))
        {
            if (IsVisible) Hide();
            return false;
        }
        if (!IsVisible) Show();
        if (handle != IntPtr.Zero && bounds != placed)
        {
            placed = bounds;
            SetWindowPos(handle, HwndTopmost, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SwpNoActivate | SwpShowWindow);
        }
        return true;
    }

    /// <summary>
    /// The line covers the game's subtitle: at least as big as it, centred on it, in the chosen text size and colours
    /// (Subtitle appearance). Arabic is laid out right to left.
    /// </summary>
    private void DrawLine()
    {
        lineLayer.Children.Clear();
        if (line is not { } shown) return;
        double width = lineLayer.ActualWidth, height = lineLayer.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var (text, box, style, fill) = shown;
        var scale = height / 1080.0;
        var size = Math.Max(10, OverlayPlacement.FontSize(style.FontSize) * scale);
        // Opaque: the English underneath must not show through the Arabic (tested: even at 92% the original subtitle could
        // still be made out behind the translation).
        const byte alpha = 255;
        var boxWidth = Math.Max(0, box.Right - box.Left) * width;
        var boxHeight = Math.Max(0, box.Bottom - box.Top) * height;
        var words = new TextBlock
        {
            Text = text,
            FlowDirection = TextDirection.For(text),
            FontSize = size,
            LineHeight = size * 1.3,
            FontWeight = style.Bold ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = new SolidColorBrush(SpeakerText.Rgb(style.TextRgb)),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 2 * scale, BlurRadius = 4 * scale, Opacity = 1, Direction = 270 },
        };
        fill?.Invoke(words);
        var block = new Border
        {
            Background = new SolidColorBrush(SpeakerText.Rgb(style.BackgroundRgb, alpha)),
            CornerRadius = new CornerRadius(8 * scale),
            Padding = new Thickness(18 * scale, 6 * scale, 18 * scale, 6 * scale),
            MinWidth = boxWidth,
            MinHeight = boxHeight,
            MaxWidth = width * 0.9,
            Child = words,
        };
        block.Measure(new Size(width * 0.9, double.PositiveInfinity));
        var desired = block.DesiredSize;
        var centerX = (box.Left + box.Right) / 2 * width;
        var centerY = (box.Top + box.Bottom) / 2 * height;
        Canvas.SetLeft(block, Math.Clamp(centerX - desired.Width / 2, 0, Math.Max(0, width - desired.Width)));
        Canvas.SetTop(block, Math.Clamp(centerY - desired.Height / 2, 0, Math.Max(0, height - desired.Height)));
        lineLayer.Children.Add(block);
    }

    protected override void OnClosed(EventArgs e)
    {
        follow.Stop();
        base.OnClosed(e);
    }

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExLayered = 0x80000, WsExNoActivate = 0x8000000;
    private const uint WdaExcludeFromCapture = 0x11;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x10, SwpShowWindow = 0x40;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
