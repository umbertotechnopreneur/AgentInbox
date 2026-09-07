using MailMeUp.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MailMeUp.Desktop;

/// <summary>Edits an explicit calendar selection locally, with search and bounded pages.</summary>
public sealed class CalendarSelectionDialog : ContentDialog
{
    private const int PageSize = 5;
    private readonly IReadOnlyList<ProviderCalendar> _available;
    private readonly HashSet<string> _selected;
    private readonly TextBox _search = new() { PlaceholderText = "Find a calendar" };
    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly TextBlock _summary = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _pageText = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _previous = new() { Content = "Previous" };
    private readonly Button _next = new() { Content = "Next calendars" };
    private readonly Grid _pager = new();
    private int _page;

    /// <summary>Creates a picker without reading provider data or changing persisted sharing settings.</summary>
    public CalendarSelectionDialog(IReadOnlyList<ProviderCalendar> available, IEnumerable<string> selectedIds, bool selectAllAvailable)
    {
        _available = available;
        // Retain saved IDs that discovery cannot currently return.
        _selected = selectedIds.ToHashSet(StringComparer.Ordinal);
        if (selectAllAvailable) _selected.UnionWith(available.Select(calendar => calendar.ProviderCalendarId));
        Title = "Choose calendars";
        PrimaryButtonText = "Use selection";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        Resources["ContentDialogMaxWidth"] = 620.0;

        var panel = new StackPanel { Spacing = 12, MinWidth = 280 };
        panel.Children.Add(new TextBlock
        {
            Text = "Only calendar names are loaded here. Nothing is shared until you save the account's choices.",
            TextWrapping = TextWrapping.Wrap
        });
        AutomationProperties.SetName(_search, "Find a calendar");
        AutomationProperties.SetLiveSetting(_summary, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        panel.Children.Add(_search);
        panel.Children.Add(_rows);
        _pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _pager.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _pager.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_pageText, 1);
        Grid.SetColumn(_next, 2);
        _pager.Children.Add(_previous);
        _pager.Children.Add(_pageText);
        _pager.Children.Add(_next);
        panel.Children.Add(_pager);
        panel.Children.Add(_summary);
        Content = new ScrollViewer { Content = panel, MaxHeight = 420, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _search.TextChanged += (_, _) => { _page = 0; RenderPage(); };
        _previous.Click += (_, _) => { _page--; RenderPage(); };
        _next.Click += (_, _) => { _page++; RenderPage(); };
        RenderPage();
    }

    /// <summary>Gets the draft selection, including saved IDs missing from current discovery.</summary>
    public IReadOnlyCollection<string> SelectedCalendarIds => _selected.ToArray();

    private void RenderPage()
    {
        var matching = _available.Where(calendar => calendar.Name.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        _page = Math.Clamp(_page, 0, Math.Max(0, (matching.Length - 1) / PageSize));
        _rows.Children.Clear();
        foreach (var calendar in matching.Skip(_page * PageSize).Take(PageSize))
        {
            var label = calendar.Name + (calendar.Primary ? " (primary)" : string.Empty);
            var checkbox = new CheckBox
            {
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                IsChecked = _selected.Contains(calendar.ProviderCalendarId),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(checkbox, label);
            checkbox.Checked += (_, _) => { _selected.Add(calendar.ProviderCalendarId); UpdateSummary(); };
            checkbox.Unchecked += (_, _) => { _selected.Remove(calendar.ProviderCalendarId); UpdateSummary(); };
            _rows.Children.Add(checkbox);
        }
        if (matching.Length == 0)
            _rows.Children.Add(new TextBlock { Text = _available.Count == 0 ? "No calendars returned. Saved selections are kept." : "No matching calendars.", TextWrapping = TextWrapping.Wrap });
        _pager.Visibility = matching.Length > PageSize ? Visibility.Visible : Visibility.Collapsed;
        _previous.IsEnabled = _page > 0;
        _next.IsEnabled = (_page + 1) * PageSize < matching.Length;
        _pageText.Text = $"{_page * PageSize + 1}–{Math.Min((_page + 1) * PageSize, matching.Length)} of {matching.Length}";
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        _summary.Text = _selected.Count > 100
            ? "Choose up to 100 individual calendars, or use all calendars in the account settings."
            : $"{_selected.Count} selected. Use selection returns these choices to the account editor.";
        IsPrimaryButtonEnabled = _available.Count > 0 && _selected.Count <= 100;
    }
}

