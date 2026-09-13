using MailMeUp.Core;
using MailMeUp.Desktop.Services;
using Xunit;

namespace MailMeUp.Tests;

public sealed class DemoMailMeUpApplicationTests
{
    [Fact]
    public async Task SavedSharingAndSearchPeriodDoNotEscapeThePreviewSession()
    {
        var changed = new DemoMailMeUpApplication();
        var otherSession = new DemoMailMeUpApplication();
        var original = (await changed.ListAccountSharingAsync()).First(settings => settings.Enabled);

        await changed.SaveAccountSharingAsync(original with { Enabled = false });
        await changed.SaveMailSearchPreferencesAsync(new(30));

        Assert.False((await changed.ListAccountSharingAsync()).Single(settings => settings.AccountId == original.AccountId).Enabled);
        Assert.DoesNotContain(await changed.ListSharedAccountsAsync(), account => account.Id == original.AccountId);
        Assert.Equal(30, (await changed.GetMailSearchPreferencesAsync()).DefaultLookbackDays);
        Assert.True((await otherSession.ListAccountSharingAsync()).Single(settings => settings.AccountId == original.AccountId).Enabled);
        Assert.Contains(await otherSession.ListSharedAccountsAsync(), account => account.Id == original.AccountId);
        Assert.Equal(14, (await otherSession.GetMailSearchPreferencesAsync()).DefaultLookbackDays);

        var reopened = new DemoMailMeUpApplication();
        Assert.True((await reopened.ListAccountSharingAsync()).Single(settings => settings.AccountId == original.AccountId).Enabled);
        Assert.Equal(14, (await reopened.GetMailSearchPreferencesAsync()).DefaultLookbackDays);
    }

    [Fact]
    public async Task CalendarSelectionCannotChangeWithoutAnExplicitSave()
    {
        var application = new DemoMailMeUpApplication();
        var settings = (await application.ListAccountSharingAsync()).First(item => item.CalendarIds is { Count: > 0 });
        var expectedCalendarIds = settings.CalendarIds!.ToArray();
        var editableSelection = Assert.IsType<string[]>(settings.CalendarIds);
        editableSelection[0] = "demo-unsaved-calendar";

        var reread = (await application.ListAccountSharingAsync()).Single(item => item.AccountId == settings.AccountId);

        Assert.Equal(expectedCalendarIds, reread.CalendarIds);

        var requestedSelection = new[] { expectedCalendarIds[0] };
        var saved = await application.SaveAccountSharingAsync(settings with { CalendarIds = requestedSelection });
        requestedSelection[0] = "demo-changed-after-save";
        Assert.IsType<string[]>(saved.CalendarIds)[0] = "demo-changed-returned-copy";

        var afterSave = (await application.ListAccountSharingAsync()).Single(item => item.AccountId == settings.AccountId);
        Assert.Equal([expectedCalendarIds[0]], afterSave.CalendarIds);
    }

    [Fact]
    public async Task RemovingASampleAccountDoesNotAffectOtherPreviewSessions()
    {
        var changed = new DemoMailMeUpApplication();
        var otherSession = new DemoMailMeUpApplication();
        var account = (await changed.ListAccountsAsync()).First(item => item.CalendarReadEnabled);

        var removed = await changed.RemoveAccountAsync(account.Id);

        Assert.True(removed.Removed);
        Assert.DoesNotContain(await changed.ListAccountsAsync(), item => item.Id == account.Id);
        Assert.DoesNotContain(await changed.ListAccountSharingAsync(), item => item.AccountId == account.Id);
        Assert.Contains(await otherSession.ListAccountsAsync(), item => item.Id == account.Id);
        Assert.NotEmpty(await otherSession.ListAvailableCalendarsAsync(account.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => changed.ListAvailableCalendarsAsync(account.Id));
    }

    [Fact]
    public async Task AccountAndCalendarPickerDataIsClearlySynthetic()
    {
        var application = new DemoMailMeUpApplication();
        var accounts = await application.ListAccountsAsync();

        Assert.InRange(accounts.Count, 3, 5);
        Assert.All(accounts, account =>
        {
            Assert.StartsWith("demo-", account.Id, StringComparison.Ordinal);
            Assert.EndsWith("@example.test", account.EmailAddress, StringComparison.Ordinal);
            Assert.Contains("(demo)", account.DisplayName, StringComparison.Ordinal);
        });
        foreach (var account in accounts.Where(item => item.CalendarReadEnabled))
        {
            var calendars = await application.ListAvailableCalendarsAsync(account.Id);
            Assert.NotEmpty(calendars);
            Assert.Single(calendars, calendar => calendar.Primary);
            Assert.All(calendars, calendar =>
            {
                Assert.StartsWith("demo-", calendar.ProviderCalendarId, StringComparison.Ordinal);
                Assert.Contains("(demo)", calendar.Name, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public async Task DemoReadinessDoesNotAdvertiseRealConnectionsOrCredentials()
    {
        var application = new DemoMailMeUpApplication();
        var status = application.GetStatus();

        Assert.True(application.IsDemo);
        Assert.Contains("demo", status.Stage, StringComparison.Ordinal);
        Assert.True(status.ReadOnly);
        Assert.False(status.CanConnectAccounts);
        Assert.All(status.Providers, provider =>
        {
            Assert.False(provider.AuthenticationAvailable);
            Assert.False(provider.MailReadAvailable);
            Assert.False(provider.CalendarReadAvailable);
        });
        var setup = await application.ListProviderSetupAsync();
        Assert.Equal(status.Providers.Select(provider => provider.Id), setup.Select(provider => provider.ProviderId));
        Assert.All(setup, provider =>
        {
            Assert.False(provider.Configured);
            Assert.False(provider.ProtectedSecretConfigured);
            Assert.Null(provider.ClientIdHint);
        });
    }

    [Fact]
    public async Task SignInSetupAndProviderReadsFailExplicitlyInsteadOfSimulatingSuccess()
    {
        var application = new DemoMailMeUpApplication();
        Func<Task>[] unsupportedActions =
        [
            () => application.ConfigureProviderAsync("google", "demo://example.test/client.json"),
            () => application.ConfigureProviderAsync("microsoft", "demo-application-id"),
            () => application.ConnectAccountAsync("google", new()),
            () => application.ConnectAccountAsync("microsoft", new()),
            () => application.CheckConnectionsAsync(),
            () => application.SearchMailAsync(new()),
            () => application.ReadMailAsync(new("demo-message")),
            () => application.ListCalendarsAsync(new()),
            () => application.SearchEventsAsync(new("2026-09-12T00:00:00Z", "2026-09-13T00:00:00Z")),
            () => application.ReadEventAsync(new("demo-event"))
        ];

        foreach (var action in unsupportedActions)
        {
            var error = await Assert.ThrowsAsync<NotSupportedException>(action);
            Assert.Contains("demo mode", error.Message, StringComparison.Ordinal);
            Assert.Contains("not connected", error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task InvalidSearchPeriodsDoNotReplaceTheSessionPreference(int days)
    {
        var application = new DemoMailMeUpApplication();
        await application.SaveMailSearchPreferencesAsync(new(30));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => application.SaveMailSearchPreferencesAsync(new(days)));

        Assert.Equal(30, (await application.GetMailSearchPreferencesAsync()).DefaultLookbackDays);
    }
}
