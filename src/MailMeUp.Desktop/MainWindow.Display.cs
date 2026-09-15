using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace MailMeUp.Desktop;

public sealed partial class MainWindow
{
    private XamlRoot? _displayXamlRoot;
    private double _displayScale;
    private Size _displaySize;
    private bool _displayRefreshQueued;

    private void ObserveDisplayRoot()
    {
        var root = Root.XamlRoot;
        if (root is null || ReferenceEquals(root, _displayXamlRoot)) return;
        StopObservingDisplayRoot();
        _displayXamlRoot = root;
        _displayScale = root.RasterizationScale;
        _displaySize = root.Size;
        root.Changed += DisplayRoot_Changed;
        QueueDisplayLayoutRefresh();
    }

    private void StopObservingDisplayRoot()
    {
        if (_displayXamlRoot is { } root) root.Changed -= DisplayRoot_Changed;
        _displayXamlRoot = null;
    }

    private void DisplayRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_lifetime.IsCancellationRequested || !ReferenceEquals(sender, _displayXamlRoot)) return;
        var scale = sender.RasterizationScale;
        var size = sender.Size;
        if (_displayScale == scale && _displaySize == size) return;
        if (_displayScale != scale)
            _logger.LogDebug("Desktop display scale changed from {PreviousScale} to {Scale}", _displayScale, scale);
        _displayScale = scale;
        _displaySize = size;
        QueueDisplayLayoutRefresh();
    }

    private void QueueDisplayLayoutRefresh()
    {
        if (_displayRefreshQueued || _lifetime.IsCancellationRequested) return;
        _displayRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            try
            {
                if (!_loaded || _displayXamlRoot is null || _lifetime.IsCancellationRequested) return;
                // XamlRoot can announce a new scale before the controls have their new
                // measured size. Let WinUI finish the native DPI transition first.
                Root.InvalidateMeasure();
                Root.InvalidateArrange();
                Root.UpdateLayout();
                UpdateTitleBar();
                UpdateLayout();
                _activeSetupDialog?.InvalidateMeasure();
                _activeSetupDialog?.InvalidateArrange();
                _pageVerticalScrollBar = null;
                _settingsVerticalScrollBar = null;
                _pageScrollPointerInside = IsCursorInsidePageWindow();
                RefreshPageScrollChrome();
                // XAML stays in DIPs: do not scale it again, resize the HWND, or
                // recreate pages/dialogs and lose focus, scroll position or drafts.
            }
            finally
            {
                _displayRefreshQueued = false;
            }
        })) _displayRefreshQueued = false;
    }
}
