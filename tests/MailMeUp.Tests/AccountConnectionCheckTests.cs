using MailMeUp.Application;
using MailMeUp.Core;
using Xunit;

namespace MailMeUp.Tests;

public sealed class AccountConnectionCheckTests
{
    [Fact]
    public async Task CheckConnectionsChecksEveryLocalAccountAndReturnsSafeFailures()
    {
        var google = new RecordingChecker("google");
        var microsoft = new RecordingChecker("microsoft", _ =>
            Task.FromException(new ProviderReadException("Synthetic private provider detail", ReadFailureKind.SignInRequired)));
        var application = CreateApplication(
            [
                new Account("google:one", "google", "Google", "one@example.test", true, true),
                new Account("microsoft:two", "microsoft", "Microsoft", "two@example.test", true, false)
            ],
            [google, microsoft]);

        var result = await application.CheckConnectionsAsync();

        Assert.Equal(["google:one"], google.CheckedAccountIds);
        Assert.Equal(["microsoft:two"], microsoft.CheckedAccountIds);
        Assert.Collection(
            result.Accounts,
            check =>
            {
                Assert.Equal("google:one", check.AccountId);
                Assert.True(check.Reachable);
                Assert.Null(check.FailureKind);
                Assert.Equal(true, check.MailReachable);
                Assert.Equal(true, check.CalendarReachable);
            },
            check =>
            {
                Assert.Equal("microsoft:two", check.AccountId);
                Assert.False(check.Reachable);
                Assert.Equal(ReadFailureKind.SignInRequired, check.FailureKind);
                Assert.Equal(false, check.MailReachable);
                Assert.Equal(ReadFailureKind.SignInRequired, check.MailFailureKind);
                Assert.Null(check.CalendarReachable);
            });
    }

    [Fact]
    public async Task CheckConnectionsMarksAccountsWithoutAProviderCheckerAsUnavailable()
    {
        var application = CreateApplication(
            [new Account("other:one", "other", "Other", "one@example.test", true, false)],
            []);

        var result = await application.CheckConnectionsAsync();

        var check = Assert.Single(result.Accounts);
        Assert.False(check.Reachable);
        Assert.Equal(ReadFailureKind.ProviderUnavailable, check.FailureKind);
    }

    private static MailMeUpApplication CreateApplication(
        IReadOnlyList<Account> accounts,
        IReadOnlyList<IAccountConnectionChecker> checkers) => new(
            new AccountStore(accounts),
            [],
            [],
            [],
            [],
            [],
            connectionCheckers: checkers);

    private sealed class AccountStore(IReadOnlyList<Account> accounts) : IAccountStore
    {
        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts);

        public Task SaveAsync(Account account, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class RecordingChecker(string providerId, Func<Account, Task>? action = null) : IAccountConnectionChecker
    {
        public List<string> CheckedAccountIds { get; } = [];

        public string ProviderId { get; } = providerId;

        public async Task<AccountConnectionCheck> CheckAsync(Account account, CancellationToken cancellationToken = default)
        {
            CheckedAccountIds.Add(account.Id);
            if (action is not null)
            {
                await action(account);
            }

            return new AccountConnectionCheck(
                account.Id,
                Reachable: true,
                MailReachable: account.MailReadEnabled ? true : null,
                CalendarReachable: account.CalendarReadEnabled ? true : null);
        }
    }
}
