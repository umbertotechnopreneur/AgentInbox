using MailMeUp.Application;
using MailMeUp.Core;

namespace MailMeUp.Desktop.Services;

/// <summary>Provides synthetic, in-memory setup data without opening providers, credentials or local storage.</summary>
public sealed class DemoMailMeUpApplication : IMailMeUpApplication
{
    private const string UnsupportedMessage =
        "This action is unavailable in demo mode. Demo accounts are examples and are not connected to any provider.";
    private readonly object _sync = new();
    private readonly List<Account> _accounts;
    private readonly Dictionary<string, AccountSharingSettings> _sharing;
    private readonly Dictionary<string, IReadOnlyList<ProviderCalendar>> _calendars;
    private MailSearchPreferences _mailSearchPreferences = new();

    /// <summary>Creates fresh sample accounts and sharing choices that last only for this preview session.</summary>
    public DemoMailMeUpApplication()
    {
        _accounts =
        [
            new("demo-personal", "google", "Personal (demo)", "personal@example.test", true, true),
            new("demo-work", "microsoft", "Work (demo)", "work@example.test", true, true),
            new("demo-client", "google", "Client (demo)", "client@example.test", true, false),
            new("demo-calendar", "microsoft", "Calendar only (demo)", "calendar@example.test", false, true)
        ];
        _sharing = new(StringComparer.Ordinal)
        {
            ["demo-personal"] = new("demo-personal"),
            ["demo-work"] = new("demo-work", CalendarIds: ["demo-work-primary", "demo-work-project"]),
            ["demo-client"] = new("demo-client", Enabled: false, ShareMail: true, ShareCalendars: false),
            ["demo-calendar"] = new("demo-calendar", ShareMail: false, ShareCalendars: true)
        };
        _calendars = new(StringComparer.Ordinal)
        {
            ["demo-personal"] =
            [
                new("demo-personal-primary", "Personal (demo)", true, "Europe/Rome"),
                new("demo-personal-family", "Family (demo)", false, "Europe/Rome"),
                new("demo-personal-holidays", "Holidays (demo)", false, "Europe/Rome")
            ],
            ["demo-work"] =
            [
                new("demo-work-primary", "Work calendar (demo)", true, "Europe/London"),
                new("demo-work-project", "Project delivery (demo)", false, "Europe/London"),
                new("demo-work-team", "Team availability (demo)", false, "Europe/London"),
                new("demo-work-travel", "Business travel (demo)", false, "Europe/London")
            ],
            ["demo-calendar"] =
            [
                new("demo-calendar-primary", "Appointments (demo)", true, "Asia/Ho_Chi_Minh"),
                new("demo-calendar-community", "Community events (demo)", false, "Asia/Ho_Chi_Minh")
            ]
        };
    }

    /// <summary>Identifies this application as an isolated UI preview with no real account connections.</summary>
    public bool IsDemo => true;

    /// <summary>Reports that the preview cannot authenticate or read mail and appointments.</summary>
    public ApplicationStatus GetStatus() => new(
        "ui_demo_no_provider_connections", "none", true, false,
        [
            new("google", "Google (demo only)", false, false, false),
            new("microsoft", "Microsoft (demo only)", false, false, false)
        ]);

    /// <summary>Returns sample account metadata from this preview session.</summary>
    public Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
            return Task.FromResult<IReadOnlyList<Account>>(_accounts.ToArray());
    }

    /// <summary>Returns sample accounts with the effective sharing categories chosen during this session.</summary>
    public Task<IReadOnlyList<Account>> ListSharedAccountsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var accounts = _accounts.Where(account => _sharing[account.Id].Enabled).Select(account => account with
            {
                MailReadEnabled = account.MailReadEnabled && _sharing[account.Id].ShareMail,
                CalendarReadEnabled = account.CalendarReadEnabled && _sharing[account.Id].ShareCalendars &&
                    _sharing[account.Id].CalendarIds is not { Count: 0 }
            }).ToArray();
            return Task.FromResult<IReadOnlyList<Account>>(accounts);
        }
    }

    /// <summary>Returns copies of the preview session's current sharing choices.</summary>
    public Task<IReadOnlyList<AccountSharingSettings>> ListAccountSharingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
            return Task.FromResult<IReadOnlyList<AccountSharingSettings>>(
                _accounts.Select(account => CopySharing(_sharing[account.Id])).ToArray());
    }

    /// <summary>Saves sample sharing choices in memory, without changing real accounts or local files.</summary>
    public Task<AccountSharingSettings> SaveAccountSharingAsync(
        AccountSharingSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        lock (_sync)
        {
            FindAccount(settings.AccountId);
            var normalized = settings with
            {
                CalendarIds = settings.CalendarIds?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            };
            _sharing[settings.AccountId] = normalized;
            return Task.FromResult(CopySharing(normalized));
        }
    }

    /// <summary>Returns the in-memory default search period, initially fourteen days.</summary>
    public Task<MailSearchPreferences> GetMailSearchPreferencesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
            return Task.FromResult(_mailSearchPreferences);
    }

    /// <summary>Saves a valid default search period for this preview session only.</summary>
    public Task<MailSearchPreferences> SaveMailSearchPreferencesAsync(
        MailSearchPreferences preferences, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        lock (_sync)
        {
            _mailSearchPreferences = preferences;
            return Task.FromResult(preferences);
        }
    }

    /// <summary>Returns clearly labeled sample calendar names for the local calendar picker.</summary>
    public Task<IReadOnlyList<ProviderCalendar>> ListAvailableCalendarsAsync(
        string accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!FindAccount(accountId).CalendarReadEnabled)
                throw new InvalidOperationException("This sample account does not include calendar access.");
            return Task.FromResult<IReadOnlyList<ProviderCalendar>>(_calendars[accountId].ToArray());
        }
    }

    /// <summary>Rejects read checks because sample accounts have no provider connections.</summary>
    public Task<AccountConnectionCheckResult> CheckConnectionsAsync(CancellationToken cancellationToken = default) =>
        Unsupported<AccountConnectionCheckResult>(cancellationToken);

    /// <summary>Reports both providers as unconfigured, without any credentials or real setup lookup.</summary>
    public Task<IReadOnlyList<ProviderSetupStatus>> ListProviderSetupAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ProviderSetupStatus>>(
            [new("google", false, null, false), new("microsoft", false, null, false)]);
    }

    /// <summary>Rejects provider setup without reading or importing the supplied source.</summary>
    public Task<ProviderSetupResult> ConfigureProviderAsync(
        string providerId, string source, CancellationToken cancellationToken = default) =>
        Unsupported<ProviderSetupResult>(cancellationToken);

    /// <summary>Rejects sign-in instead of simulating successful authentication.</summary>
    public Task<AccountConnectionResult> ConnectAccountAsync(
        string providerId, AccountConnectionOptions options, CancellationToken cancellationToken = default) =>
        Unsupported<AccountConnectionResult>(cancellationToken);

    /// <summary>Removes one sample account from this session without touching real accounts or credentials.</summary>
    public Task<AccountRemovalResult> RemoveAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        lock (_sync)
        {
            var removed = _accounts.RemoveAll(account => string.Equals(account.Id, accountId, StringComparison.Ordinal)) > 0;
            _sharing.Remove(accountId);
            _calendars.Remove(accountId);
            return Task.FromResult(new AccountRemovalResult(accountId, removed));
        }
    }

    /// <summary>Rejects mail searches because the preview supplies no mailbox contents.</summary>
    public Task<MailSearchResult> SearchMailAsync(MailSearchRequest request, CancellationToken cancellationToken = default) =>
        Unsupported<MailSearchResult>(cancellationToken);

    /// <summary>Rejects message reads because no mail is available in this preview.</summary>
    public Task<MailMessageResult> ReadMailAsync(MailReadRequest request, CancellationToken cancellationToken = default) =>
        Unsupported<MailMessageResult>(cancellationToken);

    /// <summary>Rejects assistant calendar reads; sample calendar names exist only for the local setup picker.</summary>
    public Task<CalendarListResult> ListCalendarsAsync(CalendarListRequest request, CancellationToken cancellationToken = default) =>
        Unsupported<CalendarListResult>(cancellationToken);

    /// <summary>Rejects appointment searches because the preview supplies no event contents.</summary>
    public Task<EventSearchResult> SearchEventsAsync(EventSearchRequest request, CancellationToken cancellationToken = default) =>
        Unsupported<EventSearchResult>(cancellationToken);

    /// <summary>Rejects appointment reads because no events are available in this preview.</summary>
    public Task<EventResult> ReadEventAsync(EventReadRequest request, CancellationToken cancellationToken = default) =>
        Unsupported<EventResult>(cancellationToken);

    private Account FindAccount(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return _accounts.SingleOrDefault(account => string.Equals(account.Id, accountId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The sample account is no longer available in this preview.", nameof(accountId));
    }

    private static AccountSharingSettings CopySharing(AccountSharingSettings settings) =>
        settings with { CalendarIds = settings.CalendarIds?.ToArray() };

    private static Task<T> Unsupported<T>(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<T>(cancellationToken)
            : Task.FromException<T>(new NotSupportedException(UnsupportedMessage));
}
