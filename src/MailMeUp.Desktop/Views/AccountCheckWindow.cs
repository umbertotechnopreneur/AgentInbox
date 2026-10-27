// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Runtime.InteropServices;
using MailMeUp.Desktop.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace MailMeUp.Desktop.Views;

/// <summary>Renders the shared check model and forwards native input without owning mail-operation state.</summary>
internal sealed class AccountCheckWindow : Window, IDisposable
{
    private readonly AccountCheckModel _model;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ListView _folders = new() { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 220 };
    private readonly Button _primary = new() { MinWidth = 112 };
    private readonly Button _close = new() { MinWidth = 100 };
    private bool _renderingFolders;
    private bool _windowClosed;

    internal AccountCheckWindow(Window owner, AccountCheckModel model)
    {
        _model = model;
        Title = "AgentInbox";
        SystemBackdrop = new MicaBackdrop();
        var root = new Grid { Padding = new Thickness(24), RowSpacing = 18 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new Grid { ColumnSpacing = 14 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var ring = new ProgressRing { Width = 28, Height = 28 };
        var title = new TextBlock { FontSize = 24, TextWrapping = TextWrapping.Wrap };
        ModelBinding.Bind(ring, ProgressRing.IsActiveProperty, model, nameof(AccountCheckModel.IsWorking));
        ModelBinding.Bind(title, TextBlock.TextProperty, model, nameof(AccountCheckModel.Heading));
        heading.Children.Add(ring);
        Grid.SetColumn(title, 1);
        heading.Children.Add(title);
        root.Children.Add(heading);
        var body = new StackPanel { Spacing = 14 };
        foreach (var stage in model.Stages) AddStage(body, stage);
        var folderHeading = new TextBlock { Text = "Folders available to your assistant" };
        var readSample = new CheckBox { Content = "Also test reading one message" };
        body.Children.Add(folderHeading);
        body.Children.Add(_folders);
        body.Children.Add(readSample);
        ModelBinding.Visible(folderHeading, model, nameof(AccountCheckModel.ShowFolders));
        ModelBinding.Visible(_folders, model, nameof(AccountCheckModel.ShowFolders));
        ModelBinding.Visible(readSample, model, nameof(AccountCheckModel.ShowFolders));
        ModelBinding.Bind(_folders, ItemsControl.ItemsSourceProperty, model, nameof(AccountCheckModel.AvailableFolders));
        ModelBinding.Bind(_folders, Control.IsEnabledProperty, model, nameof(AccountCheckModel.CanSelectFolders));
        ModelBinding.Bind(readSample, Control.IsEnabledProperty, model, nameof(AccountCheckModel.ShowPrimary));
        ModelBinding.Bind(readSample, ToggleButton.IsCheckedProperty, model, nameof(AccountCheckModel.ReadMessageRequested), BindingMode.TwoWay);
        ToolTipService.SetToolTip(readSample, "Check reading with one message from the first selected folder. It won't be marked as read or deleted, and its text won't be saved or shown.");
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        AutomationProperties.SetName(message, "Connection check details");
        ModelBinding.Bind(message, TextBlock.TextProperty, model, nameof(AccountCheckModel.Message));
        var footer = new StackPanel { Spacing = 14 };
        footer.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] });
        footer.Children.Add(new ScrollViewer { Content = message, MaxHeight = 220,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        _primary.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        actions.Children.Add(_primary);
        actions.Children.Add(_close);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        root.SizeChanged += (_, args) => actions.Orientation = args.NewSize.Width < 460 ? Orientation.Vertical : Orientation.Horizontal;
        Content = root;
        BindButton(_primary, nameof(AccountCheckModel.PrimaryText), nameof(AccountCheckModel.PrimaryHint), nameof(AccountCheckModel.CanChooseFolders));
        BindButton(_close, nameof(AccountCheckModel.CloseText), nameof(AccountCheckModel.CloseHint), nameof(AccountCheckModel.CanClose));
        ModelBinding.Visible(_primary, model, nameof(AccountCheckModel.ShowPrimary));
        AutomationProperties.SetName(_folders, "Folders available to your assistant");
        _primary.Click += (_, _) => model.ChooseFolders();
        _close.Click += (_, _) => model.RequestClose();
        _folders.SelectionChanged += (_, _) =>
        {
            if (!_renderingFolders && !model.UpdatingFolders) model.SelectFolders(_folders.SelectedItems.Cast<string>().ToArray());
        };
        root.KeyDown += (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Escape) return;
            args.Handled = true;
            if (!model.IsSaving) model.RequestClose();
        };
        AppWindow.Closing += (_, args) =>
        {
            if (!model.IsWorking) return;
            args.Cancel = true;
            model.RequestClose();
        };
        Closed += (_, _) => { _windowClosed = true; model.Closed(); _closed.TrySetResult(); };
        model.PropertyChanged += ModelChanged;
        OwnAndPosition(this, owner);
    }

    internal Task ClosedTask => _closed.Task;

    private void BindButton(Button button, string text, string hint, string enabled)
    {
        ModelBinding.Bind(button, ContentControl.ContentProperty, _model, text);
        ModelBinding.Bind(button, AutomationProperties.NameProperty, _model, text);
        ModelBinding.Bind(button, ToolTipService.ToolTipProperty, _model, hint);
        ModelBinding.Bind(button, Control.IsEnabledProperty, _model, enabled);
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_windowClosed) return;
        _renderingFolders = true;
        try
        {
            _folders.SelectedItems.Clear();
            foreach (var folder in _model.SelectedFolders) _folders.SelectedItems.Add(folder);
        }
        finally { _renderingFolders = false; }
        if (_model.ShouldClose) Close();
    }

    private static void AddStage(StackPanel panel, CheckStageModel stage)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        ModelBinding.Bind(label, TextBlock.TextProperty, stage, nameof(CheckStageModel.Label));
        ModelBinding.Bind(status, TextBlock.TextProperty, stage, nameof(CheckStageModel.Status));
        ModelBinding.Visible(status, stage, nameof(CheckStageModel.ShowStatusText));
        var statusIcon = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        ModelBinding.Bind(statusIcon, TextBlock.TextProperty, stage, nameof(CheckStageModel.StatusGlyph));
        ModelBinding.Bind(statusIcon, TextBlock.ForegroundProperty, stage, nameof(CheckStageModel.StatusTone), converter: ModelBinding.ToneConverter.Instance);
        ModelBinding.Bind(statusIcon, AutomationProperties.NameProperty, stage, nameof(CheckStageModel.StatusDescription));
        ModelBinding.Bind(statusIcon, ToolTipService.ToolTipProperty, stage, nameof(CheckStageModel.StatusDescription));
        row.Children.Add(label);
        Grid.SetColumn(status, 1);
        row.Children.Add(status);
        Grid.SetColumn(statusIcon, 1);
        row.Children.Add(statusIcon);
        panel.Children.Add(row);
    }

    public void Dispose() => _model.PropertyChanged -= ModelChanged;

    /// <summary>Uses the owner relationship for native modality and centers the window within its display.</summary>
    internal static void OwnAndPosition(Window window, Window owner)
    {
        window.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var surface = new Grid();
        surface.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        surface.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var titleBar = new Grid { Height = 40, Padding = new Thickness(12, 0, 140, 0), ColumnSpacing = 10, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.Children.Add(new Image { Width = 20, Height = 20,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.png"))) });
        var title = new TextBlock { Text = "AgentInbox", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        titleBar.Children.Add(title);
        surface.Children.Add(titleBar);
        Grid.SetRow(content, 1);
        surface.Children.Add(content);
        window.Content = surface;
        window.SetTitleBar(titleBar);
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var ownerHandle = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtr(handle, -8, ownerHandle) == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            throw new InvalidOperationException("The account check window could not be attached to its owner.");
        var presenter = OverlappedPresenter.CreateForDialog();
        window.AppWindow.SetPresenter(presenter);
        presenter.IsModal = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        var scale = owner.Content.XamlRoot?.RasterizationScale ?? throw new InvalidOperationException("The account form is not ready.");
        var area = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)Math.Round(600 * scale), area.Width);
        var height = Math.Min((int)Math.Round(640 * scale), area.Height);
        var x = Math.Clamp(owner.AppWindow.Position.X + (owner.AppWindow.Size.Width - width) / 2, area.X, area.X + area.Width - width);
        var y = Math.Clamp(owner.AppWindow.Position.Y + (owner.AppWindow.Size.Height - height) / 2, area.Y, area.Y + area.Height - height);
        window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
