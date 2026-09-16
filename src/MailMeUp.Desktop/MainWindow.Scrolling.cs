using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private ScrollBar? _pageVerticalScrollBar;
    private ScrollBar? _settingsVerticalScrollBar;
    private ScrollViewer? _trackedSettingsScroll;
    private UIElement? _settingsScrollPointerSurface;
    private bool _pageScrollWindowActive;
    private bool _pageScrollPointerInside;
    private bool _pageScrollKeyboardInput;
    private bool _pageScrollTouchInput;
    private bool _pageScrollTouchActive;

    private static readonly RoutedEvent[] PageScrollPointerEvents =
    [
        UIElement.PointerEnteredEvent, UIElement.PointerMovedEvent,
        UIElement.PointerReleasedEvent, UIElement.PointerCanceledEvent, UIElement.PointerCaptureLostEvent
    ];

    private void InitializePageScrolling()
    {
        foreach (var pointerEvent in PageScrollPointerEvents)
            Root.AddHandler(pointerEvent, new PointerEventHandler(PageScrollRoot_PointerChanged), true);
        Root.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(PageScrollRoot_PointerExited), true);
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(PageScrollRoot_KeyDown), true);
        PageScroll.Loaded += PageScroll_Loaded;
        PageScroll.ActualThemeChanged += PageScroll_ActualThemeChanged;
        PageScroll.ViewChanged += PageScroll_ViewChanged;
        Activated += PageScrolling_WindowActivated;
        Closed += PageScrolling_WindowClosed;
    }

    private void RefreshPageScrollChrome()
    {
        if (_lifetime.IsCancellationRequested) return;
        _pageVerticalScrollBar ??= FindNativeVerticalScrollBar(PageScroll, PageScroll.Content);
        if (_trackedSettingsScroll is { } settingsScroll)
            _settingsVerticalScrollBar ??= FindNativeVerticalScrollBar(settingsScroll, settingsScroll.Content);

        // Change only the native bar's opacity: its layout, hit testing, bindings and
        // ScrollViewer input behavior stay intact. High contrast keeps native visibility.
        var show = _accessibility.HighContrast || (_pageScrollWindowActive &&
            (_pageScrollPointerInside || _pageScrollKeyboardInput || _pageScrollTouchActive));
        if (_pageVerticalScrollBar is { } pageBar) pageBar.Opacity = show ? 1 : 0;
        if (_settingsVerticalScrollBar is { } settingsBar) settingsBar.Opacity = show ? 1 : 0;
    }

    private static ScrollBar? FindNativeVerticalScrollBar(DependencyObject parent, object? content)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (ReferenceEquals(child, content)) continue;
            if (child is ScrollBar { Name: "VerticalScrollBar" } scrollBar) return scrollBar;
            if (FindNativeVerticalScrollBar(child, content) is { } match) return match;
        }
        return null;
    }

    private void TrackSettingsScrollViewer(ScrollViewer scroll)
    {
        if (_trackedSettingsScroll is { } previous) UntrackSettingsScrollViewer(previous);
        _trackedSettingsScroll = scroll;
        scroll.Loaded += SettingsScroll_Loaded;
        scroll.ActualThemeChanged += SettingsScroll_ActualThemeChanged;
        scroll.ViewChanged += PageScroll_ViewChanged;
        SetSettingsScrollPointerSurface(scroll);
    }

    private void UntrackSettingsScrollViewer(ScrollViewer scroll)
    {
        if (!ReferenceEquals(scroll, _trackedSettingsScroll)) return;
        scroll.Loaded -= SettingsScroll_Loaded;
        scroll.ActualThemeChanged -= SettingsScroll_ActualThemeChanged;
        scroll.ViewChanged -= PageScroll_ViewChanged;
        SetSettingsScrollPointerSurface(null);
        if (_settingsVerticalScrollBar is { } scrollBar) scrollBar.Opacity = 1;
        _settingsVerticalScrollBar = null;
        _trackedSettingsScroll = null;
    }

    private void SettingsScroll_Loaded(object sender, RoutedEventArgs args)
    {
        // ContentDialog lives in a popup, so its routed input does not reach Root.
        SetSettingsScrollPointerSurface((UIElement?)_settingsDialog ?? (UIElement?)_sharingDialog
            ?? (UIElement?)_addAccountDialog ?? _trackedSettingsScroll);
        _settingsVerticalScrollBar = null;
        RefreshPageScrollChrome();
    }

    private void SettingsScroll_ActualThemeChanged(FrameworkElement sender, object args)
    {
        _settingsVerticalScrollBar = null;
        RefreshPageScrollChrome();
    }

    private void SetSettingsScrollPointerSurface(UIElement? surface)
    {
        if (_settingsScrollPointerSurface is { } previous)
        {
            foreach (var pointerEvent in PageScrollPointerEvents)
                previous.RemoveHandler(pointerEvent, new PointerEventHandler(SettingsScroll_PointerChanged));
            previous.RemoveHandler(UIElement.PointerExitedEvent, new PointerEventHandler(SettingsScroll_PointerExited));
            previous.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(PageScrollRoot_KeyDown));
        }
        _settingsScrollPointerSurface = surface;
        if (surface is null) return;
        foreach (var pointerEvent in PageScrollPointerEvents)
            surface.AddHandler(pointerEvent, new PointerEventHandler(SettingsScroll_PointerChanged), true);
        surface.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(SettingsScroll_PointerExited), true);
        surface.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(PageScrollRoot_KeyDown), true);
    }

    private void SettingsScroll_PointerChanged(object sender, PointerRoutedEventArgs args)
    {
        _pageScrollKeyboardInput = false;
        _pageScrollTouchInput = args.Pointer.PointerDeviceType == PointerDeviceType.Touch;
        _pageScrollTouchActive = _pageScrollTouchInput && args.GetCurrentPoint(null).IsInContact;
        _pageScrollPointerInside = !_pageScrollTouchInput && IsCursorInsidePageWindow();
        RefreshPageScrollChrome();
    }

    private void SettingsScroll_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        SettingsScroll_PointerChanged(sender, args);
        if (!ReferenceEquals(args.OriginalSource, _settingsScrollPointerSurface)) return;
        _pageScrollPointerInside = _pageScrollTouchActive = false;
        RefreshPageScrollChrome();
    }

    private void PageScroll_Loaded(object sender, RoutedEventArgs args)
    {
        _pageVerticalScrollBar = null;
        _pageScrollPointerInside = IsCursorInsidePageWindow();
        RefreshPageScrollChrome();
    }

    private void PageScroll_ActualThemeChanged(FrameworkElement sender, object args)
    {
        _pageVerticalScrollBar = null;
        RefreshPageScrollChrome();
    }

    private void PageScrollRoot_PointerChanged(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(Root);
        _pageScrollKeyboardInput = false;
        _pageScrollTouchInput = args.Pointer.PointerDeviceType == PointerDeviceType.Touch;
        _pageScrollTouchActive = _pageScrollTouchInput && point.IsInContact;
        // Bounds also cover captured drags, whose PointerExited event may be suppressed,
        // and ignore exits bubbled from a child while the pointer is still in the window.
        _pageScrollPointerInside = !_pageScrollTouchInput &&
            point.Position.X >= 0 && point.Position.X < Root.ActualWidth &&
            point.Position.Y >= 0 && point.Position.Y < Root.ActualHeight;
        RefreshPageScrollChrome();
    }

    private void PageScrollRoot_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is not (VirtualKey.Tab or VirtualKey.Up or VirtualKey.Down or
            VirtualKey.Left or VirtualKey.Right or VirtualKey.PageUp or VirtualKey.PageDown or
            VirtualKey.Home or VirtualKey.End or VirtualKey.Space)) return;
        _pageScrollKeyboardInput = true;
        _pageScrollTouchInput = _pageScrollTouchActive = false;
        RefreshPageScrollChrome();
    }

    private void PageScrollRoot_PointerExited(object sender, PointerRoutedEventArgs args)
    {
        PageScrollRoot_PointerChanged(sender, args);
        if (!ReferenceEquals(args.OriginalSource, Root)) return;
        // Native caption regions can stop XAML pointer events before the cursor leaves
        // the window bounds. A root exit hides chrome there; child exits do not.
        _pageScrollPointerInside = _pageScrollTouchActive = false;
        RefreshPageScrollChrome();
    }

    private void PageScroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (_pageScrollTouchInput) _pageScrollTouchActive = args.IsIntermediate;
        RefreshPageScrollChrome();
    }

    private void PageScrolling_WindowActivated(object sender, WindowActivatedEventArgs args)
    {
        _pageScrollWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        _pageScrollPointerInside = _pageScrollWindowActive && IsCursorInsidePageWindow();
        if (!_pageScrollWindowActive)
            _pageScrollKeyboardInput = _pageScrollTouchInput = _pageScrollTouchActive = false;
        RefreshPageScrollChrome();
    }

    private bool IsCursorInsidePageWindow()
    {
        if (!GetCursorPos(out var point) ||
            !ScreenToClient(WinRT.Interop.WindowNative.GetWindowHandle(this), ref point)) return false;
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        return point.X >= 0 && point.Y >= 0 &&
            point.X < Root.ActualWidth * scale && point.Y < Root.ActualHeight * scale;
    }

    private void PageScrolling_WindowClosed(object sender, WindowEventArgs args)
    {
        if (_trackedSettingsScroll is { } settingsScroll) UntrackSettingsScrollViewer(settingsScroll);
        foreach (var pointerEvent in PageScrollPointerEvents)
            Root.RemoveHandler(pointerEvent, new PointerEventHandler(PageScrollRoot_PointerChanged));
        Root.RemoveHandler(UIElement.PointerExitedEvent, new PointerEventHandler(PageScrollRoot_PointerExited));
        Root.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(PageScrollRoot_KeyDown));
        PageScroll.Loaded -= PageScroll_Loaded;
        PageScroll.ActualThemeChanged -= PageScroll_ActualThemeChanged;
        PageScroll.ViewChanged -= PageScroll_ViewChanged;
        Activated -= PageScrolling_WindowActivated;
        Closed -= PageScrolling_WindowClosed;
        _pageVerticalScrollBar = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PageScrollCursorPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out PageScrollCursorPoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr window, ref PageScrollCursorPoint point);
}
