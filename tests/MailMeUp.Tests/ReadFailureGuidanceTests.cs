using MailMeUp.Application;
using MailMeUp.Core;
using Xunit;

namespace MailMeUp.Tests;

public sealed class ReadFailureGuidanceTests
{
    private const string PrivateDiagnostic = "private provider body and secret-token-example";

    [Theory]
    [InlineData(ReadFailureKind.SignInRequired, "sign_in_required", "sign in")]
    [InlineData(ReadFailureKind.AccessDenied, "access_denied", "permissions")]
    [InlineData(ReadFailureKind.ProviderUnavailable, "provider_unavailable", "try again")]
    [InlineData(ReadFailureKind.RateLimited, "rate_limited", "shorter date range")]
    [InlineData(ReadFailureKind.Network, "network_unavailable", "internet")]
    [InlineData(ReadFailureKind.Timeout, "read_timed_out", "shorter date range")]
    [InlineData(ReadFailureKind.LocalCredentialsUnavailable, "local_credentials_unavailable", "credential storage")]
    public void KnownFailuresHavePlainEnglishRecoveryAdvice(ReadFailureKind kind, string code, string action)
    {
        var advice = ReadFailureGuidance.Describe(kind);
        Assert.Equal(code, advice.Code);
        Assert.NotEmpty(advice.Explanation);
        Assert.Contains(action, advice.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ReadFailureKind.SignInRequired)]
    [InlineData(ReadFailureKind.AccessDenied)]
    [InlineData(ReadFailureKind.ProviderUnavailable)]
    [InlineData(ReadFailureKind.RateLimited)]
    [InlineData(ReadFailureKind.Network)]
    [InlineData(ReadFailureKind.Timeout)]
    [InlineData(ReadFailureKind.LocalCredentialsUnavailable)]
    public async Task PartialSearchPreservesHealthyMatchesAndSafeFailureCategory(ReadFailureKind kind)
    {
        var application = new MailMeUpApplication(new AccountStore(), [], [], [], [new MailReader(kind)], [], timeProvider: FixedTimeProvider.September2026);

        var result = await application.SearchMailAsync(new("sample"));

        Assert.False(result.CoverageComplete);
        Assert.Equal("google:healthy", Assert.Single(result.Items).AccountId);
        var failure = Assert.Single(result.FailedAccounts);
        Assert.Equal("google:failed", failure.AccountId);
        Assert.Equal(kind, failure.Kind);
        Assert.DoesNotContain(PrivateDiagnostic, failure.Reason, StringComparison.Ordinal);
        Assert.Contains(ReadFailureGuidance.Describe(kind).Action, failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedDetailRetainsItsCategoryWithoutConvertingItIntoAnEmptyMessage()
    {
        var application = new MailMeUpApplication(new AccountStore(), [], [], [], [new MailReader(ReadFailureKind.SignInRequired)], [], timeProvider: FixedTimeProvider.September2026);
        var search = await application.SearchMailAsync(new("sample", ["google:healthy"]));

        var error = await Assert.ThrowsAsync<ProviderReadException>(() => application.ReadMailAsync(new(Assert.Single(search.Items).Reference)));

        Assert.Equal(ReadFailureKind.SignInRequired, error.Kind);
    }

    private sealed class AccountStore : IAccountStore
    {
        public Task<IReadOnlyList<Account>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Account>>([
                new("google:failed", "google", "Failed sample", "failed@example.test", true),
                new("google:healthy", "google", "Healthy sample", "healthy@example.test", true)
            ]);

        public Task SaveAsync(Account account, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MailReader(ReadFailureKind failureKind) : IMailReader
    {
        public string ProviderId => "google";

        public Task<ProviderMailSearchPage> SearchAsync(Account account, ProviderMailQuery query, int limit, string? cursor, CancellationToken cancellationToken = default)
        {
            if (account.Id == "google:failed")
            {
                throw new ProviderReadException(PrivateDiagnostic, failureKind);
            }

            return Task.FromResult(new ProviderMailSearchPage([
                new("sample", "Synthetic subject", "sender@example.test", DateTimeOffset.Parse("2026-09-06T10:00:00Z"), "Synthetic preview")
            ], null));
        }

        public Task<ProviderMailMessage> ReadAsync(Account account, string providerMessageId, CancellationToken cancellationToken = default) =>
            throw new ProviderReadException(PrivateDiagnostic, failureKind);
    }
}
